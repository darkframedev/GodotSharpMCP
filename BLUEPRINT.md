# GodotSharp MCP: Native .NET Architecture Blueprint

**Project:** GodotSharp MCP (Model Context Protocol)  
**Target Engine:** Godot 4.4+ (C# / .NET 10)  
**Primary Objective:** Eliminate external runtimes (Node.js) by bridging AI clients (VS Code, OpenCode.ai) directly to the Godot Editor using a zero-port, pure C# IPC architecture.

---

## 1. System Architecture: The "Zero-Node" Approach

The system is strictly divided into two highly cohesive components. This isolates the blocking nature of MCP's JSON-RPC parsing from Godot's frame execution, preventing engine stutter while maintaining strict thread-safety boundaries.

```text
[ AI Client: OpenCode.ai / VS Code / Copilot ] 
                     |
                     | (stdio or HTTP/SSE)
                     v
+-------------------------------------------------+
|  GodotSharp.MCP.exe (Standalone Relay)          |
|  - MCP Protocol Implementation (JSON-RPC 2.0)   |
|  - Tool Definitions Schema (The 163 List)       |
|  - Named Pipe Server (System.IO.Pipes)          |
+-------------------------------------------------+
                     |
                     | (Named Pipes: \\.\pipe\GodotSharpMCP_Deadletters)
                     v
+-------------------------------------------------+
|  Godot 4 Editor Process                         |
|  - GodotSharp MCP Plugin (C#)                   |
|      * Background Task: Named Pipe Client       |
|      * Main Thread: CallDeferred Dispatcher     |
|      * EditorUndoRedoManager Wrapper            |
+-------------------------------------------------+
```

---

## 2. Component Breakdown & Critical Constraints

### A. The Relay (`GodotSharp.MCP.exe`)
A standard .NET 10 Console Application published as a lightweight, single-file executable.

* **Transport Layer (External):** Listens for MCP requests via `stdio` (for CLI tools) or a lightweight HTTP/SSE server (for remote/IDE connections).
* **Transport Layer (Internal):** Uses `NamedPipeServerStream` to communicate with the Godot Editor. **Constraint:** Do not use TCP, UDP, or local loopback ports for the engine IPC. This eliminates port conflicts and firewall prompts.
* **Responsibilities:**
  * Parse incoming JSON-RPC.
  * Hold the schema definitions for all Tool Categories so the AI knows what is available.
  * Forward validated execution requests down the Named Pipe.
  * Wait for the serialized response from Godot and route it back to the AI.

### B. The Engine Plugin (`addons/godotsharp_mcp`)
A pure native C# `EditorPlugin` running inside Godot.

* **Lifecycle:** In `_EnterTree()`, the plugin adds a toolbar button to launch `GodotSharp.MCP.exe` via `System.Diagnostics.Process.Start()`, making it a 1-click startup.
* **The Listener:** Spawns a background `Task` using `NamedPipeClientStream` to continuously read incoming commands from the Relay.
* **CRITICAL THREADING CONSTRAINT:** The background listener task **must never** interact with `EditorInterface` or the `SceneTree`. All engine mutations and state queries must be marshaled to the main thread using:
  ```csharp
  Callable.From(() => { 
      // Safe SceneTree/Editor logic here
  }).CallDeferred();
  ```
* **Mutation Safety:** Any command that modifies the project (adding nodes, altering properties, writing files) must be wrapped in Godot's `EditorUndoRedoManager` to ensure full `Ctrl+Z` support.

---

## 3. Implementation Roadmap

### Phase 1: Core IPC & "Lite Mode"
Focus entirely on establishing the pipe and basic scene inspection.
1. **Transport:** Build the `stdio` listener in the Relay and the Named Pipe bridge.
2. **Project (File Info):** Standard `System.IO` wrappers to read the active project directory.
3. **Scene (Tree Inspection):** Wrap `EditorInterface.GetEditedSceneRoot()`. Recursively serialize the node tree into a clean JSON string for the AI.
4. **Node (Mutation):** Implement `AddChild`, `QueueFree`, and `SetProperty`, strictly wrapped in `CallDeferred` and `UndoRedo`.

### Phase 2: Editor Integration
1. **Script Management:** Tools to attach `.cs` scripts to specific NodePaths.
2. **Editor Logging:** Hook into `Godot.EditorLog` so execution errors are streamed back down the pipe to the AI.
3. **Resource Parsing:** Safe read/write access to `.tres` files.

### Phase 3: Runtime Telemetry (Advanced)
1. **Live Inspection:** Query the `SceneTree` while the game is actively playing.
2. **Input Simulation:** Generate synthetic `InputEvent` objects to test character controllers.

---

## 4. Development & Symlink Workflow

To prevent coupling the MCP tools to your game's proprietary namespaces, develop the plugin in a clean, standalone repository and symlink it into the active project.

1. **Initialize Repo:**
   `C:\Projects\godotsharp_mcp`
   * `/src/GodotSharp.Relay/` (Console App)
   * `/src/GodotSharp.Plugin/` (Godot Addon)

2. **Symlink to Active Project:**
   Open an Administrator PowerShell and run:
   ```powershell
   mklink /J "C:\Projects\deadletters\deadletters\addons\godotsharp_mcp" "C:\Projects\godotsharp_mcp\src\GodotSharp.Plugin\addons\godotsharp_mcp"
   ```

3. **Build Pipeline:**
   Create a build script that compiles the Relay `.exe`, copies it into the Plugin's `bin/` directory, and triggers Godot to hot-reload the C# assemblies. This guarantees the executable and the plugin stay perfectly synchronized.

---

## 5. Current Tool Inventory (20 tools, Phase 1 + Phase 2)

### Scene / Project
| Tool | Description |
|---|---|
| `godot_get_project_info` | Project name, directory, engine version |
| `godot_list_scenes` | All `.tscn` files in the project |
| `godot_open_scene` | Open a scene by `res://` path |
| `godot_save_scene` | Save the currently open scene |
| `godot_get_scene_tree` | Full node hierarchy as JSON; optional property dump |

### Node
| Tool | Description |
|---|---|
| `godot_add_node` | Create and attach a node to a parent |
| `godot_remove_node` | Remove a node and its children |
| `godot_set_node_property` | Set an Inspector-visible property |
| `godot_list_signals` | All signals on a node with argument signatures |
| `godot_connect_signal` | Wire a signal from source node to target method |
| `godot_add_node_to_group` | Add node to a named group |
| `godot_remove_node_from_group` | Remove node from a named group |

### Script
| Tool | Description |
|---|---|
| `godot_get_script` | Read script source by path or from a node |
| `godot_set_script` | Write script source to disk; triggers filesystem rescan |
| `godot_attach_script` | Wire an existing script file to a scene node |

### Resource
| Tool | Description |
|---|---|
| `godot_list_resources` | All files in project, filterable by extension |
| `godot_get_resource_property` | Read a property from a `.tres` / `.res` file |
| `godot_set_resource_property` | Write a property to a `.tres` / `.res` file, save to disk |

### Editor
| Tool | Description |
|---|---|
| `godot_get_editor_log` | Tail of `godot.log` from disk |
| `godot_list_input_actions` | Full Input Map with bound events |

---

## 6. Future Tool Backlog

Organized by domain and implementation complexity. All tools follow the `godot_{verb}_{noun}` convention.

---

### 6A. Asset Import

Godot stores per-asset import settings in sidecar `.import` files next to each asset.  
Key API: `EditorInterface.GetResourceFilesystem()`, `EditorFileSystem.Reimport(paths[])`.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_get_import_settings` | Read `.import` sidecar file (INI format via `ConfigFile`) | Returns flags like `compress/mode`, `texture/hdr`, etc. |
| `godot_set_import_settings` | Write `.import` sidecar + `EditorFileSystem.Reimport()` | Lets the AI tweak texture compression, audio loops, mesh LODs without opening the Import dock |
| `godot_reimport_asset` | `EditorFileSystem.Reimport(new[]{ path })` | Force a single asset to re-run its importer; useful after `godot_set_import_settings` |
| `godot_reimport_all` | `EditorFileSystem.ScanSources()` + `Reimport()` | Nuke and re-import everything; heavy but occasionally necessary |

---

### 6B. Resource Creation & .tres Management

Currently `godot_set_resource_property` can mutate an existing `.tres`. These tools round out the full resource lifecycle.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_create_resource` | `ClassDB.Instantiate(type)` cast to `Resource` + `ResourceSaver.Save()` | Creates a blank `.tres` of any Resource subclass (e.g. `StandardMaterial3D`, `AudioStreamOggVorbis`, `Curve`) |
| `godot_list_resource_properties` | `resource.GetPropertyList()` filtered by `PropertyUsageFlags.Editor` | Lets the AI discover what properties exist on a resource before calling get/set |
| `godot_duplicate_resource` | `ResourceLoader.Load()` + `resource.Duplicate()` + `ResourceSaver.Save()` | Copy a `.tres` to a new path; useful for material variants |
| `godot_convert_resource` | Load + cast + `ResourceSaver.Save(resource, newPath, SaverFlags.ChangePath)` | Re-save a resource in a different format (e.g. `.res` binary vs `.tres` text) |

---

### 6C. Packed Scenes (Prefabs)

Godot's equivalent of prefabs are `.tscn` files loaded as `PackedScene` resources.  
Key API: `PackedScene.Instantiate()`, `PackedScene.Pack(node)`.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_instantiate_scene` | `ResourceLoader.Load<PackedScene>(path).Instantiate()` then `parent.AddChild()` | Adds an instance of a packed scene into the currently open scene as a child of a given node; returns the instance's NodePath |
| `godot_pack_node_as_scene` | `new PackedScene().Pack(node)` + `ResourceSaver.Save()` | Extracts a subtree from the current scene and saves it as a reusable `.tscn` prefab |
| `godot_get_scene_inherited_info` | Read `SceneFilePath` and `GetInheritanceChain()` | Reports whether a scene inherits from another; important for multi-level scene hierarchies |

---

### 6D. Project Settings & Autoloads

The `project.godot` file controls global configuration. The AI needs read/write access to configure physics layers, window size, rendering settings, and autoload singletons.  
Key API: `ProjectSettings.GetSetting()`, `ProjectSettings.SetSetting()`, `ProjectSettings.Save()`.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_get_project_setting` | `ProjectSettings.GetSetting(key)` | Read any `project.godot` key, e.g. `"physics/2d/default_gravity"`, `"display/window/size/viewport_width"` |
| `godot_set_project_setting` | `ProjectSettings.SetSetting(key, value)` + `ProjectSettings.Save()` | Write a project setting; dangerous if misused but essential for AI-driven project configuration |
| `godot_list_autoloads` | Read `ProjectSettings` keys under `"autoload/*"` | Returns all singleton names and their script paths |
| `godot_add_autoload` | `ProjectSettings.SetSetting("autoload/Name", "res://path.cs")` + `Save()` | Register a new global singleton; equivalent to Project > Project Settings > Autoload |
| `godot_remove_autoload` | `ProjectSettings.Clear("autoload/Name")` + `Save()` | Deregister a singleton |
| `godot_list_physics_layers` | Read `"layer_names/2d_physics/layer_*"` from ProjectSettings | Returns the human-readable names for collision layers; the AI needs these to write correct collision masks in scripts |

---

### 6E. Node Utilities

Quality-of-life operations on scene nodes that come up constantly in editor workflows.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_duplicate_node` | `node.Duplicate()` + parent `AddChild()` | Clones a node subtree in-place; wrap in UndoRedo |
| `godot_reparent_node` | `node.Reparent(newParent)` | Move a node to a new parent without losing its children; wrap in UndoRedo |
| `godot_get_node_groups` | `node.GetGroups()` | List all groups a node currently belongs to |
| `godot_rename_node` | `node.Name = newName` | Rename a node; separate from set_property since Name has special editor handling |
| `godot_move_node` | Adjust `node.GetIndex()` via `parent.MoveChild(node, idx)` | Reorder siblings in the scene tree; affects draw order for 2D and processing order |
| `godot_get_node_property` | `node.Get(property)` | Read a single property; counterpart to the existing set tool. Currently requires `include_properties=true` on the full tree — a dedicated getter is faster for targeted reads |

---

### 6F. File & Directory Management

Low-level project filesystem operations. These operate on the raw `res://` directory, not Godot resources — use `System.IO` directly.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_create_directory` | `System.IO.Directory.CreateDirectory()` | Create a folder under `res://`; also call `EditorFileSystem.Scan()` after |
| `godot_delete_file` | `System.IO.File.Delete()` + `EditorFileSystem.Scan()` | Delete any project file; **dangerous** — require explicit confirmation parameter |
| `godot_move_file` | `System.IO.File.Move()` + `EditorFileSystem.Scan()` | Rename or move a file within the project; does not update scene references (that requires a separate re-path step) |
| `godot_list_scripts` | `Directory.GetFiles("*.cs", AllDirectories)` | List all C# source files; complements `godot_list_resources` for code navigation |

---

### 6G. Animation

Animation in Godot is driven by `AnimationPlayer` nodes holding `Animation` resources.  
Key API: `AnimationPlayer`, `Animation`, `AnimationLibrary`.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_list_animations` | `AnimationPlayer.GetAnimationList()` | Returns all animation names in an `AnimationPlayer` at a given NodePath |
| `godot_get_animation_length` | `AnimationPlayer.GetAnimation(name).Length` | Read the duration of a specific animation clip |
| `godot_create_animation` | `new Animation()` + `AnimationPlayer.GetAnimationLibrary("").AddAnimation()` | Create a blank animation clip and register it to a player |
| `godot_add_animation_track` | `animation.AddTrack(Animation.TrackType.Value)` + `animation.TrackSetPath()` | Add a property track targeting a NodePath/property; prerequisite to adding keyframes |
| `godot_add_keyframe` | `animation.TrackInsertKey(trackIdx, time, value)` | Insert a keyframe at a specific time with a specific value; this is the unit of animation authoring |

---

### 6H. Editor State & UI

Tools that let the AI observe and control the editor UI itself — selection, camera, docks.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_get_selected_nodes` | `EditorInterface.GetSelection().GetSelectedNodes()` | Returns the NodePaths of whatever the developer currently has selected in the editor; useful as implicit context |
| `godot_select_node` | `EditorInterface.GetSelection().AddNode(node)` | Programmatically select a node, causing the Inspector to focus on it |
| `godot_get_editor_camera` | `EditorInterface.GetEditedSceneRoot()` → find `EditorCamera` | Read the editor viewport camera's transform; useful for placing nodes at the current view position |
| `godot_focus_node` | `EditorInterface.GetSelection()` + `EditorPlugin.MakeBottomPanelItemVisible()` | Frame a node in the editor viewport (the "F" key equivalent) |

---

### 6I. Runtime Telemetry (Phase 3)

These tools only work while the game is actively running in the editor. They require detecting play-mode via `EditorInterface.IsPlayingScene()`.

| Tool | Godot API | Notes |
|---|---|---|
| `godot_run_scene` | `EditorInterface.PlayMainScene()` or `PlayCurrentScene()` | Launch the game from the editor |
| `godot_stop_scene` | `EditorInterface.StopPlayingScene()` | Stop a running game |
| `godot_get_runtime_node_property` | `SceneTree.Root.GetNode(path).Get(property)` | Read a live node property while the game runs; enables the AI to observe game state |
| `godot_set_runtime_node_property` | `SceneTree.Root.GetNode(path).Set(property, value)` | Mutate live state; useful for live-tweaking physics values, speeds, positions |
| `godot_send_input_action` | `Input.ActionPress(action)` + `Input.ActionRelease(action)` | Synthesize an input action; lets the AI test character controllers and input-driven logic |
| `godot_get_runtime_scene_tree` | Walk `SceneTree.Root` while playing | Same as `godot_get_scene_tree` but operates on the live running tree, not the editor scene |

---

### 6J. Build & Export

| Tool | Godot API | Notes |
|---|---|---|
| `godot_list_export_presets` | Read `export_presets.cfg` via `ConfigFile` | Returns configured export preset names (Windows Desktop, Android, Web, etc.) |
| `godot_export_project` | `EditorInterface` doesn't expose export directly; use `EditorExportPlugin` or shell out to `godot --export` | Trigger a build for a named preset; returns build output path and any errors |

---

### Implementation Priority Order

When picking up the next phase of work, suggested order based on day-to-day development impact:

1. `godot_get_node_property` — missing read counterpart to the existing set tool; low effort
2. `godot_create_resource` — completes the resource lifecycle already started
3. `godot_list_resource_properties` — makes resource tools self-documenting for the AI
4. `godot_get_project_setting` / `godot_set_project_setting` — physics layers, window config, gravity
5. `godot_list_autoloads` / `godot_add_autoload` / `godot_remove_autoload` — global singletons are core Godot architecture
6. `godot_duplicate_node` / `godot_reparent_node` — bread-and-butter scene editing operations
7. `godot_instantiate_scene` — prefab instantiation unlocks scene composition workflows
8. `godot_get_import_settings` / `godot_set_import_settings` / `godot_reimport_asset` — texture/audio tuning
9. Animation tools — high complexity, defer until core workflows are solid
10. Runtime telemetry — Phase 3, significant architecture work