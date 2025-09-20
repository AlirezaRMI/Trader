using Domain.Polisy;
using Domain.Services;
using Domain.Services.Interfaces;
using Hangfire;
using Hangfire.MemoryStorage;
using Infrastructure.Extention;
using Infrastructure.Telegram;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var token = ctx.Configuration["Telegram:BotToken"];
    var chatIds = ctx.Configuration.GetSection("Telegram:ChatIds").Get<List<string>>();

    if (string.IsNullOrWhiteSpace(token) || chatIds is not {Count: > 0}) return;
    foreach (var chatId in chatIds)
    {
        cfg.WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("ops"))
            .WriteTo.TelegramBot(
                botToken: token,
                chatId: chatId,
                restrictedToMinimumLevel: LogEventLevel.Information
            )
        );
    }
});


builder.Services.AddScoped<IndicatorBasedEngine>();
builder.Services.AddSingleton<DailyTradePolicy>();
builder.Services.AddScoped<PriceActionAnalyzer>();
builder.Services.AddScoped<PriceActionEngine>();
builder.Services.AddScoped<MarketSupervisor>();
builder.Services.AddScoped<TradingJob>();

builder.Services.AddHangfire(config => config.UseMemoryStorage());
builder.Services.AddHangfireServer(options => options.WorkerCount = 1);
builder.Services.AddSingleton<IEconomicCalendarService, EconomicCalendarService>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseHangfireDashboard();

RecurringJob.AddOrUpdate<TradingJob>(
    "main-trading-cycle", 
    job => job.RunCycle(), 
    "*/1 * * * *");

app.Run();