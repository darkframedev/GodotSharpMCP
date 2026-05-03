using System.Diagnostics;
using System.IO;
using Godot;

namespace GodotSharp.Plugin;

public enum LogLevel { Info, Success, Warning, Error, Relay, Tool }

public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Message);

/// <summary>
/// GodotSharp MCP — EditorPlugin entry point.
/// </summary>
[Tool]
public partial class McpPlugin : EditorPlugin
{
    private PipeListener? _pipeListener;
    private Process?      _relayProcess;
    private McpPanel?     _panel;

    internal const string RelayExeResPath = "res://addons/godotsharp_mcp/bin/GodotSharp.Relay.exe";

    /// <summary>Returns the res:// path for the relay binary appropriate to the current OS and CPU architecture.</summary>
    internal static string RelayBinaryResPath()
    {
        const string Base = "res://addons/godotsharp_mcp/bin/GodotSharp.Relay";
        if (OperatingSystem.IsWindows()) return Base + ".exe";
        if (OperatingSystem.IsLinux())   return Base + ".linux";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Base + ".osx-arm64"
                : Base + ".osx-x64";
        throw new PlatformNotSupportedException("GodotSharp MCP: unsupported operating system.");
    }

    public event Action<LogEntry>? OnLog;

    // ------------------------------------------------------------------
    // Logging
    // ------------------------------------------------------------------

    /// <summary>Call from the main thread.</summary>
    internal void Log(string message, LogLevel level = LogLevel.Info)
    {
        GD.Print($"[MCP Plugin] {message}");
        OnLog?.Invoke(new LogEntry(DateTime.Now, level, message));
    }

    /// <summary>Call from any thread — defers the UI event to the main thread.</summary>
    internal void LogDeferred(string message, LogLevel level = LogLevel.Info)
    {
        GD.Print($"[MCP Plugin] {message}");
        var entry = new LogEntry(DateTime.Now, level, message);
        Callable.From(() => OnLog?.Invoke(entry)).CallDeferred();
    }

    // ------------------------------------------------------------------

    public override void _EnterTree()
    {
        Log("Entering tree.");

        _panel = new McpPanel(this);
        AddControlToBottomPanel(_panel, "GodotSharp MCP");

        _pipeListener = new PipeListener(this);
        _pipeListener.Start();

        // After an assembly hot-reload the relay process survives; re-attach to it so
        // IsRelayRunning stays accurate and the Exited event still fires.
        var surviving = Process.GetProcessesByName("GodotSharp.Relay")
                               .FirstOrDefault(p => !p.HasExited);
        if (surviving is not null)
        {
            _relayProcess = surviving;
            _relayProcess.EnableRaisingEvents = true;
            _relayProcess.Exited += (_, _) =>
            {
                LogDeferred("Relay process exited.", LogLevel.Warning);
                Callable.From(() => _panel?.Refresh()).CallDeferred();
            };
            Log($"Reattached to existing relay (PID {_relayProcess.Id}).", LogLevel.Success);
        }
    }

    public override void _ExitTree()
    {
        Log("Exiting tree.");

        _pipeListener?.Stop();
        _pipeListener = null;

        // Do NOT kill the relay here. The relay must survive Godot assembly hot-reloads
        // so active AI client sessions (stdio/SSE) are not broken.
        //
        // DO dispose the Process handle — this severs the ErrorDataReceived and Exited
        // event subscriptions without terminating the process. Those delegates are compiled
        // from this assembly; leaving them attached prevents .NET from garbage-collecting
        // the old AssemblyLoadContext, which is what triggers "Failed to unload assemblies".
        if (_relayProcess is not null)
        {
            try { _relayProcess.CancelErrorRead(); } catch { }
            _relayProcess.Dispose();
            _relayProcess = null;
        }

        if (_panel is not null)
        {
            RemoveControlFromBottomPanel(_panel);
            _panel.QueueFree();
            _panel = null;
        }
    }

    // ------------------------------------------------------------------

    internal void LaunchRelay()
    {
        // Kill any stale relay processes from a previous session (e.g. editor crash or hot-reload).
        foreach (var stale in Process.GetProcessesByName("GodotSharp.Relay"))
        {
            try
            {
                stale.Kill();
                stale.WaitForExit(2000);
                Log($"Killed stale relay process (PID {stale.Id}).", LogLevel.Warning);
            }
            catch { /* already gone */ }
            finally { stale.Dispose(); }
        }

        var exePath = ProjectSettings.GlobalizePath(RelayBinaryResPath());

        if (!File.Exists(exePath))
        {
            Log($"Relay binary not found at: {exePath}", LogLevel.Error);
            return;
        }

        // On Linux and macOS the file may lack the execute bit after being installed
        // from a zip or via git checkout. Ensure it is executable before launching.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                using var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod", Arguments = $"+x \"{exePath}\"",
                    UseShellExecute = false, CreateNoWindow = true
                });
                chmod?.WaitForExit(3000);
            }
            catch { /* chmod not available on this system — try launching anyway */ }
        }

        try
        {
            _relayProcess = Process.Start(new ProcessStartInfo
            {
                FileName               = exePath,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardError  = true,
                RedirectStandardOutput = false
            });

            if (_relayProcess is not null)
            {
                _relayProcess.EnableRaisingEvents = true;
                _relayProcess.Exited += (_, _) =>
                {
                    LogDeferred("Relay process exited.", LogLevel.Warning);
                    Callable.From(() => _panel?.Refresh()).CallDeferred();
                };
                _relayProcess.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                        LogDeferred(e.Data, LogLevel.Relay);
                };
                _relayProcess.BeginErrorReadLine();
                Log($"Relay launched (PID {_relayProcess.Id}).", LogLevel.Success);
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to launch relay: {ex.Message}", LogLevel.Error);
        }
    }

    internal void StopRelay()
    {
        if (_relayProcess is null) return;
        try { if (!_relayProcess.HasExited) _relayProcess.Kill(); }
        catch { /* already dead */ }
        finally
        {
            _relayProcess.Dispose();
            _relayProcess = null;
            Log("Relay stopped.", LogLevel.Warning);
        }
    }

    internal bool IsRelayRunning =>
        _relayProcess is { HasExited: false };
}

