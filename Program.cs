using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Services.Auth;
using HaliYikamaAPI.Services.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────
//  Services
// ──────────────────────────────────────────────

// Entity Framework Core – SQL Server
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection yapılandırılmalıdır.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(defaultConnection));

// Reverse proxy (Nginx, Cloudflare, Azure, Railway vb.) arkasında gerçek istemci IP'si
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// HttpContext & Auth Services (Multi-Tenant & JWT)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();

// JWT Kimlik Doğrulama
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecretKey) || jwtSecretKey == "REPLACE_WITH_ENV_VAR_OR_USER_SECRET")
{
    if (builder.Environment.IsDevelopment())
    {
        jwtSecretKey = "HaliYikama_DevOnly_JWT_Key_2026_Must_Be_At_Least_256_Bits_Long!";
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:SecretKey yapılandırılmalıdır (ortam değişkeni veya gizli anahtar deposu kullanın).");
    }
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "HaliYikamaAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "HaliYikamaClients";

builder.Services.AddSingleton(new JwtSettings
{
    SecretKey = jwtSecretKey,
    Issuer = jwtIssuer,
    Audience = jwtAudience
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var companyIdStr = principal?.FindFirst("companyId")?.Value;
            if (!Guid.TryParse(companyIdStr, out var companyId))
            {
                context.Fail("Geçersiz firma kimliği.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var companyActive = await db.Companies
                .AsNoTracking()
                .AnyAsync(c => c.Id == companyId && c.IsActive, context.HttpContext.RequestAborted);

            if (!companyActive)
            {
                context.Fail("Firma hesabı aktif değil.");
                return;
            }

            var role = principal?.FindFirst(ClaimTypes.Role)?.Value;
            if (role == "Driver")
            {
                var driverIdStr = principal?.FindFirst("driverId")?.Value;
                if (!Guid.TryParse(driverIdStr, out var driverId))
                {
                    context.Fail("Geçersiz sürücü kimliği.");
                    return;
                }

                var driverActive = await db.Drivers
                    .AsNoTracking()
                    .AnyAsync(d => d.Id == driverId && d.CompanyId == companyId && d.IsActive,
                        context.HttpContext.RequestAborted);

                if (!driverActive)
                {
                    context.Fail("Sürücü hesabı pasife alınmıştır.");
                }
            }
        }
    };
});

builder.Services.AddAuthorization();

// Rate Limiting — IP bazlı brute-force ve DDoS koruması
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Çok fazla istek gönderildi. Lütfen kısa bir süre sonra tekrar deneyin."
        }, cancellationToken);
    };

    // Tüm /api/* uç noktaları için global IP bazlı limit (statik dosyalar hariç)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return RateLimitPartition.GetNoLimiter("static");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });

    // Kimlik doğrulama uç noktaları için daha sıkı brute-force limiti
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Controllers – Enum'ları string olarak serialize et
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Halı Yıkama API",
        Version = "v1",
        Description = "Çoklu Firma (SaaS) ve Sürücü Destekli Halı ve Koltuk Yıkama Sipariş Yönetim API'si",
        Contact = new OpenApiContact
        {
            Name = "Halı Yıkama Yönetim",
        }
    });

    // JWT Bearer yetkilendirme şemasını Swagger'a ekle
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization başlığı. Format: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // XML dokümantasyon yorumlarını aktif et
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Enum'ları string olarak göster
    options.UseInlineDefinitionsForEnums();
});

// CORS — Geliştirme ortamında tüm originlere izin ver, production'da yapılandırmadan oku
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("X-Total-Count");
        }
        else if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .WithExposedHeaders("X-Total-Count");
        }
    });
});

// ──────────────────────────────────────────────
//  WhatsApp Cloud API Servisleri
// ──────────────────────────────────────────────

builder.Services.Configure<WhatsAppSettings>(
    builder.Configuration.GetSection(WhatsAppSettings.SectionName));

builder.Services.AddSingleton<IConversationStateMachine, ConversationStateMachine>();
builder.Services.AddSingleton<IWebhookSignatureValidator, WebhookSignatureValidator>();
builder.Services.AddHttpClient<IWhatsAppSender, WhatsAppSender>();
builder.Services.AddSingleton<IWhatsAppMessageQueue, WhatsAppMessageQueue>();
builder.Services.AddHostedService<WhatsAppBackgroundWorker>();

var app = builder.Build();

// ──────────────────────────────────────────────
//  Middleware Pipeline
// ──────────────────────────────────────────────

// Global exception handler — stack trace sızmasını önle
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (exceptionFeature is not null)
        {
            logger.LogError(exceptionFeature.Error, "İşlenmeyen hata oluştu");
        }

        await context.Response.WriteAsJsonAsync(new
        {
            message = "Sunucuda bir hata oluştu. Lütfen daha sonra tekrar deneyin."
        });
    });
});

// İstek loglama — yalnızca geliştirme ortamında
if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RequestLogger");
        logger.LogInformation(
            "→ {Method} {Path}{Query}",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString);
        await next();
        logger.LogInformation(
            "← {Method} {Path} → {StatusCode}",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode);
    });
}

// Swagger UI — yalnızca geliştirme ortamında
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Halı Yıkama API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Halı Yıkama API – Swagger";
    });
}

// Reverse proxy başlıklarını işle (Rate Limiter ve HTTPS yönlendirmesinden önce)
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowAll");

// wwwroot klasöründen statik dosya sunumu
app.UseDefaultFiles(); // index.html otomatik sunulsun
app.UseStaticFiles();

// Rate limiter
app.UseRateLimiter();

// Kimlik doğrulama & Yetkilendirme middleware sırası
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Sağlık kontrolü — DB bağlantısını da kontrol et
app.MapGet("/health", async (ApplicationDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (canConnect)
        {
            return Results.Ok(new
            {
                status = "healthy",
                database = true,
                timestamp = DateTime.UtcNow
            });
        }

        return Results.Json(new
        {
            status = "degraded",
            database = false,
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new
        {
            status = "degraded",
            database = false,
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// ──────────────────────────────────────────────
//  Veritabanı Migration'larını Uygula (Yalnızca Development)
// ──────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInit");
        logger.LogError(ex, "Veritabanı migration uygulanamadı — uygulama DB olmadan başlatılıyor");
    }
}

await app.RunAsync();

/// <summary>WebApplicationFactory entegrasyon testleri için giriş noktası bildirimi.</summary>
public partial class Program { }
