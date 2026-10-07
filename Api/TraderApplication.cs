using System.Security.Cryptography;
using Application.Broker;
using Application.Runtime;
using Domain.Services;
using Infrastructure.Bridge;
using Infrastructure.Persistence;
using Infrastructure.Runtime;
using Infrastructure.Research;
using Infrastructure.Telegram;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Trader.Protocol;

namespace Api;

public static class TraderApplication
{
    public static WebApplication Build(string[] args)
    {
        var current = Directory.GetCurrentDirectory();
        var root = FindRoot(current);
        var apiRoot = Directory.Exists(Path.Combine(root, "Api")) ? Path.Combine(root, "Api") : root;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ApplicationName = typeof(TraderApplication).Assembly.GetName().Name,
            ContentRootPath = apiRoot, WebRootPath = Path.Combine(apiRoot, "wwwroot")
        });
        builder.Configuration.AddInMemoryCollection(LocalEnvironment.Read(Path.Combine(root, ".env")));
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(args);
        if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5080");
        var data = Path.GetFullPath(builder.Configuration["Runtime:DataDirectory"] ?? "data", root);
        var password = builder.Configuration["Security:Password"] ?? "";
        var token = builder.Configuration["Bridge:Token"] ?? "";
        if (!builder.Environment.IsDevelopment() && (password.Length < 12 || token.Length < 32))
            throw new InvalidOperationException(
                "Production requires Security__Password (12+ characters) and Bridge__Token (32+ characters)");
        Directory.CreateDirectory(data);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = LocalEnvironment.ReadBridgeKey(data);
            if (string.IsNullOrEmpty(token))
            {
                token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var keyPath = Path.Combine(data, "bridge.key");
                File.WriteAllText(keyPath, token);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        builder.Services.AddSingleton(new DashboardSecurity(password, token, builder.Environment.IsDevelopment()));
        builder.Services.AddSingleton(new RiskProfileCatalog(builder.Configuration.GetSection("RiskProfiles").Get<RiskProfile[]>()
            ?? throw new InvalidOperationException("RiskProfiles configuration is required")));
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (System.Net.IPAddress.TryParse(builder.Configuration["Security:TrustedProxy"], out var proxy))
                options.KnownProxies.Add(proxy);
        });
        builder.Host.UseSerilog((_, log) => log.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning).Enrich.FromLogContext()
                .WriteTo.Console().WriteTo.File(Path.Combine(data, "logs", "trader-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14),
            preserveStaticLogger: true);
        builder.Services.AddDataProtection().SetApplicationName("Trader.Dashboard")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "session-keys")))
            .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
            {
                EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
                ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
            });
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "trader.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
                        {PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true}));
            options.RejectionStatusCode = 429;
        });
        builder.Services.AddSingleton<AgentGateway>();
        builder.Services.AddSingleton<IBrokerGateway>(sp => sp.GetRequiredService<AgentGateway>());
        builder.Services.AddSingleton(_ => new SqliteTradeStore(Path.Combine(data, "trader.db")));
        builder.Services.AddSingleton<ITradeStore>(sp => sp.GetRequiredService<SqliteTradeStore>());
        builder.Services.AddSingleton<IndicatorBasedEngine>();
        builder.Services.AddSingleton<PriceActionEngine>();
        builder.Services.AddSingleton<MarketSupervisor>();
        builder.Services.AddSingleton<PriceActionAnalyzer>();
        builder.Services.AddSingleton<TradeExecutor>();
        builder.Services.AddSingleton<ResearchRunner>();
        builder.Services.AddSingleton<TradingRuntime>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TradingRuntime>());
        builder.Services.AddSingleton<TelegramOutboxWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramOutboxWorker>());

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseWebSockets(new WebSocketOptions {KeepAliveInterval = TimeSpan.FromSeconds(10)});
        app.UseAuthentication();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";
            await app.Services.GetRequiredService<DashboardSecurity>().AuthorizeAsync(context, next);
        });
        app.UseDefaultFiles();
        var dashboardFiles = new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
        };
        app.UseStaticFiles(dashboardFiles);
        app.MapDashboardEndpoints();
        app.MapResearchEndpoints();
        app.MapGet("/health/live", () => Results.Ok(new {status = "alive", dashboardProtocol = 2}));
        // API misses must never fall through to the SPA's index.html and masquerade as successful JSON.
        app.MapFallback("/api/{**path}", () => Results.NotFound(new {error = "مسیر API موجود نیست؛ بک‌اند و داشبورد را با نسخهٔ یکسان دوباره اجرا کن."}));
        app.MapFallbackToFile("index.html", dashboardFiles);
        return app;
    }

    private static string FindRoot(string current)
    {
        foreach (var candidate in new[] {current, AppContext.BaseDirectory})
        {
            var directory = new DirectoryInfo(candidate);
            for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Trader.sln")))
                    return directory.FullName;
        }

        return Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")) ? AppContext.BaseDirectory : current;
    }
}
