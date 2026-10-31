using Channel.Worker;

namespace Channel.Tests;

public class RuntimeMonitorFileLockTests
{
    [Fact]
    public async Task TemporarilyLockedStopFileDoesNotKillTheMonitor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DotNetMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var shutdown = new CancellationTokenSource();
        Task? monitor = null;
        try
        {
            using (var request = new FileStream(Path.Combine(directory, "stop.request"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
                await request.WriteAsync(bytes); await request.FlushAsync();
                monitor = RuntimeMonitor.Run(directory, shutdown);
                Assert.False(monitor.IsCompleted);
            }
            await monitor.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shutdown.IsCancellationRequested);
        }
        finally
        {
            shutdown.Cancel(); if (monitor is not null) await monitor;
            Directory.Delete(directory, recursive: true);
        }
    }
}
