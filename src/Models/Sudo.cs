using System.Diagnostics;
using System.Threading.Tasks;

namespace SDCardFormatterApp;

public static class Sudo
{
    public static string Password { get; set; }

    public static async Task<int> RunWithPasswordAsync(string command)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"-S sh -c \"{command}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi);
        if (process == null) return -1;

        await process.StandardInput.WriteLineAsync(Password);
        await process.StandardInput.FlushAsync();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public static async Task<string> RunWithPasswordAndOutAsync(string command)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"-S sh -c \"{command}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi);
        if (process == null) return string.Empty;

        await process.StandardInput.WriteLineAsync(Password);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();
        
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? await process.StandardOutput.ReadToEndAsync() : string.Empty;
    }
}