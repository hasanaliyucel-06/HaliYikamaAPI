using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Services.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────
//  Services
// ──────────────────────────────────────────────

// Entity Framework Core – SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

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
        Description = "Halı ve Koltuk Yıkama İşletmesi Sipariş Yönetim API'si",
        Contact = new OpenApiContact
        {
            Name = "Halı Yıkama Yönetim",
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

// CORS — Geliştirme ortamında tüm originlere izin ver, production'da kısıtla
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Production'da sadece bilinen originlere izin ver
            // TODO: Kendi domain'inizi buraya ekleyin
            policy.WithOrigins("https://yourdomain.com")
                  .AllowAnyMethod()
                  .AllowAnyHeader();
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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowAll");

// wwwroot klasöründen statik dosya sunumu
app.UseDefaultFiles(); // index.html otomatik sunulsun
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// Sağlık kontrolü — DB bağlantısını da kontrol et
app.MapGet("/health", async (ApplicationDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        return Results.Ok(new
        {
            status = canConnect ? "healthy" : "degraded",
            database = canConnect,
            timestamp = DateTime.UtcNow
        });
    }
    catch
    {
        return Results.Ok(new
        {
            status = "degraded",
            database = false,
            timestamp = DateTime.UtcNow
        });
    }
});

// ──────────────────────────────────────────────
//  Veritabanı Migration'larını Uygula (Development)
//  NOT: DB hatası uygulamanın başlamasını engellemesin
// ──────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseInit");
    logger.LogError(ex, "Veritabanı migration uygulanamadı — uygulama DB olmadan başlatılıyor");
}

await app.RunAsync();
