using System.IO.Pipes;
using System.Text;
using Infrastructure.Extention;
using Microsoft.Extensions.Logging;

namespace Infrastructure;

public class MetaTraderPipeClient
{
    public static string SendCommand(string command, ILogger<TradingJob> logger)
    {
        using var pipeClient = new NamedPipeClientStream(".", "MQL4_PIPE", PipeDirection.InOut);
        
        Console.WriteLine("Connecting to MQL4 pipe server...");
        pipeClient.Connect(5000);
        Console.WriteLine("Connected.");

        var requestBytes = Encoding.UTF8.GetBytes(command);
        pipeClient.Write(requestBytes, 0, requestBytes.Length);
        Console.WriteLine($"Sent: {command}");

        var responseBytes = new byte[512];
        var bytesRead = pipeClient.Read(responseBytes, 0, responseBytes.Length);
        var response = Encoding.UTF8.GetString(responseBytes, 0, bytesRead);
        Console.WriteLine($"Received: {response}");

        return response;
    }
}