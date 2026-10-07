using System.Security.Cryptography;
using System.Text;

namespace Trader.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = FindRoot(args);
        if (root is null)
        {
            MessageBox.Show("لانچر را از فایل Start-Trader.cmd در پوشهٔ پروژه اجرا کن.", "Trader",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
        using var instance = new Mutex(true, $"Local\\Trader.Launcher.{identity}", out var created);
        if (!created)
        {
            MessageBox.Show("پنجرهٔ کنترل این پروژه از قبل باز است.", "Trader", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            System.Windows.Forms.Application.Run(new LauncherForm(root, !args.Contains("--manual")));
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    private static string? FindRoot(string[] args)
    {
        var index = Array.IndexOf(args, "--root");
        var candidate = index >= 0 && index + 1 < args.Length ? args[index + 1] : AppContext.BaseDirectory;
        var directory = new DirectoryInfo(Path.GetFullPath(candidate));
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Api", "Api.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "Bridge", "Bridge.csproj")))
                return directory.FullName;
        return null;
    }
}
