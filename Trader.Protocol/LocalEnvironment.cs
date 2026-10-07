namespace Trader.Protocol;

public static class LocalEnvironment
{
    // Explicit .env loading for laptop launches, never logging values.
    public static Dictionary<string, string?> Read(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals < 1) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim().Trim('"', '\'');
            var configKey = key.Replace("__", ":");
            if (key == "TELEGRAM_BOT_TOKEN") configKey = "Telegram:BotToken";
            if (key == "DASHBOARD_PASSWORD") configKey = "Security:Password";
            if (key == "BRIDGE_TOKEN") configKey = "Bridge:Token";
            if (key == "TRUSTED_PROXY") configKey = "Security:TrustedProxy";
            if (key.StartsWith("TELEGRAM_CHAT_IDS_", StringComparison.Ordinal))
                configKey = "Telegram:ChatIds:" + key["TELEGRAM_CHAT_IDS_".Length..];
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) values[configKey] = value;
        }
        return values;
    }

    public static string ReadBridgeKey(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "bridge.key");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }
}
