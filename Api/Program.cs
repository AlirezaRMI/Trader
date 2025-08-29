using System.Threading.RateLimiting;
using Application.Behaviors;
using Application.Handler;
using Application.Validation;
using Domain;
using Domain.Polisy;
using Domain.Services;
using FluentValidation;
using Infrastructure.Behaviors;
using Infrastructure.Extention;
using Infrastructure.Maine;
using Infrastructure.Memory;
using Infrastructure.Telegram;
using MediatR;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Exceptions;

var builder = WebApplication.CreateBuilder(args);

// -------------------- Serilog --------------------
SelfLog.Enable(msg => Console.Error.WriteLine("SERILOG-SELFLOG: " + msg));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithExceptionDetails()
    .Enrich.WithProperty("Application", "Trader.Api")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateLogger();

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .Enrich.WithProperty("Application", "Trader.Api")
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);

    var token = ctx.Configuration["Telegram:BotToken"];
    var chat = ctx.Configuration["Telegram:ChatId"];
    if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(chat))
    {
        cfg.WriteTo.Logger(lc =>
            TelegramBotSinkExtensions.TelegramBot(lc.Filter.ByIncludingOnly(e => e.Properties.ContainsKey("ops"))
                    .WriteTo, botToken: token,
                chatId: chat,
                restrictedToMinimumLevel: LogEventLevel.Information,
                batchSizeLimit: 10,
                periodSeconds: 2,
                queueLimit: 2000,
                disableNotification: true
            )
        );
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "Trader API";
    options.Version = "v1";
    options.DocumentName = "trader";
    options.Description = "Auto-trading API (cTrader / Evaluate / Exec)";

    // اگر خواستی امنیت اضافه کنی:
    // options.AddSecurity("ApiKey", new OpenApiSecurityScheme { ... });
    // options.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("ApiKey"));
});

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<EvaluateValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<EvalExecValidator>();

// MediatR Handlers
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(EvaluateHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(EvalExecHandler).Assembly);
});

// Domain services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<DailyTradePolicy>();
builder.Services.AddSingleton<StrategyEngine>();


builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

builder.Services.AddSingleton<ISeriesStore, InMemorySeriesStore>();

builder.Services.Configure<CTraderOpenApiOptions>(builder.Configuration.GetSection("CTrader"));
builder.Services.AddSingleton<CTraderOpenApiSession>();

var ctSection = builder.Configuration.GetSection("CTrader");
var accessToken = ctSection["AccessToken"];
var accountIdStr = ctSection["AccountId"];
var hasCtAuth = !string.IsNullOrWhiteSpace(accessToken)
                && long.TryParse(accountIdStr, out var parsedId)
                && parsedId > 0;

if (hasCtAuth)
    builder.Services.AddSingleton<IOrderExecutionPort, CTraderOrderExecutionPort>();
else
    builder.Services.AddSingleton<IOrderExecutionPort, PaperOrderExecutionPort>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// -------------------- Middleware pipeline --------------------
app.UseStaticFiles();
app.UseRateLimiter();
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
});

app.MapControllers();

app.UseOpenApi(cfg =>
{
    cfg.Path = "/openapi/docs.json";
    cfg.DocumentName = "trader";
});

app.MapScalarApiReference(opt =>
{
    opt.WithOpenApiRoutePattern("/openapi/docs.json");
    opt.Title = "Trader API";
});

app.Run();