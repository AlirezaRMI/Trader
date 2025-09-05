using Domain.Polisy;
using Domain.Services;
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
    var chat = ctx.Configuration["Telegram:ChatId"];
    
    if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(chat))
    {
        cfg.WriteTo.Logger(lc => lc

            .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("ops"))

            .WriteTo.TelegramBot(
                botToken: token,
                chatId: chat,
                restrictedToMinimumLevel: LogEventLevel.Information
            )
        );
    }
});


builder.Services.AddSingleton<StrategyEngine>();
builder.Services.AddSingleton<DailyTradePolicy>();
builder.Services.AddScoped<TradingJob>();

builder.Services.AddHangfire(config => config.UseMemoryStorage());
builder.Services.AddHangfireServer(options => options.WorkerCount = 1);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseHangfireDashboard();

RecurringJob.AddOrUpdate<TradingJob>(
    "main-trading-cycle", 
    job => job.RunCycle(), 
    "*/10 * * * * *");

app.Run();