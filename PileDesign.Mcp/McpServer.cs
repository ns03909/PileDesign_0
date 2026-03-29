using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PileDesign.Mcp;

/// <summary>
/// MCP (Model Context Protocol) サーバー。
/// JSON-RPC 2.0 over stdin/stdout で通信する。
/// </summary>
public sealed class McpServer
{
    private readonly PileDesignTools _tools = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task RunAsync()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        // stderr にログ出力（stdout は MCP プロトコル専用）
        Log("PileDesign MCP Server started");

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line);
                if (request == null) continue;

                var response = HandleRequest(request);
                if (response != null)
                {
                    var json = response.ToJsonString(_jsonOptions);
                    Console.WriteLine(json);
                    Console.Out.Flush();
                }
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
        }

        Log("PileDesign MCP Server stopped");
        await Task.CompletedTask; // async シグネチャ維持
    }

    private JsonNode? HandleRequest(JsonNode request)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"];

        // Notification (no id) — no response needed
        if (id == null && method == "notifications/initialized")
            return null;

        if (method == null)
            return MakeError(id, -32600, "Invalid Request");

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => HandleToolsCall(id, request["params"]),
            "ping" => MakeResult(id, new JsonObject()),
            _ => MakeError(id, -32601, $"Method not found: {method}")
        };
    }

    private JsonNode HandleInitialize(JsonNode? id)
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
                ["name"] = "piledesign",
                ["version"] = "1.0.0"
            }
        };
        return MakeResult(id, result);
    }

    private JsonNode HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray();

        tools.Add(MakeToolDef("load_model",
            "JSONモデルファイルを読み込む（GUIアプリで保存した.jsonファイル）",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "JSONファイルのパス" }
                },
                ["required"] = new JsonArray { "path" }
            }));

        tools.Add(MakeToolDef("run_analysis",
            "読み込み済みモデルで水平解析を実行し、結果を返す",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["convergence_tolerance"] = new JsonObject { ["type"] = "number", ["description"] = "収束判定値（既定: 0.001）" },
                    ["max_iterations"] = new JsonObject { ["type"] = "integer", ["description"] = "最大反復回数（既定: 50）" },
                    ["relaxation_factor"] = new JsonObject { ["type"] = "number", ["description"] = "緩和係数（既定: 1.0）" }
                }
            }));

        tools.Add(MakeToolDef("get_model_info",
            "読み込み済みモデルの概要情報を返す（杭本数、地盤層数、荷重ケース数等）",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }));

        tools.Add(MakeToolDef("list_piles",
            "杭配置の一覧を返す（No, X, Y, Z, 杭体番号, 地盤番号）",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }));

        tools.Add(MakeToolDef("list_load_cases",
            "荷重ケースの一覧を返す",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }));

        tools.Add(MakeToolDef("set_pile_property",
            "指定した杭のプロパティを変更する",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["pile_no"] = new JsonObject { ["type"] = "integer", ["description"] = "杭番号" },
                    ["x"] = new JsonObject { ["type"] = "number", ["description"] = "X座標 (m)" },
                    ["y"] = new JsonObject { ["type"] = "number", ["description"] = "Y座標 (m)" },
                    ["z"] = new JsonObject { ["type"] = "number", ["description"] = "杭頭Z座標 (m)" },
                    ["axial_force_vl0"] = new JsonObject { ["type"] = "number", ["description"] = "軸力VL0 (kN)" }
                },
                ["required"] = new JsonArray { "pile_no" }
            }));

        tools.Add(MakeToolDef("save_model",
            "現在のモデルをJSONファイルに保存する",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "保存先ファイルパス" }
                },
                ["required"] = new JsonArray { "path" }
            }));

        var result = new JsonObject
        {
            ["tools"] = tools
        };
        return MakeResult(id, result);
    }

    private JsonNode HandleToolsCall(JsonNode? id, JsonNode? @params)
    {
        var toolName = @params?["name"]?.GetValue<string>();
        var arguments = @params?["arguments"];

        if (toolName == null)
            return MakeError(id, -32602, "Missing tool name");

        try
        {
            string resultText = toolName switch
            {
                "load_model" => _tools.LoadModel(arguments?["path"]?.GetValue<string>()),
                "run_analysis" => _tools.RunAnalysis(
                    arguments?["convergence_tolerance"]?.GetValue<double?>(),
                    arguments?["max_iterations"]?.GetValue<int?>(),
                    arguments?["relaxation_factor"]?.GetValue<double?>()),
                "get_model_info" => _tools.GetModelInfo(),
                "list_piles" => _tools.ListPiles(),
                "list_load_cases" => _tools.ListLoadCases(),
                "set_pile_property" => _tools.SetPileProperty(arguments),
                "save_model" => _tools.SaveModel(arguments?["path"]?.GetValue<string>()),
                _ => throw new InvalidOperationException($"Unknown tool: {toolName}")
            };

            var content = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = resultText
                }
            };
            return MakeResult(id, new JsonObject { ["content"] = content });
        }
        catch (Exception ex)
        {
            var content = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"エラー: {ex.Message}"
                }
            };
            return MakeResult(id, new JsonObject { ["content"] = content, ["isError"] = true });
        }
    }

    // ─── ヘルパー ───

    private static JsonObject MakeToolDef(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonObject MakeResult(JsonNode? id, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        };
    }

    private static JsonObject MakeError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static void Log(string message)
    {
        Console.Error.WriteLine($"[PileDesign MCP] {message}");
    }
}
