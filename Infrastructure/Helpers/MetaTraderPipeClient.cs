using Microsoft.Extensions.Logging;
using System.IO.Pipes;
using System.Text;

namespace Infrastructure.Helpers;

public sealed class MetaTraderPipeClient(ILogger logger) : IDisposable
{
    private const string PipeName = "MQL4_PIPE";
    private readonly NamedPipeClientStream _pipeClient = new(".", PipeName, PipeDirection.InOut);

    public void Connect()
    {
        if (_pipeClient.IsConnected) return;
        logger.LogInformation("C#: Attempting to connect to MQL4...");
        _pipeClient.Connect(5000);
        logger.LogInformation("C#: Connection established.");
    }

    public string SendCommand(string command)
    {
        if (!_pipeClient.IsConnected)
        {
            throw new InvalidOperationException("Pipe is not connected.");
        }

        try
        {
            var requestBytes = Encoding.UTF8.GetBytes(command);

            _pipeClient.Write(requestBytes, 0, requestBytes.Length);
            _pipeClient.WaitForPipeDrain();
            logger.LogInformation("C#: Sent command: '{Command}'", command);
            
            var responseBytes = new byte[4096];
            var streamReader = new StreamReader(_pipeClient);
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var readTask = streamReader.BaseStream.ReadAsync(responseBytes, 0, responseBytes.Length, cancellationTokenSource.Token);

            readTask.Wait(cancellationTokenSource.Token); 

            if (readTask.IsCompletedSuccessfully)
            {
                var bytesRead = readTask.Result;
                var response = Encoding.UTF8.GetString(responseBytes, 0, bytesRead);
                logger.LogInformation("C#: Received response: '{Response}'", response);
                return response;
            }
            else
            {
                logger.LogError("C#: Timeout! No response received from MT4 in 10 seconds.");
                return "ERROR,Timeout";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "C#: An error occurred during SendCommand.");
            if (!_pipeClient.IsConnected)
            {
                 logger.LogWarning("Pipe seems to be broken. Disposing client for next run.");
                 Dispose();
            }
            return $"ERROR,{ex.Message}";
        }
    }

    public void Dispose()
    {
        _pipeClient?.Dispose();
    }
}