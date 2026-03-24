using System.Diagnostics;
using System.Threading.Tasks;

namespace SDCardFormatterApp;

public static class Exec
{
    public static async Task<int> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        using var process = Process.Start(psi);
        if (process == null) return -1;

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public static async Task<string> RunCommandWithOutputAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "";
        return await process.StandardOutput.ReadToEndAsync();
    }
}