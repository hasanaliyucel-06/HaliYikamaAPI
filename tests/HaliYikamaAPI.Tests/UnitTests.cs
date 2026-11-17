using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Models;
using HaliYikamaAPI.Models.Enums;
using HaliYikamaAPI.Services.Auth;
using HaliYikamaAPI.Services.WhatsApp;
using Xunit;

namespace HaliYikamaAPI.Tests;

public class UnitTests
{
    [Fact]
    public void PasswordHasher_ValidatesCorrectWrongAndMalformedHashes()
    {
        var hash = PasswordHasher.HashPassword("Deniz123!");

        Assert.True(PasswordHasher.VerifyPassword("Deniz123!", hash));
        Assert.False(PasswordHasher.VerifyPassword("Yanlis123!", hash));
        Assert.False(PasswordHasher.VerifyPassword("Deniz123!", "bozuk-format"));
        Assert.False(PasswordHasher.VerifyPassword("Deniz123!", "not_base64!:also_not_base64!"));
        Assert.False(PasswordHasher.VerifyPassword("Deniz123!", ""));
    }

    [Fact]
    public async Task ConversationStateMachine_FirstGreetingDoesNotShowInvalidChoiceError()
    {
        using var sp = BuildInMemoryServiceProvider();
        var sm = new ConversationStateMachine(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConversationStateMachine>.Instance);

        var reply = await sm.ProcessMessageAsync("905551112233", "Merhaba", "Ahmet");

        Assert.DoesNotContain("Geçersiz seçim", reply);
        Assert.Contains("Deniz Halı Yıkama", reply);

        // İkinci mesaj da geçersizse artık hata göstermeli
        var secondReply = await sm.ProcessMessageAsync("905551112233", "abc", "Ahmet");
        Assert.Contains("Geçersiz seçim", secondReply);
    }

    [Fact]
    public async Task ConversationStateMachine_PersistsStateInDbAcrossInstancesAndRoutesMultiTenant()
    {
        using var sp = BuildInMemoryServiceProvider();
        Guid secondCompanyId;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var secondCompany = new Company
            {
                Id = Guid.NewGuid(),
                Name = "Yıldız Halı Yıkama",
                Email = "yildiz@test.com",
                PasswordHash = PasswordHasher.HashPassword("Test123!"),
                Phone = "05320000001",
                CodePrefix = "YLD",
                WhatsAppPhoneNumberId = "WA_PHONE_999",
                IsActive = true
            };
            secondCompanyId = secondCompany.Id;
            db.Companies.Add(secondCompany);
            await db.SaveChangesAsync();
        }

        // İlk instance ile hizmet + adet adımlarını geç
        var sm1 = new ConversationStateMachine(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConversationStateMachine>.Instance);

        var r1 = await sm1.ProcessMessageAsync("905559998877", "Merhaba", "Veli", "WA_PHONE_999");
        Assert.Contains("Yıldız Halı Yıkama", r1);

        var r2 = await sm1.ProcessMessageAsync("905559998877", "1", "Veli", "WA_PHONE_999");
        Assert.Contains("Kaç adet halı", r2);

        var r3 = await sm1.ProcessMessageAsync("905559998877", "4", "Veli", "WA_PHONE_999");
        Assert.Contains("4 adet halı", r3);

        // Sunucu yeniden başlamış gibi YENİ ConversationStateMachine instance'ı oluştur
        var sm2 = new ConversationStateMachine(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConversationStateMachine>.Instance);

        // 500 karakterden uzun adres reddedilmeli
        var tooLong = await sm2.ProcessMessageAsync("905559998877", new string('A', 501), "Veli", "WA_PHONE_999");
        Assert.Contains("çok uzun", tooLong);

        // Geçerli adres siparişi doğru firmaya oluşturmalı ve konuşma kaydını temizlemeli
        var r4 = await sm2.ProcessMessageAsync("905559998877", "Atatürk Mah. İnönü Cad. No:12 Daire:4", "Veli", "WA_PHONE_999");
        Assert.Contains("başarıyla oluşturuldu", r4);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.SingleAsync(o => o.CompanyId == secondCompanyId);
            Assert.Equal("05559998877", order.CustomerPhone);
            Assert.Equal("Veli", order.CustomerName);
            Assert.Equal(ServiceType.Hali, order.ServiceType);
            Assert.Equal(4, order.CarpetCount);
            Assert.Empty(await db.WhatsAppConversations.ToListAsync());
        }
    }

    private static ServiceProvider BuildInMemoryServiceProvider()
    {
        var services = new ServiceCollection();
        var dbName = $"UnitTestDb_{Guid.NewGuid():N}";
        services.AddDbContext<ApplicationDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        return sp;
    }
}
