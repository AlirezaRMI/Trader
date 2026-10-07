using Bridge;
using Trader.Protocol;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddInMemoryCollection(LocalEnvironment.Read(".env"));
builder.Configuration.AddEnvironmentVariables();
var port = builder.Configuration.GetValue("Bridge:LocalPort", 8766);
if (port is < 1024 or > 65535) throw new InvalidOperationException("Invalid local MT4 port");
builder.Services.AddSingleton(sp => new TerminalServer(port, sp.GetRequiredService<ILogger<TerminalServer>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<TerminalServer>());
builder.Services.AddHostedService<BackendConnection>();
await builder.Build().RunAsync();