using System.Text.Json;
using Channel.Worker;

namespace Channel.Tests;

public class RuntimeMonitorTests
{
    [Fact]
    public async Task StopRequestCancelsOnlyTheMatchingWorkerAndWritesStoppedState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DotNetMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var shutdown = new CancellationTokenSource();
            var monitor = RuntimeMonitor.Run(directory, shutdown);
            await File.WriteAllTextAsync(Path.Combine(directory, "stop.request"), Environment.ProcessId.ToString());
            await monitor.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shutdown.IsCancellationRequested);
            Assert.False(File.Exists(Path.Combine(directory, "stop.request")));
            using var runtime = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "runtime.json")));
            Assert.Equal("Stopped", runtime.RootElement.GetProperty("Status").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
