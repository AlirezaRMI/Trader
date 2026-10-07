namespace Application.Runtime;

public sealed record ExecutionResult(string Status, long? Ticket, string Note);
