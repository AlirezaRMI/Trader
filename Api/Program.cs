using System.Threading.RateLimiting;
using Api;
using Application.Handler;
using Application.Validation;
using Domain;
using Domain.Polisy;
using Domain.Services;
using FluentValidation;
using Infrastructure;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.ApiServiceProvider(builder.Configuration, builder.Environment);
builder.Services.AddOpenApiDocument(options =>
{
    options.Title = "EasyHub API";
    options.Version = "v1";
    options.Description = "Simple and Secure API for EasyHub";

    options.AddSecurity("JWT", new NSwag.OpenApiSecurityScheme
    {
        Type = NSwag.OpenApiSecuritySchemeType.ApiKey,
        Name = "Authorization",
        In = NSwag.OpenApiSecurityApiKeyLocation.Header,
        Description = "Enter JWT token like: Bearer {your token}"
    });

    options.OperationProcessors.Add(
        new NSwag.Generation.Processors.Security.AspNetCoreOperationSecurityScopeProcessor("JWT"));
});
// Validators
builder.Services.AddValidatorsFromAssemblyContaining<EvaluateValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<EvalExecValidator>();

// MediatR
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(EvaluateHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(EvalExecHandler).Assembly);
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<DailyTradePolicy>();
builder.Services.AddSingleton<StrategyEngine>();


builder.Services.AddSingleton<ISeriesStore, InMemorySeriesStore>();
builder.Services.AddSingleton<IOrderExecutionPort, PaperOrderExecutionPort>();

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
app.UseStaticFiles();
app.UseRateLimiter();
app.MapControllers();

// OpenAPI JSON + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();
