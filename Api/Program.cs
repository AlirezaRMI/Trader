using Api;

if (args is ["--health-check"])
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try { Environment.ExitCode = (await http.GetAsync("http://127.0.0.1:8080/health/live")).IsSuccessStatusCode ? 0 : 1; }
    catch (HttpRequestException) { Environment.ExitCode = 1; }
    catch (TaskCanceledException) { Environment.ExitCode = 1; }
}
else await TraderApplication.Build(args).RunAsync();
