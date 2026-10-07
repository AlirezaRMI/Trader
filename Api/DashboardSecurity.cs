using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Api;

public sealed class DashboardSecurity(string password, string bridgeToken, bool development)
{
    public bool PasswordRequired => !string.IsNullOrWhiteSpace(password);
    public bool MatchesPassword(string candidate) => FixedEquals(password, candidate);

    public async Task AuthorizeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/bridge"))
        {
            if (!FixedEquals("Bearer " + bridgeToken, context.Request.Headers.Authorization.ToString()))
            { context.Response.StatusCode = 401; return; }
            if (context.Request.Headers.ContainsKey("Origin"))
            { context.Response.StatusCode = 403; return; }
        }
        else if (path.StartsWithSegments("/api"))
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !SameOrigin(context.Request))
            { context.Response.StatusCode = 403; return; }
            var publicAuth = path == "/api/auth/login" || path == "/api/auth/session";
            var local = development && !PasswordRequired && IsLoopback(context);
            if (!publicAuth && !local && context.User.Identity?.IsAuthenticated != true)
            { context.Response.StatusCode = 401; return; }
            if (publicAuth && !PasswordRequired && !local)
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
        }
        await next(context);
    }

    public static bool SameOrigin(HttpRequest request)
    {
        if (request.Headers["Sec-Fetch-Site"].ToString() == "cross-site") return false;
        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0) return true; // Non-browser clients still need a valid session or loopback.
        return Uri.TryCreate(origin, UriKind.Absolute, out var supplied) &&
               Uri.TryCreate(request.Scheme + "://" + request.Host, UriKind.Absolute, out var expected) &&
               supplied.Scheme == expected.Scheme && supplied.Authority.Equals(expected.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(HttpContext context)
    {
        var host = context.Request.Host.Host;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        return localHost && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);
    }

    private static bool FixedEquals(string expected, string candidate) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
}
