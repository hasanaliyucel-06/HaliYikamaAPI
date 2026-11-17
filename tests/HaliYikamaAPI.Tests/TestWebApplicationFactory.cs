using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Services.WhatsApp;

namespace HaliYikamaAPI.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"HaliYikamaTestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // EF Core 9: SQL Server sağlayıcı yapılandırmasını tamamen kaldırıp InMemory kaydet
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Testlerde gerçek Meta API çağrısı yapma
            services.RemoveAll<IWhatsAppSender>();
            services.AddSingleton<IWhatsAppSender, NoOpWhatsAppSender>();
        });
    }

    private sealed class NoOpWhatsAppSender : IWhatsAppSender
    {
        public Task SendTextMessageAsync(string recipientPhone, string message, string? phoneNumberIdOverride = null)
            => Task.CompletedTask;
    }
}
