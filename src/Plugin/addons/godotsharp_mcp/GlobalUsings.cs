// Global using directives for the GodotSharp MCP plugin.
// These cover all namespaces used across the CommandDispatcher partials and helpers.
// Explicit globals are required because the Godot game project's .csproj may not
// have ImplicitUsings enabled, and the addon source is compiled in that context.
//
// NOTE: Godot.Collections and System.Collections.Generic are intentionally omitted
// to avoid ambiguous reference errors (e.g. Dictionary<,>) in the host project's scripts.
// Plugin code that needs those types uses fully-qualified names instead.

global using System;
global using System.Diagnostics;
global using System.IO.Pipes;
global using System.Linq;
global using System.Runtime.InteropServices;
global using System.Text.Json.Nodes;
global using System.Threading.Tasks;
global using Godot;
