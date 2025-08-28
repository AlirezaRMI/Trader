using Application;
using Application.Command;
using Application.Handler; // EvaluateCommand, DecisionDto
using Application.Validation;         // EvaluateValidator, EvaluateHandler
using Domain; // EvalExecCommand, EvalExecResult, EvalExecHandler, EvalExecValidator
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;                 // IOrderExecutionPort (برای PaperPort)
using FluentValidation;
using Infrastructure;
using MediatR;

var builder = WebApplication.CreateBuilder(args);

// Validators
builder.Services.AddValidatorsFromAssemblyContaining<EvaluateValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<EvalExecValidator>();

// MediatR (هر دو هندلر)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(EvaluateHandler).Assembly);
    cfg.RegisterServicesFromAssembly(typeof(EvalExecHandler).Assembly);
});

// Domain services
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<DailyTradePolicy>();
builder.Services.AddSingleton<StrategyEngine>();

// Infra
builder.Services.AddSingleton<ISeriesStore, InMemorySeriesStore>();
builder.Services.AddSingleton<IOrderExecutionPort, PaperOrderExecutionPort>(); 

var app = builder.Build();

app.MapPost("/evaluate", async (EvaluateCommand cmd, IValidator<EvaluateCommand> v, IMediator mediator) =>
{
    var res = await v.ValidateAsync(cmd);
    if (!res.IsValid) return Results.ValidationProblem(res.ToDictionary());

    DecisionDto decision = await mediator.Send(cmd);
    var csv = $"{decision.Action},{decision.Size},{decision.Sl},{decision.Tp},{decision.Note.Replace(',', ' ')}";
    return Results.Text(csv, "text/plain");
});

app.MapPost("/eval-exec", async (EvalExecCommand cmd, IValidator<EvalExecCommand> v, IMediator mediator) =>
{
    var res = await v.ValidateAsync(cmd);
    if (!res.IsValid) return Results.ValidationProblem(res.ToDictionary());

    object? result = await mediator.Send(cmd);
    return Results.Json(result);
});

app.Run();
