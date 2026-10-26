using System.IO;
using System.IO.Pipes;

namespace CtrlCpp.Services;

/// <summary>单实例（命名 Mutex）+ 命名管道 IPC（show / settings / exit）。</summary>
public sealed class SingleInstanceService : IDisposable
{
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    public event Action<string>? CommandReceived;

    public bool TryAcquirePrimary()
    {
        _mutex = new Mutex(true, @"Local\CtrlCpp.Instance", out var created);
        if (created) { StartServer(); return true; }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static bool TrySendCommand(string command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "CtrlCpp.IPC", PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch { return false; }
    }

    private void StartServer()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream("CtrlCpp.IPC", PipeDirection.In);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        CommandReceived?.Invoke(line.Trim());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogService.LogEx("IPC 服务异常", ex);
                    try { await Task.Delay(500, token); } catch (OperationCanceledException) { break; }
                }
                finally
                {
                    if (server != null) try { server.Dispose(); } catch { /* 忽略 */ }
                }
            }
        }, token);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* 忽略 */ }
        try
        {
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { /* 非拥有线程释放会抛异常，忽略 */ }
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch { /* 忽略 */ }
    }
}
