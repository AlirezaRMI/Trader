using System.Security.Claims;
using System.Text.Json;
using Application.Runtime;
using Application.Broker;
using Infrastructure.Bridge;
using Infrastructure.Runtime;
using Infrastructure.Telegram;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Trader.Protocol;

namespace Api;

public static class DashboardEndpoints
{
    public sealed record LoginRequest(string Password);
    public sealed record SettingsUpdateRequest(RuntimeSettings Settings, RuntimeSettings ExpectedSettings);
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/session", (HttpContext context, DashboardSecurity security) =>
            Results.Ok(new { authenticated = !security.PasswordRequired || context.User.Identity?.IsAuthenticated == true, passwordRequired = security.PasswordRequired }));
        app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, DashboardSecurity security) =>
        {
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
                return Results.BadRequest(new { error = "HTTPS is required" });
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 || !security.MatchesPassword(request.Password))
                return Results.Unauthorized();
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Ok(new { authenticated = true });
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });
        app.MapGet("/api/snapshot", (TradingRuntime runtime, TelegramOutboxWorker telegram) => Results.Ok(Enrich(runtime, telegram)));
        app.MapGet("/api/ui", () => Results.Ok(new { modamAvailable = File.Exists(Path.Combine(app.Environment.WebRootPath, "fonts", "MODAM-REGULAR.TTF")) ||
            File.Exists(Path.Combine(app.Environment.WebRootPath, "fonts", "Modam.woff2")) }));
        app.MapGet("/api/market/history", async (string symbol, int timeframe, IBrokerGateway gateway, TradingRuntime runtime, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(symbol) || SettingsValidator.Validate(new() { Symbol = symbol }) is not null || timeframe is not (1 or 5 or 15 or 30 or 60 or 240 or 1440))
                return Results.BadRequest(new { error = "Invalid symbol or timeframe" });
            try
            {
                var account = runtime.Snapshot.Account ?? throw new BrokerException("No current account");
                return Results.Ok(await BrokerContext.InvokeAsync(gateway, account, "GET_HISTORY", symbol + "," + timeframe + ",250", context.RequestAborted));
            }
            catch (BrokerException error) { return Results.Conflict(new { error = error.Message }); }
        });
        app.MapGet("/api/journal", (int? limit, ITradeStore store) => Results.Ok(store.ReadJournal(limit ?? 200)));
        app.MapGet("/api/journal/page", (int? limit, long? before, ITradeStore store) =>
        {
            try { return Results.Ok(store.ReadJournalPage(limit ?? 200, before)); }
            catch (ArgumentException error) { return Results.BadRequest(new {error = error.Message}); }
        });
        app.MapGet("/api/trades/history", (int? limit, string? cursor, ITradeStore store) =>
        {
            try { return Results.Ok(store.ReadTradeHistory(limit ?? 50, cursor)); }
            catch (ArgumentException error) { return Results.BadRequest(new {error = error.Message}); }
        });
        app.MapGet("/api/risk-profiles", (RiskProfileCatalog profiles) => Results.Ok(profiles.Profiles));
        app.MapPost("/api/risk-profiles/{id}/apply", (string id, RuntimeSettings expectedSettings, RiskProfileCatalog profiles, TradingRuntime runtime) =>
        {
            var profile = profiles.Find(id);
            if (profile is null) return Results.BadRequest(new {error = "Unknown risk profile"});
            try { return Results.Ok(runtime.ApplyRiskProfile(profile, expectedSettings)); }
            catch (ArgumentException error) { return Results.BadRequest(new {error = error.Message}); }
            catch (InvalidOperationException error) { return Results.Conflict(new {error = error.Message}); }
        });
        app.MapPost("/api/settings", (SettingsUpdateRequest request, TradingRuntime runtime, RiskProfileCatalog profiles) =>
        {
            if (request.Settings is null || request.ExpectedSettings is null)
                return Results.BadRequest(new {error = "Settings and expectedSettings are required; refresh the dashboard before saving"});
            try { runtime.UpdateSettings(profiles.Normalize(request.Settings), request.ExpectedSettings); return Results.Ok(runtime.Snapshot.Settings); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        });
        app.MapPost("/api/control/{action}", async (string action, TradingRuntime runtime, HttpContext context) =>
        {
            try
            {
                switch (action)
                {
                    case "start": await runtime.SetEntriesEnabledAsync(true, context.RequestAborted); break;
                    case "pause": await runtime.SetEntriesEnabledAsync(false, context.RequestAborted); break;
                    case "refresh": await runtime.RefreshAsync(context.RequestAborted); break;
                    case "reset-risk": await runtime.ResetRiskAsync(context.RequestAborted); break;
                    default: return Results.BadRequest(new { error = "Unknown action" });
                }
                return Results.Ok(runtime.Snapshot);
            }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        });
        app.MapGet("/api/events", async (HttpContext context, TradingRuntime runtime, TelegramOutboxWorker telegram) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache, no-transform";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                do
                {
                    await context.Response.WriteAsync("event: snapshot\ndata: " +
                        JsonSerializer.Serialize(Enrich(runtime, telegram), WebSocketFrames.JsonOptions) + "\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                } while (await timer.WaitForNextTickAsync(context.RequestAborted));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        app.MapGet("/bridge/ws", async (HttpContext context, AgentGateway gateway) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await gateway.AttachAsync(socket, context.RequestAborted);
        });
    }

    private static RuntimeSnapshot Enrich(TradingRuntime runtime, TelegramOutboxWorker telegram) => runtime.Snapshot with
    {
        TelegramConfigured = telegram.Status.Configured,
        TelegramDeliveryEnabled = telegram.Status.DeliveryEnabled,
        TelegramError = telegram.Status.LastError,
        PendingNotifications = telegram.Status.Pending
    };
}
