Console.OutputEncoding = System.Text.Encoding.UTF8;
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
try { await Channel.Worker.Application.Run(args, shutdown.Token); }
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { Console.WriteLine("Остановлено."); }
catch (Exception e) { Console.Error.WriteLine($"Ошибка: {e.Message}"); Environment.ExitCode = 1; }
