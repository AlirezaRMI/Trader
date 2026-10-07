using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace Trader.Launcher;

internal sealed record LocalServiceState(bool ApiReady, bool BridgePortOpen, bool OwnsApi, bool OwnsBridge, bool ApiOutdated);

/// <summary>Controls only child processes created by this launcher; never enables trading.</summary>
internal sealed class LocalServices(string root) : IDisposable
{
    public const string DashboardUrl = "http://127.0.0.1:5080";
    public const string DashboardLaunchUrl = DashboardUrl + "/?v=20261007-ui2";

    private readonly HttpClient _http = new(new HttpClientHandler {UseProxy = false, AllowAutoRedirect = false})
        {Timeout = TimeSpan.FromSeconds(2)};

    private readonly List<Process> _children = [];
    private readonly Dictionary<Process, Task> _output = [];
    private Process? _api;
    private Process? _bridge;
    private bool _apiOutdated;
    public string LogDirectory { get; } = Path.Combine(root, "data", "launcher-logs");
    public bool HasOwnedServices => IsRunning(_api) || IsRunning(_bridge);
    public event Action<string>? Progress;

    public async Task<LocalServiceState> ReadStateAsync(CancellationToken token)
    {
        return new(await IsApiReadyAsync(token), IsPortOpen(8766), IsRunning(_api), IsRunning(_bridge), _apiOutdated);
    }

    public async Task StartApiAsync(CancellationToken token)
    {
        if (await IsApiReadyAsync(token))
        {
            Progress?.Invoke("برنامه از قبل آماده است؛ فرایند دیگری را متوقف نمی‌کنم.");
            return;
        }

        if (_apiOutdated)
            throw new InvalidOperationException("API قدیمی هنوز اجراست؛ آن را از لانچر قبلی یا ترمینال خودش متوقف کن و دوباره اجرای برنامه را بزن. بستن تب مرورگر کافی نیست.");

        if (IsRunning(_api)) throw new InvalidOperationException("برنامه هنوز پاسخ نمی‌دهد؛ گزارش اجرا را بررسی کن.");
        if (IsPortOpen(5080)) throw new InvalidOperationException("پورت 5080 در اختیار برنامهٔ دیگری است.");
        Progress?.Invoke("در حال آماده‌سازی برنامه… اولین اجرا ممکن است کمی طول بکشد.");
        await BuildAsync("Api", token);
        token.ThrowIfCancellationRequested();
        _api = StartService("Api", ["--environment", "Development", "--urls", DashboardUrl]);
        try
        {
            await WaitUntilAsync(_api, IsApiReadyAsync, token);
            Progress?.Invoke("برنامه آماده است؛ ورود معاملاتی را خودت از داشبورد کنترل می‌کنی.");
        }
        catch
        {
            await StopAndDrainAsync(_api);
            throw;
        }
    }

    public async Task StartBridgeAsync(CancellationToken token)
    {
        await StartApiAsync(token);
        if (IsRunning(_bridge))
        {
            if (!IsPortOpen(8766)) throw new InvalidOperationException("پل اجرا شده، اما پورت MT4 هنوز آماده نیست.");
            return;
        }

        if (IsPortOpen(8766))
        {
            Progress?.Invoke("پورت 8766 از قبل باز است؛ وضعیت اتصال MT4 را در داشبورد بررسی کن.");
            return;
        }

        Progress?.Invoke("در حال آماده‌سازی پل متاتریدر…");
        await BuildAsync("Bridge", token);
        token.ThrowIfCancellationRequested();
        _bridge = StartService("Bridge", []);
        try
        {
            // A TCP probe would become TerminalServer's active EA session and disconnect MT4.
            await WaitUntilAsync(_bridge, _ => Task.FromResult(IsPortOpen(8766)), token);
            Progress?.Invoke("پل آماده است. اتصال حساب و EA را در داشبورد بررسی کن.");
        }
        catch
        {
            await StopAndDrainAsync(_bridge);
            throw;
        }
    }

