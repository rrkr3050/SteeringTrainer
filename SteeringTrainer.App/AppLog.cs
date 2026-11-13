using System.IO;
using System.Text;

namespace SteeringTrainer.App;

internal static class AppLog
{
    private static readonly object Gate = new();
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteeringTrainer", "logs", $"app-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception loggingError)
            {
                // Logging must never turn an otherwise recoverable error into a crash.
                System.Diagnostics.Trace.WriteLine($"Log write failed: {loggingError}; {message}");
            }
        }
    }
}
