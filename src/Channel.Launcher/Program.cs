using System.Diagnostics;
using System.Text;

// Windows subsystem launcher: no visible console, no PowerShell execution policy changes.
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "settings.example.json"))) root = root.Parent;
if (root is null) return 1;
var logDir = Path.Combine(root.FullName, "data", "logs");
Directory.CreateDirectory(logDir);
try
{
    var assembly = Path.Combine(root.FullName, "src", "Channel.Worker", "bin", "Release", "net10.0", "Channel.Worker.exe");
    var start = new ProcessStartInfo(assembly) { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
    start.ArgumentList.Add("--root"); start.ArgumentList.Add(root.FullName);
    // A task scheduler process may have an older environment than a newly opened terminal.
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(key)) start.Environment["OPENAI_API_KEY"] = key;
    }
    var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
    using var stdout = new StreamWriter(Path.Combine(logDir, $"service-{stamp}.out.log"), append: true, Encoding.UTF8) { AutoFlush = true };
    using var stderr = new StreamWriter(Path.Combine(logDir, $"service-{stamp}.err.log"), append: true, Encoding.UTF8) { AutoFlush = true };
    using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
    File.WriteAllText(Path.Combine(root.FullName, "data", "worker.pid"), worker.Id.ToString());
    await Task.WhenAll(Pump(worker.StandardOutput, stdout), Pump(worker.StandardError, stderr), worker.WaitForExitAsync());
    return worker.ExitCode;
}
catch (Exception e)
{
    File.AppendAllText(Path.Combine(logDir, "launcher-error.log"), $"{DateTimeOffset.Now:O} {e.Message}{Environment.NewLine}");
    return 1;
}

static async Task Pump(StreamReader reader, StreamWriter writer)
{
    while (await reader.ReadLineAsync() is { } line) await writer.WriteLineAsync(line);
}