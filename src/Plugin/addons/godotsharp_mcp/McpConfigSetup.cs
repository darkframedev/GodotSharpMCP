using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace GodotSharp.Plugin;

/// <summary>
/// Discovers and writes AI client MCP config entries for the GodotSharp relay.
/// Supports OpenCode (project-local), VS Code (.vscode/mcp.json), and GitHub Copilot CLI (~/.copilot/mcp-config.json).
/// </summary>
internal static class McpConfigSetup
{
    private const string EntryKey = "godot";

    public enum ConfigKind     { OpenCodeLocal, VsCode, CopilotCli }
    public enum ConfigStatus   { Registered, NotRegistered }

    public record ConfigEntry(string Label, ConfigKind Kind, string Path, ConfigStatus Status);

    // ------------------------------------------------------------------
    // Discovery — returns one entry per detected client
    // ------------------------------------------------------------------

    public static List<(string Label, ConfigKind Kind, ConfigStatus Status)> GetConfigStatus()
        => Discover().Select(e => (e.Label, e.Kind, e.Status)).ToList();

    public static void Apply(ConfigKind kind, string relayExe)
    {
        var entry = Discover().FirstOrDefault(e => e.Kind == kind);
        if (entry is null) return;

        try
        {
            switch (kind)
            {
                case ConfigKind.OpenCodeLocal: WriteOpenCode(entry.Path, relayExe);   break;
                case ConfigKind.VsCode:        WriteVsCode(entry.Path, relayExe);     break;
                case ConfigKind.CopilotCli:    WriteCopilotCli(entry.Path, relayExe); break;
            }
            GD.Print($"[MCP Plugin] Registered relay in {entry.Label}: {entry.Path}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MCP Plugin] Failed to update {entry.Label} config: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------

    private static List<ConfigEntry> Discover()
    {
        var results = new List<ConfigEntry>();

        // OpenCode — project-local opencode.json (travels with the project)
        var projectDir = ProjectSettings.GlobalizePath("res://");
        if (!string.IsNullOrEmpty(projectDir))
        {
            var localPath = Path.Combine(projectDir, "opencode.json");
            var status = AlreadyRegistered_OpenCode(localPath) ? ConfigStatus.Registered : ConfigStatus.NotRegistered;
            results.Add(new ConfigEntry("OpenCode (project)", ConfigKind.OpenCodeLocal, localPath, status));
        }

        // VS Code — project-local .vscode/mcp.json
        var vsPath = VsCodeMcpPath();
        if (vsPath is not null)
        {
            var status = File.Exists(vsPath) && AlreadyRegistered_VsCode(vsPath)
                ? ConfigStatus.Registered
                : ConfigStatus.NotRegistered;
            results.Add(new ConfigEntry("VS Code (.vscode/mcp.json)", ConfigKind.VsCode, vsPath, status));
        }

        // GitHub Copilot CLI — global ~/.copilot/mcp-config.json
        // (Copilot CLI has no project-local equivalent; COPILOT_HOME overrides the directory.)
        var copilotPath = CopilotCliMcpPath();
        if (copilotPath is not null)
        {
            var status = File.Exists(copilotPath) && AlreadyRegistered_CopilotCli(copilotPath)
                ? ConfigStatus.Registered
                : ConfigStatus.NotRegistered;
            results.Add(new ConfigEntry("GitHub Copilot CLI (~/.copilot/mcp-config.json)", ConfigKind.CopilotCli, copilotPath, status));
        }

        return results;
    }

    private static string? VsCodeMcpPath()
    {
        var projectDir = ProjectSettings.GlobalizePath("res://");
        return string.IsNullOrEmpty(projectDir)
            ? null
            : Path.Combine(projectDir, ".vscode", "mcp.json");
    }

    private static string? CopilotCliMcpPath()
    {
        // Respect COPILOT_HOME if set, otherwise fall back to ~/.copilot
        var home = System.Environment.GetEnvironmentVariable("COPILOT_HOME")
                   ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".copilot");
        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "mcp-config.json");
    }

    // ------------------------------------------------------------------
    // Registration checks
    // ------------------------------------------------------------------

    private static bool AlreadyRegistered_OpenCode(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path))?["mcp"]?[EntryKey] is not null; }
        catch { return false; }
    }

    private static bool AlreadyRegistered_VsCode(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path))?["servers"]?[EntryKey] is not null; }
        catch { return false; }
    }

    private static bool AlreadyRegistered_CopilotCli(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path))?["mcpServers"]?[EntryKey] is not null; }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    // Writers
    // ------------------------------------------------------------------

    private static void WriteOpenCode(string path, string relayExe)
    {
        var root = TryLoad(path);
        var mcp  = root["mcp"] as JsonObject ?? new JsonObject();
        root["mcp"] = mcp;

        mcp[EntryKey] = new JsonObject
        {
            ["type"]    = "local",
            ["command"] = new JsonArray { JsonValue.Create(relayExe) }
        };

        Save(path, root);
    }

    private static void WriteVsCode(string path, string relayExe)
    {
        var root    = TryLoad(path);
        var servers = root["servers"] as JsonObject ?? new JsonObject();
        root["servers"] = servers;

        servers[EntryKey] = new JsonObject
        {
            ["type"]    = "stdio",
            ["command"] = relayExe,
            ["args"]    = new JsonArray()
        };

        Save(path, root);
    }

    private static void WriteCopilotCli(string path, string relayExe)
    {
        var root    = TryLoad(path);
        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        root["mcpServers"] = servers;

        servers[EntryKey] = new JsonObject
        {
            ["type"]    = "stdio",
            ["command"] = relayExe,
            ["args"]    = new JsonArray()
        };

        Save(path, root);
    }

    private static JsonObject TryLoad(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    private static void Save(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
