using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Channel.Admin;

public record ServiceStatus(string TaskState, bool AutoStart, int? ProcessId, DateTimeOffset? Started, DateTimeOffset? Heartbeat, string? Error);

public sealed class ServiceController(string root, string dataDirectory)
{
    public ServiceStatus Read()
    {
        var taskState = "Не найдена"; var enabled = false; string? error = null;
        try { WithTask(task => { taskState = (int)task.State switch { 4 => "Работает", 3 => "Готова", 2 => "Ожидание запуска", 1 => "Отключена", _ => "Неизвестно" }; enabled = (bool)task.Enabled; }); }
        catch (Exception e) { error = "Задача Windows недоступна: " + e.Message; }
        using var worker = FindWorker();
        DateTimeOffset? started = worker is null ? null : worker.StartTime.ToUniversalTime();
        DateTimeOffset? heartbeat = null;
        try
        {
            using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "runtime.json")));
            if (worker is not null && runtime.RootElement.GetProperty("ProcessId").GetInt32() == worker.Id)
            { started = runtime.RootElement.GetProperty("StartedUtc").GetDateTimeOffset(); heartbeat = runtime.RootElement.GetProperty("HeartbeatUtc").GetDateTimeOffset(); }
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new(taskState, enabled, worker?.Id, started, heartbeat, error);
    }

    public async Task Start()
    {
        using var worker = FindWorker(); if (worker is not null) return;
        var stopPath = Path.Combine(dataDirectory, "stop.request");
        if (File.Exists(stopPath)) File.Delete(stopPath);
        WithTask(task => { if (!(bool)task.Enabled) throw new InvalidOperationException("Задача Windows отключена. Включите её в Планировщике заданий."); task.Run(null); });
        for (var i = 0; i < 40; i++)
        {
            using var check = FindWorker(); if (check is not null) return;
            await Task.Delay(500);
        }
        throw new InvalidOperationException("Процесс не появился за 20 секунд. Откройте журнал ошибок запуска.");
    }

    public async Task Stop()
    {
        using var worker = FindWorker();
        if (worker is not null)
        {
            Directory.CreateDirectory(dataDirectory);
            var stopPath = Path.Combine(dataDirectory, "stop.request");
            await File.WriteAllTextAsync(stopPath + ".tmp", worker.Id.ToString());
            File.Move(stopPath + ".tmp", stopPath, overwrite: true);
            try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { throw new InvalidOperationException("Штатная остановка ещё не завершена. Подождите и обновите состояние. Процесс принудительно не завершался."); }
        }
        // Also stop a queued startup or launcher still running after the worker exited.
        WithTask(task => { if ((int)task.State is 2 or 4) task.Stop(0); });
    }

    private Process? FindWorker()
    {
        var expected = Path.GetFullPath(Path.Combine(root, "src", "Channel.Worker", "bin", "Release", "net10.0", "Channel.Worker.exe"));
        foreach (var process in Process.GetProcessesByName("Channel.Worker"))
        {
            try { if (!process.HasExited && string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return process; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            process.Dispose();
        }
        return null;
    }

    private static void WithTask(Action<dynamic> action)
    {
        object? service = null, folder = null, task = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Планировщик Windows не найден."));
            ((dynamic)service!).Connect(); folder = ((dynamic)service!).GetFolder("\\"); task = ((dynamic)folder).GetTask("DotNetChannel"); action((dynamic)task);
        }
        finally { foreach (var obj in new[] { task, folder, service }) if (obj is not null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj); }
    }
}
