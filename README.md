# GodotSharp MCP

**Bridge AI coding assistants directly into the Godot 4 editor.**

GodotSharp MCP is a Godot 4 editor plugin that exposes [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) tools to AI clients — letting assistants like GitHub Copilot, OpenCode, and VS Code Copilot read and manipulate your live Godot scene without leaving the editor.

```
AI Client (Copilot / OpenCode / VS Code)
        │  stdio / SSE
        ▼
GodotSharp.Relay.exe  ←── MCP JSON-RPC server (standalone process)
        │  Named Pipe
        ▼
Godot 4 Editor  ←── GodotSharp MCP plugin (C#)
```

No TCP ports. No Node.js. No external runtimes. Pure .NET.

> **GDScript projects are fully supported.** The plugin runs entirely inside the editor and uses Godot's language-agnostic editor API — your game scripts can be GDScript, C#, or a mix. The only requirement is that .NET is enabled in the project so the editor can load C# editor plugins (this adds a harmless `.csproj` if one doesn't already exist).

---

## Features

- **60+ MCP tools** across 10 domains: scene tree, nodes, scripts, resources, animations, project settings, editor UI, and more
- **Zero-port IPC** — relay communicates with Godot via named pipes, eliminating firewall prompts and port conflicts
- **Survives hot-reload** — relay stays alive across Godot assembly reloads; your AI session is never interrupted
- **Full undo/redo support** — all node mutations are wrapped in Godot's `EditorUndoRedoManager`
- **One-click AI client setup** — built-in "Add Config" panel writes the MCP config for OpenCode, VS Code, and GitHub Copilot CLI

---

## Requirements

| Requirement | Version |
|---|---|
| Godot Engine | 4.4 or later |
| .NET SDK | 8.0 or later |
| OS | Windows, Linux, or macOS |

---

## Installation

**Option A — GitHub Releases (recommended)**

1. Download the latest `godotsharp-mcp-vX.Y.Z.zip` from the [Releases page](../../releases)
2. Extract into your **project root** — `addons/godotsharp_mcp/` should appear
3. In Godot: **Project → Project Settings → Plugins** → **GodotSharp MCP** → **Enabled**
4. The **GodotSharp MCP** panel appears at the bottom of the editor

<details>
<summary>Option B — Godot Asset Library</summary>

1. In Godot: **AssetLib** tab → search **GodotSharp MCP** → Install
2. Enable the plugin as above

</details>

---

## First-Time Setup

**1 — Launch the Relay**

In the **GodotSharp MCP** panel, click **Launch Relay**. The status indicator turns green.

**2 — Configure Your AI Client**

Click **Add Config** next to your client:

| Client | Config written |
|---|---|
| **GitHub Copilot CLI** | `~/.copilot/mcp-config.json` |
| **OpenCode** | `<project>/opencode.json` |
| **VS Code** | `<project>/.vscode/mcp.json` |

**3 — Start Coding**

```
Use the get_scene_tree tool to show me the current scene.
```

---

## Tool Reference

Tools follow the `godot_{verb}_{noun}` naming convention.

<details>
<summary>Full tool list (60+ tools across 10 domains)</summary>

| Domain | Tools |
|---|---|
| **Scene** | `get_scene_tree`, `open_scene`, `save_scene`, `list_scenes`, `instantiate_scene`, `pack_node_as_scene` |
| **Node** | `add_node`, `remove_node`, `set_node_property`, `get_node_property`, `duplicate_node`, `reparent_node`, `rename_node`, `move_node` |
| **Script** | `get_script`, `set_script`, `attach_script` |
| **Resource** | `list_resources`, `get_resource_property`, `set_resource_property` |
| **Animation** | `list_animations`, `create_animation`, `add_animation_track`, `add_keyframe` |
| **Project Settings** | `get_project_setting`, `set_project_setting`, `list_autoloads`, `add_autoload`, `remove_autoload` |
| **Editor** | `get_editor_log`, `select_node`, `focus_node`, `get_selected_nodes` |
| **File System** | `list_scripts`, `create_directory`, `move_file` |
| **Import** | `get_import_settings`, `set_import_settings`, `reimport_asset` |
| **Runtime** | `run_scene`, `stop_scene`, `get_runtime_node_property` (play-mode only) |

</details>

<details>
<summary>Node path format</summary>

All `node_path` parameters accept the **scene-root-relative** path returned by `get_scene_tree`:

- Direct child: `"Player"`
- Nested: `"Player/Sprite2D"`
- Scene root itself: `"."`

Absolute paths like `/root/World/Player` are also accepted and normalised automatically.

</details>

---

## Developer / Contributor Setup

<details>
<summary>Local development setup</summary>

```powershell
# 1. Clone the repo
git clone https://github.com/darkframedev/GodotSharpMCP
cd GodotSharpMCP

# 2. Symlink the addon into your Godot project (run as Administrator on Windows)
New-Item -ItemType Junction `
  -Path "C:\Projects\mygame\addons\godotsharp_mcp" `
  -Target "$PWD\src\Plugin\addons\godotsharp_mcp"

# 3. Build the relay
.\build.ps1                   # all platforms
.\build.ps1 -Platform win-x64 # Windows only (fastest for local dev)

# 4. Open your game project in Godot — the plugin hot-reloads automatically
```

> Cross-compiling Linux/macOS binaries from Windows requires the .NET cross-compilation toolchain. The CI workflow handles this on GitHub Actions.

</details>

<details>
<summary>Architecture overview</summary>

```
GodotSharpMCP/
├── src/
│   ├── Relay/                  # Standalone .NET 8 console app
│   │   ├── Program.cs          # Entry point; starts MCP stdio server
│   │   ├── McpServer.cs        # JSON-RPC 2.0 dispatcher
│   │   ├── PipeServer.cs       # Named pipe server (IPC to Godot)
│   │   └── Tools/
│   │       └── ToolRegistry.cs # All 60+ MCP tool schemas
│   └── Plugin/
│       └── addons/godotsharp_mcp/
│           ├── plugin.cfg
│           ├── McpPlugin.cs            # EditorPlugin entry point
│           ├── PipeListener.cs         # Named pipe client (background task)
│           ├── CommandDispatcher.*.cs  # Tool implementations
│           ├── NodeSerializer.cs       # Scene tree → JSON
│           ├── McpConfigSetup.cs       # AI client config writers
│           ├── McpPanel.cs             # Editor bottom panel UI
│           └── bin/                    # Pre-built relay binaries (all platforms)
├── build.ps1
├── LICENSE
└── README.md
```

**Key constraint:** All Godot API calls must happen on the main thread. The pipe listener runs on a background thread and marshals commands via `Callable.From().CallDeferred()`.

</details>

<details>
<summary>Godot Asset Library submission settings</summary>

- **Repository subfolder:** `src/Plugin`
- **Installation folder:** `addons/godotsharp_mcp`
- **Category:** Tools
- **Tags:** `ai`, `mcp`, `copilot`, `editor`, `csharp`

</details>

---

## License

[MIT](LICENSE) — © 2025 darkframe.dev
