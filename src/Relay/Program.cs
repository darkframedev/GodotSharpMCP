using System.Diagnostics;
using GodotSharp.Relay;

// Disable stdout buffering — MCP stdio transport requires line-flushed output.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Kill any stale relay instances left over from a previous session (editor crash,
// hot-reload, OpenCode restart, etc.) before we try to claim the named pipe.
foreach (var stale in Process.GetProcessesByName("GodotSharp.Relay")
    .Where(p => p.Id != Environment.ProcessId))
{
    try
    {
        stale.Kill(entireProcessTree: true);
        stale.WaitForExit(2000);
        Console.Error.WriteLine($"[Relay] Killed stale relay process (PID {stale.Id}).");
    }
    catch { /* already gone */ }
    finally { stale.Dispose(); }
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await using var pipeServer = new PipeServer();
var mcpServer = new McpServer(pipeServer);

// Connect to the Godot plugin in the background — reconnects automatically
// whenever the editor closes and reopens without restarting the relay.
_ = pipeServer.ListenLoopAsync(cts.Token).ContinueWith(t =>
{
    if (t.IsFaulted)
        Console.Error.WriteLine($"[Relay] Pipe listener failed: {t.Exception?.GetBaseException().Message}");
}, TaskScheduler.Default);

await mcpServer.RunAsync(cts.Token);

