using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Models.Enums;
using Xunit;

namespace HaliYikamaAPI.Tests;

public class ApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithIp(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private async Task<AuthResponseDto> RegisterCompanyAsync(HttpClient client, string name, string email, string password = "Password123!")
    {
        var res = await client.PostAsJsonAsync("/api/auth/company/register", new CompanyRegisterDto
        {
            Name = name,
            Email = email,
            Password = password,
            Phone = "05321112233",
            Address = "Test Mah. No:1"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts);
        Assert.NotNull(auth);
        return auth!;
    }

    [Fact]
    public async Task CompanyRegister_TurkishNameProducesAsciiPrefix_AndProfileAndPasswordWork()
    {
        var client = CreateClientWithIp("10.0.0.1");
        var auth = await RegisterCompanyAsync(client, "Çiçek Şelale Halı", "cicek@test.com", "IlkSifre1!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        // Profil oku — Türkçe karakterlerin ASCII'ye çevrildiğini doğrula
        var profile = await client.GetFromJsonAsync<CompanyProfileDto>("/api/company/profile", JsonOpts);
        Assert.NotNull(profile);
        Assert.Equal("CIC", profile!.CodePrefix);

        // Profil + WhatsAppPhoneNumberId güncelle
        var updateRes = await client.PutAsJsonAsync("/api/company/profile", new UpdateCompanyProfileDto
        {
            Name = "Çiçek Halı Yeni",
            Phone = "05329998877",
            Address = "Yeni Adres 123",
            WhatsAppPhoneNumberId = "PHONE_ID_12345"
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updatedProfile = await updateRes.Content.ReadFromJsonAsync<CompanyProfileDto>(JsonOpts);
        Assert.Equal("PHONE_ID_12345", updatedProfile!.WhatsAppPhoneNumberId);

        // Şifre değiştir
        var pwdRes = await client.PostAsJsonAsync("/api/company/change-password", new ChangePasswordDto
        {
            CurrentPassword = "IlkSifre1!",
            NewPassword = "YeniSifre2!"
        });
        Assert.Equal(HttpStatusCode.OK, pwdRes.StatusCode);

        // Eski şifre reddedilmeli, yeni şifre ile giriş başarılı olmalı
        var oldLogin = await client.PostAsJsonAsync("/api/auth/company/login", new CompanyLoginDto
        {
            Email = "cicek@test.com",
            Password = "IlkSifre1!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await client.PostAsJsonAsync("/api/auth/company/login", new CompanyLoginDto
        {
            Email = "cicek@test.com",
            Password = "YeniSifre2!"
        });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task MultiTenantIsolation_CompanyBCannotAccessCompanyAData()
    {
        var clientA = CreateClientWithIp("10.0.0.2");
        var clientB = CreateClientWithIp("10.0.0.3");

        var authA = await RegisterCompanyAsync(clientA, "Firma A", "firmaa@test.com");
        var authB = await RegisterCompanyAsync(clientB, "Firma B", "firmab@test.com");

        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.Token);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB.Token);

        var orderRes = await clientA.PostAsJsonAsync("/api/orders", new CreateOrderDto
        {
            CustomerName = "Gizli Müşteri",
            CustomerPhone = "05324445566",
            CustomerAddress = "Gizli Adres 12345",
            ServiceType = ServiceType.Hali,
            CarpetCount = 2
        });
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);
        var orderA = await orderRes.Content.ReadFromJsonAsync<OrderResponseDto>(JsonOpts);

        // Firma B kendi listesinde ve tekil sorguda Firma A siparişini görememeli
        var listB = await clientB.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders", JsonOpts);
        Assert.Empty(listB!);

        var getB = await clientB.GetAsync($"/api/orders/{orderA!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getB.StatusCode);

        var delB = await clientB.DeleteAsync($"/api/orders/{orderA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delB.StatusCode);
    }

    [Fact]
    public async Task DriverRbac_AndImmediateTokenRevocationOnDeactivation()
    {
        var adminClient = CreateClientWithIp("10.0.0.4");
        var driverClient = CreateClientWithIp("10.0.0.5");

        var adminAuth = await RegisterCompanyAsync(adminClient, "Rbac Halı", "rbac@test.com");
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);

        // Sürücü oluştur
        var drvRes = await adminClient.PostAsJsonAsync("/api/drivers", new CreateDriverDto
        {
            FullName = "Mehmet Sürücü",
            Phone = "05552223344",
            VehiclePlate = "06 TST 06"
        });
        Assert.Equal(HttpStatusCode.Created, drvRes.StatusCode);
        var driver = await drvRes.Content.ReadFromJsonAsync<DriverResponseDto>(JsonOpts);

        // Atanmamış bir sipariş oluştur
        var unassignedRes = await adminClient.PostAsJsonAsync("/api/orders", new CreateOrderDto
        {
            CustomerName = "Atanmamış Müşteri",
            CustomerPhone = "05441112233",
            CustomerAddress = "Çankaya Mah. No:5",
            ServiceType = ServiceType.Koltuk
        });
        var unassignedOrder = await unassignedRes.Content.ReadFromJsonAsync<OrderResponseDto>(JsonOpts);

        // Sürücü girişi
        var drvLoginRes = await driverClient.PostAsJsonAsync("/api/auth/driver/login", new DriverLoginDto
        {
            AccessCode = driver!.AccessCode
        });
        Assert.Equal(HttpStatusCode.OK, drvLoginRes.StatusCode);
        var drvAuth = await drvLoginRes.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOpts);
        driverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", drvAuth!.Token);

        // Sürücü sipariş oluşturamaz (403)
        var createAttempt = await driverClient.PostAsJsonAsync("/api/orders", new CreateOrderDto
        {
            CustomerPhone = "05320001122",
            CustomerAddress = "Deneme Adres 1234",
            ServiceType = ServiceType.Hali,
            CarpetCount = 1
        });
        Assert.Equal(HttpStatusCode.Forbidden, createAttempt.StatusCode);

        // Sürücü atanmamış siparişleri listeleyemez, arayamaz veya tekil çekemez
        var unassignedList = await driverClient.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders?unassignedOnly=true", JsonOpts);
        Assert.Empty(unassignedList!);

        var searchList = await driverClient.GetFromJsonAsync<List<OrderResponseDto>>("/api/orders/search?query=0544", JsonOpts);
        Assert.Empty(searchList!);

        var singleGet = await driverClient.GetAsync($"/api/orders/{unassignedOrder!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, singleGet.StatusCode);

        // Sürücü pasife alındığında elindeki JWT anında 401 dönmeli
        var deactivateRes = await adminClient.DeleteAsync($"/api/drivers/{driver.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deactivateRes.StatusCode);

        var meAfterDeactivate = await driverClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterDeactivate.StatusCode);
    }

    [Fact]
    public async Task OrderValidation_AndTerminalStatusFullLock()
    {
        var client = CreateClientWithIp("10.0.0.6");
        var auth = await RegisterCompanyAsync(client, "Validasyon Halı", "val@test.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        // Negatif halı adedi -> 400
        var negRes = await client.PostAsJsonAsync("/api/orders", new
        {
            customerPhone = "05321112233",
            customerAddress = "Test Adres 12345",
            serviceType = "Hali",
            carpetCount = -3
        });
        Assert.Equal(HttpStatusCode.BadRequest, negRes.StatusCode);

        // Geçersiz enum -> 400
        var enumRes = await client.PostAsJsonAsync("/api/orders", new
        {
            customerPhone = "05321112233",
            customerAddress = "Test Adres 12345",
            serviceType = 99
        });
        Assert.Equal(HttpStatusCode.BadRequest, enumRes.StatusCode);

        // Geçerli sipariş oluştur -> Tamamlandi yap -> fiyat/adres değişikliği bile engellenmeli
        var createRes = await client.PostAsJsonAsync("/api/orders", new CreateOrderDto
        {
            CustomerPhone = "05321112233",
            CustomerAddress = "Test Adres 12345",
            ServiceType = ServiceType.Hali,
            CarpetCount = 2
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(JsonOpts);

        var completeRes = await client.PutAsJsonAsync($"/api/orders/{order!.Id}", new UpdateOrderDto
        {
            CustomerPhone = order.CustomerPhone,
            CustomerAddress = order.CustomerAddress,
            ServiceType = order.ServiceType,
            CarpetCount = 2,
            TotalPrice = 500,
            Status = OrderStatus.Tamamlandi
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // Tamamlandı siparişin fiyatını değiştirmeyi dene -> 400
        var editPriceLocked = await client.PutAsJsonAsync($"/api/orders/{order.Id}", new UpdateOrderDto
        {
            CustomerPhone = order.CustomerPhone,
            CustomerAddress = order.CustomerAddress,
            ServiceType = order.ServiceType,
            CarpetCount = 2,
            TotalPrice = 999,
            Status = OrderStatus.Tamamlandi
        });
        Assert.Equal(HttpStatusCode.BadRequest, editPriceLocked.StatusCode);

        // Tamamlandı siparişte sürücü atamasını değiştirmeyi dene -> 400
        var assignLocked = await client.PatchAsJsonAsync($"/api/orders/{order.Id}/assign-driver", new AssignDriverDto
        {
            DriverId = null
        });
        Assert.Equal(HttpStatusCode.BadRequest, assignLocked.StatusCode);
    }

    [Fact]
    public async Task AuthRateLimiter_BlocksSameIpAfter15Requests_WithoutAffectingOtherIp()
    {
        var attackerClient = CreateClientWithIp("10.99.99.1");
        var normalClient = CreateClientWithIp("10.99.99.2");

        HttpStatusCode lastStatus = HttpStatusCode.OK;
        for (int i = 0; i < 16; i++)
        {
            var res = await attackerClient.PostAsJsonAsync("/api/auth/driver/login", new DriverLoginDto
            {
                AccessCode = "NOPE-999"
            });
            lastStatus = res.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);

        // Farklı X-Forwarded-For IP'si etkilenmemeli (401 döner, 429 değil)
        var otherRes = await normalClient.PostAsJsonAsync("/api/auth/driver/login", new DriverLoginDto
        {
            AccessCode = "NOPE-999"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, otherRes.StatusCode);
    }
}
