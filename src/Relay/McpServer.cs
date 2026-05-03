using System.Text.Json.Nodes;
using GodotSharp.Relay.Protocol;
using GodotSharp.Relay.Tools;

namespace GodotSharp.Relay;

/// <summary>
/// Implements MCP (Model Context Protocol) over stdio using JSON-RPC 2.0.
/// Handles initialize, tools/list, and tools/call.
/// Tool calls are forwarded to the Godot editor plugin via the PipeServer.
/// </summary>
public sealed class McpServer
{
    private readonly PipeServer _pipe;
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;

    public McpServer(PipeServer pipe, TextReader? stdin = null, TextWriter? stdout = null)
    {
        _pipe = pipe;
        _stdin = stdin ?? Console.In;
        _stdout = stdout ?? Console.Out;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.Error.WriteLine("[Relay] MCP server ready. Listening on stdio.");

        while (!ct.IsCancellationRequested)
        {
            var line = await _stdin.ReadLineAsync(ct);
            if (line is null) break; // stdin closed — AI client disconnected

            if (string.IsNullOrWhiteSpace(line)) continue;

            var request = JsonRpcRequest.Parse(line);
            if (request is null)
            {
                await WriteResponseAsync(JsonRpcResponse.Failure(null, -32700, "Parse error"), ct);
                continue;
            }

            var response = await HandleRequestAsync(request, ct);
            await WriteResponseAsync(response, ct);
        }
    }

    // -----------------------------------------------------------------

    private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest req, CancellationToken ct)
    {
        try
        {
            return req.Method switch
            {
                "initialize"   => HandleInitialize(req),
                "tools/list"   => HandleToolsList(req),
                "tools/call"   => await HandleToolsCallAsync(req, ct),
                _              => JsonRpcResponse.Failure(req.Id, -32601, $"Method not found: {req.Method}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Relay] Unhandled error: {ex.Message}");
            return JsonRpcResponse.Failure(req.Id, -32603, "Internal error");
        }
    }

    private static JsonRpcResponse HandleInitialize(JsonRpcRequest req)
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "GodotSharpMCP",
                ["version"] = "1.0.0"
            }
        };
        return JsonRpcResponse.Success(req.Id, result);
    }

    private static JsonRpcResponse HandleToolsList(JsonRpcRequest req)
    {
        var result = new JsonObject { ["tools"] = ToolRegistry.GetToolList() };
        return JsonRpcResponse.Success(req.Id, result);
    }

    private async Task<JsonRpcResponse> HandleToolsCallAsync(JsonRpcRequest req, CancellationToken ct)
    {
        var toolName = req.Params?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(toolName))
            return JsonRpcResponse.Failure(req.Id, -32602, "Invalid params: missing tool name");

        if (!_pipe.IsConnected)
            return JsonRpcResponse.Failure(req.Id, -32603,
                "Godot editor not connected. Launch the MCP plugin from the Godot toolbar.");

        var command = new PipeCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Tool = toolName,
            Arguments = req.Params?["arguments"]?.DeepClone()
        };

        var pipeResponse = await _pipe.SendCommandAsync(command, ct);
        if (pipeResponse is null)
            return JsonRpcResponse.Failure(req.Id, -32603, "No response from Godot editor (pipe error).");

        // Wrap in MCP content envelope
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = pipeResponse.Text
            }
        };

        var result = new JsonObject
        {
            ["content"] = content,
            ["isError"] = pipeResponse.IsError
        };

        return JsonRpcResponse.Success(req.Id, result);
    }

    private async Task WriteResponseAsync(JsonRpcResponse response, CancellationToken ct)
    {
        var json = response.Serialize();
        await _stdout.WriteLineAsync(json.AsMemory(), ct);
        // stdout must be flushed — Console.Out auto-flushes on writeline but explicit is safer.
        if (_stdout is StreamWriter sw) await sw.FlushAsync(ct);
    }
}
