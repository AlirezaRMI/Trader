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

    public async Task<string> SendCommandAsync(string command)
    {
        if (!_pipeClient.IsConnected)
        {
            throw new InvalidOperationException("Pipe is not connected.");
        }

        try
        {
            var requestBytes = Encoding.UTF8.GetBytes(command);
            _pipeClient.Write(requestBytes, 0, requestBytes.Length);
            await _pipeClient.FlushAsync();
            logger.LogInformation("C#: Sent command: '{Command}'", command);
            
            var responseBytes = new byte[8192];
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bytesRead = await _pipeClient.ReadAsync(responseBytes, 0, responseBytes.Length, cancellationTokenSource.Token);
            
            if (bytesRead > 0)
            {
                var response = Encoding.UTF8.GetString(responseBytes, 0, bytesRead).TrimEnd('\0');
                logger.LogInformation("C#: Received response: '{Response}'", response);
                return response;
            }
            else
            {
                logger.LogError("C#: No bytes read from MT4 (possible disconnect).");
                return "ERROR,NoResponse";
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogError("C#: Timeout! No response from MT4 in 15 seconds.");
            return "ERROR,Timeout";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "C#: An error occurred during SendCommand.");
            if (!_pipeClient.IsConnected)
            {
                logger.LogWarning("Pipe seems broken. Disposing for next run.");
                Dispose();
            }
            return $"ERROR,{ex.Message}";
        }
    }

    [Obsolete("Use SendCommandAsync instead")]
    public string SendCommand(string command) => SendCommandAsync(command).GetAwaiter().GetResult();

    public void Dispose()
    {
        _pipeClient?.Dispose();
    }
}