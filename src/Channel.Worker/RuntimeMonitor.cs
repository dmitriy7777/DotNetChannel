using System.Text.Json;

namespace Channel.Worker;

internal static class RuntimeMonitor
{
    public static async Task Run(string directory, CancellationTokenSource shutdown)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    Write(directory, started, "Running");
                    var stopPath = Path.Combine(directory, "stop.request");
                    if (File.Exists(stopPath) && (await File.ReadAllTextAsync(stopPath)).Trim() == Environment.ProcessId.ToString())
                    {
                        File.Delete(stopPath); shutdown.Cancel(); break;
                    }
                }
                catch (IOException) { /* A stop request may still be open for writing. Retry on the next tick. */ }
                await Task.Delay(1000, shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Monitoring stopped: {e.Message}");
            shutdown.Cancel();
        }
        finally { Write(directory, started, "Stopped"); }
    }

    private static void Write(string directory, DateTimeOffset started, string status)
    {
        var path = Path.Combine(directory, "runtime.json");
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { ProcessId = Environment.ProcessId, StartedUtc = started, HeartbeatUtc = DateTimeOffset.UtcNow, Status = status }));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException) { }
    }
}
