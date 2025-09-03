using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Text;

namespace Infrastructure.Helpers;

public static class MetaTraderPipeClient
{
    private const string PipeName = "MQL4_PIPE";

    public static string SendCommand(string command, ILogger logger)
    {
        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        
        try
        {
            logger.LogInformation("C#: Attempting to connect to MQL4 pipe server...");
            pipeClient.Connect(5000); 
            logger.LogInformation("C#: Connection successful.");

            // ارسال دستور به MQL4
            var requestBytes = Encoding.UTF8.GetBytes(command);
            pipeClient.Write(requestBytes, 0, requestBytes.Length);
            logger.LogInformation("C#: Sent command to MT4: '{Command}'", command);

            pipeClient.WaitForPipeDrain();

            var responseBytes = new byte[512];
            var bytesRead = pipeClient.Read(responseBytes, 0, responseBytes.Length);
            var response = Encoding.UTF8.GetString(responseBytes, 0, bytesRead);
            logger.LogInformation("C#: Received response from MT4: '{Response}'", response);

            return response;
        }
        catch (TimeoutException)
        {
            logger.LogError("C#: Connection timed out. Is the EA running on a chart in MT4 with a smiling face?");
            return "ERROR,Connection timed out";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "C#: An error occurred while communicating with the MQL4 pipe.");
            return $"ERROR,{ex.Message}";
        }
    }
}