    public async Task StopOwnedAsync()
    {
        await StopAndDrainAsync(_bridge);
        await StopAndDrainAsync(_api);
        Progress?.Invoke("سرویس‌های همین پنجره متوقف شدند؛ معاملات بروکر بسته نشدند.");
    }

    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) {UseShellExecute = true});

    private async Task BuildAsync(string project, CancellationToken token)
    {
        var info = CreateStartInfo();
        foreach (var arg in new[]
                 {
                     "build", Path.Combine(root, project, $"{project}.csproj"), "-c", "Release",
                     "--nologo", "--verbosity", "minimal"
                 }) info.ArgumentList.Add(arg);
        using var process = new Process {StartInfo = info};
        if (!process.Start()) throw new InvalidOperationException("اجرای ابزار .NET ممکن نشد.");
        var output = CaptureAsync(process, $"{project.ToLowerInvariant()}-build.log");
        try
        {
            await process.WaitForExitAsync(token);
            await output;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ساخت {project} ناموفق بود؛ جزئیات در پوشهٔ گزارش‌هاست.");
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process);
            await output;
            throw;
        }
    }

    private Process StartService(string project, string[] args)
    {
        var info = CreateStartInfo();
        info.ArgumentList.Add(Path.Combine(root, ".artifacts", "bin", project, "release", $"{project}.dll"));
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process {StartInfo = info};
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"اجرای {project} ممکن نشد.");
        }

        _children.Add(process);
        _output[process] = CaptureAsync(process, $"{project.ToLowerInvariant()}.log");
        return process;
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var local = Path.Combine(root, ".artifacts", "dotnet", "dotnet.exe");
        var info = new ProcessStartInfo(File.Exists(local) ? local : "dotnet")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            Environment =
            {
                ["DOTNET_CLI_HOME"] = Path.Combine(root, ".artifacts", "dotnet-home"),
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0",
                ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                ["Runtime__DataDirectory"] = Path.Combine(root, "data"),
                ["Bridge__BackendUrl"] = "ws://127.0.0.1:5080/bridge/ws",
                ["Bridge__LocalPort"] = "8766"
            }
        };
        if (File.Exists(local)) info.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(local)!;
        return info;
    }

    private async Task CaptureAsync(Process process, string file)
    {
        StreamWriter? writer = null;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            writer = new StreamWriter(Path.Combine(LogDirectory, file), append: true, Encoding.UTF8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Progress?.Invoke("ذخیرهٔ گزارش اجرا ممکن نشد؛ دسترسی پوشهٔ data را بررسی کن.");
        }

        try
        {
            using var gate = new SemaphoreSlim(1);

            async Task PumpAsync(StreamReader reader)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    await gate.WaitAsync();
                    try
                    {
                        if (writer is not null)
                        {
                            try
                            {
                                await writer.WriteLineAsync($"{DateTimeOffset.UtcNow:O} {line}");
                                await writer.FlushAsync();
                            }
                            catch (IOException)
                            {
                                try
                                {
                                    await writer.DisposeAsync();
                                }
                                catch (IOException)
                                {
                                }

                                writer = null;
                                Progress?.Invoke("نوشتن گزارش متوقف شد؛ فضای دیسک را بررسی کن.");
                            }
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }
            }

            await Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Progress?.Invoke("ذخیرهٔ گزارش اجرا ممکن نشد؛ دسترسی پوشهٔ data را بررسی کن.");
        }
        finally
        {
            if (writer is not null)
                try
                {
                    await writer.DisposeAsync();
                }
                catch (IOException)
                {
                }
        }
    }

    private static async Task WaitUntilAsync(Process process, Func<CancellationToken, Task<bool>> ready,
        CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(40))
        {
            token.ThrowIfCancellationRequested();
            if (!IsRunning(process))
                throw new InvalidOperationException("سرویس زودتر از انتظار بسته شد؛ گزارش اجرا را ببین.");
            if (await ready(token)) return;
            await Task.Delay(400, token);
        }

        throw new InvalidOperationException("سرویس در زمان مقرر آماده نشد؛ گزارش اجرا را ببین.");
    }

    private async Task<bool> IsApiReadyAsync(CancellationToken token)
    {
        _apiOutdated = false;
        try
        {
            using var response = await _http.GetAsync($"{DashboardUrl}/health/live", token);
            if (!response.IsSuccessStatusCode) return false;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token),
                cancellationToken: token);
            var alive = document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("status", out var status) &&
                   status.ValueKind == JsonValueKind.String && status.GetString() == "alive";
            _apiOutdated = alive && !(document.RootElement.TryGetProperty("dashboardProtocol", out var protocol) &&
                protocol.ValueKind == JsonValueKind.Number && protocol.TryGetInt32(out var version) && version == 2);
            return alive && !_apiOutdated;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static bool IsPortOpen(int port) => IPGlobalProperties.GetIPGlobalProperties()
        .GetActiveTcpListeners().Any(x => x.Port == port &&
                                          (IPAddress.IsLoopback(x.Address) || x.Address.Equals(IPAddress.Any) ||
                                           x.Address.Equals(IPAddress.IPv6Any)));

    private static bool IsRunning(Process? process)
    {
        try
        {
            return process is not null && !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (!IsRunning(process)) return;
        process!.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private async Task StopAndDrainAsync(Process? process)
    {
        await StopProcessAsync(process);
        if (process is not null && _output.TryGetValue(process, out var output)) await output;
    }

    public void Dispose()
    {
        _http.Dispose();
        foreach (var child in _children) child.Dispose();
    }
}
