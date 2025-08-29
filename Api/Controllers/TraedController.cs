using Application.Command;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Tags("Traed Endpoint")]
[Route("api/traed")]
public sealed class TradeController(
    IMediator mediator,
    IValidator<EvaluateCommand> evalV,
    IValidator<EvalExecCommand> execV)
    : ControllerBase
{
    [HttpPost("evaluate")]
    [EndpointName("evaluate")]
    [EndpointSummary("Evaluate a trade")]
    [EndpointDescription("Evaluate a trade when you have money")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Evaluate([FromBody] EvaluateCommand cmd)
    {
        var res = await evalV.ValidateAsync(cmd);
        var decision = await mediator.Send(cmd); 
        var csv = $"{decision.Action},{decision.Size},{decision.Sl},{decision.Tp},{decision.Note.Replace(',', ' ')}";
        return Content(csv, "text/plain");
    }

    
    [HttpPost("eval-exec")]
    [EndpointName("eval-exec")]
    [EndpointSummary("Evaluate a trade")]
    [EndpointDescription("Evaluate a trade when you have money")]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(EvalExecResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EvalExec([FromBody] EvalExecCommand cmd)
    {
        var res = await execV.ValidateAsync(cmd);
        var result = await mediator.Send(cmd);
        return Ok(result);
    }
}