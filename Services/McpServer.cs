using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotaComboBoard.Models;

namespace DotaComboBoard.Services;

public sealed class McpServer : IDisposable
{
    public const int Port = 37822;
    public const string Endpoint = "http://127.0.0.1:37822/mcp";
    public const string DisplayName = "Dota Combo Board";
    public const string TransportName = "Streamable HTTP";
    private const int MaxRequestBytes = 2 * 1024 * 1024;
    private const string ModernProtocolVersion = "2026-07-28";
    private const string DefaultLegacyProtocolVersion = "2025-11-25";
    private const string ServerName = "dota-combo-board";
    private static readonly HashSet<string> LegacyProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        DefaultLegacyProtocolVersion
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _boardPath;
    private readonly BrowserSyncServer _browserSyncServer;
    private readonly Func<string> _patchProvider;
    private readonly ComboJsonService _comboService = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private TcpListener? _listener;
    private bool _disposed;
    private int _requestCount;

    public McpServer(string boardPath, BrowserSyncServer browserSyncServer, Func<string> patchProvider)
    {
        _boardPath = boardPath;
        _browserSyncServer = browserSyncServer;
        _patchProvider = patchProvider;
    }

    public bool IsRunning { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public int RequestCount => _requestCount;
    public string LastToolName { get; private set; } = string.Empty;

    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            IsRunning = true;
            ErrorMessage = string.Empty;
            _ = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
        }
        catch (Exception exception)
        {
            IsRunning = false;
            ErrorMessage = exception.Message;
        }

        RaiseStatusChanged();
    }

    public string CreateClientConfig()
    {
        return JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                [ServerName] = new
                {
                    type = "http",
                    url = Endpoint
                }
            }
        }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
                RaiseStatusChanged();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var request = await ReadHttpRequestAsync(stream, cancellationToken);
                if (!IsAllowedOrigin(request.Headers.GetValueOrDefault("Origin")))
                {
                    await WriteJsonAsync(stream, 403, new { error = "Origin is not allowed." }, request.Origin, null, cancellationToken);
                    return;
                }

                if (request.Method == "OPTIONS")
                {
                    await WriteEmptyAsync(stream, 204, request.Origin, cancellationToken);
                    return;
                }

                if (request.Method != "POST" || !request.Path.TrimEnd('/').Equals("/mcp", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(stream, 404, new { error = "MCP endpoint not found." }, request.Origin, null, cancellationToken);
                    return;
                }

                using var document = JsonDocument.Parse(request.Body);
                var response = await ProcessMessageAsync(document.RootElement, request.ProtocolVersion, cancellationToken);
                if (response.IsNotification)
                {
                    await WriteEmptyAsync(stream, 202, request.Origin, cancellationToken);
                    return;
                }

                Interlocked.Increment(ref _requestCount);
                RaiseStatusChanged();
                await WriteJsonAsync(stream, response.StatusCode, response.Payload!, request.Origin, response.ProtocolVersion, cancellationToken);
            }
            catch (JsonException exception)
            {
                await TryWriteErrorAsync(client, 400, null, -32700, $"Parse error: {exception.Message}", cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                await TryWriteErrorAsync(client, 400, null, -32600, exception.Message, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                if (exception is not OperationCanceledException)
                {
                    ErrorMessage = exception.Message;
                    RaiseStatusChanged();
                }
            }
        }
    }

    private async Task<McpResponse> ProcessMessageAsync(
        JsonElement root,
        string headerProtocolVersion,
        CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("jsonrpc", out var jsonRpc)
            || jsonRpc.GetString() != "2.0"
            || !root.TryGetProperty("method", out var methodElement))
        {
            throw new InvalidDataException("Expected a JSON-RPC 2.0 request.");
        }

        var method = methodElement.GetString() ?? string.Empty;
        var hasId = root.TryGetProperty("id", out var idElement);
        var id = hasId ? idElement.Clone() : default(JsonElement?);
        var modernVersion = GetModernProtocolVersion(root);
        if (method == "server/discover" || modernVersion is not null || headerProtocolVersion == ModernProtocolVersion)
        {
            var requestedVersion = modernVersion ?? headerProtocolVersion;
            if (requestedVersion != ModernProtocolVersion || headerProtocolVersion != requestedVersion)
            {
                return Error(id, -32022, "Unsupported or mismatched MCP protocol version.", 400, new
                {
                    supported = new[] { ModernProtocolVersion },
                    requested = requestedVersion
                });
            }
        }

        if (!hasId)
        {
            return new McpResponse(202, null, true, modernVersion);
        }

        return method switch
        {
            "initialize" => Initialize(root, id),
            "server/discover" => Discover(id),
            "ping" => Success(id, new { }, headerProtocolVersion),
            "tools/list" => Success(id, CreateToolsList(modernVersion is not null), modernVersion ?? headerProtocolVersion),
            "tools/call" => await CallToolAsync(root, id, modernVersion ?? headerProtocolVersion, cancellationToken),
            _ => Error(id, -32601, $"Method not found: {method}", 404)
        };
    }

    private McpResponse Initialize(JsonElement root, JsonElement? id)
    {
        var requested = root.TryGetProperty("params", out var parameters)
                        && parameters.TryGetProperty("protocolVersion", out var versionElement)
            ? versionElement.GetString()
            : null;
        var negotiated = requested is not null && LegacyProtocolVersions.Contains(requested)
            ? requested
            : DefaultLegacyProtocolVersion;
        return Success(id, new
        {
            protocolVersion = negotiated,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = ServerInfo(),
            instructions = ServerInstructions()
        }, negotiated);
    }

    private McpResponse Discover(JsonElement? id)
    {
        return Success(id, new
        {
            resultType = "complete",
            supportedVersions = new[] { ModernProtocolVersion },
            capabilities = new { tools = new { } },
            instructions = ServerInstructions(),
            ttlMs = 60000,
            cacheScope = "private",
            _meta = ServerMeta()
        }, ModernProtocolVersion);
    }

    private object CreateToolsList(bool modern)
    {
        var tools = new object[]
        {
            Tool("get_app_context", "Read the current Dota Combo Board version, patch, counts, roles, and required combo format.", EmptySchema(), ReadOnlyAnnotations()),
            Tool("get_combo_generation_package", "Return the patch-aware generation prompt and exact Combo Pack JSON template expected by the app.", ObjectSchema(new
            {
                patch = new { type = "string", description = "Dota patch override. Defaults to the app's current patch." },
                count = new { type = "integer", minimum = 1, maximum = 100, @default = 15 },
                style = new { type = "string", description = "Optional draft style such as aggressive, teamfight, or objective." }
            }), ReadOnlyAnnotations()),
            Tool("list_combos", "Return current app combos as a formatVersion 1 Combo Pack.", ObjectSchema(new
            {
                rank = new { type = "string", @enum = new[] { "all", "archon", "legend" }, @default = "all" },
                limit = new { type = "integer", minimum = 1, maximum = 100, @default = 60 }
            }), ReadOnlyAnnotations()),
            Tool("validate_combo_pack", "Validate Combo Pack JSON without changing app data.", ObjectSchema(new
            {
                comboPack = new { type = "object", description = "Combo Pack formatVersion 1 object." }
            }, new[] { "comboPack" }), ReadOnlyAnnotations()),
            Tool("import_combo_pack", "Append or replace main-board combos after validating the exact app format. A backup is created first.", ObjectSchema(new
            {
                comboPack = new { type = "object", description = "Combo Pack formatVersion 1 object." },
                mode = new { type = "string", @enum = new[] { "append", "replace" }, @default = "append" },
                confirm = new { type = "boolean", description = "Must be true to write data." }
            }, new[] { "comboPack", "confirm" }), new { readOnlyHint = false, destructiveHint = true, idempotentHint = false, openWorldHint = false }),
            Tool("search_public_web", "Ask the paired Chrome extension to search public Dota sources and return structured results.", ObjectSchema(new
            {
                query = new { type = "string" },
                sources = new { type = "array", items = new { type = "string", @enum = new[] { "valve", "d2pt", "dotabuff", "general" } }, @default = new[] { "valve", "d2pt", "dotabuff", "general" } },
                maxResults = new { type = "integer", minimum = 5, maximum = 50, @default = 20 }
            }, new[] { "query" }), new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = true })
        };

        return modern
            ? new { resultType = "complete", tools, ttlMs = 60000, cacheScope = "private", _meta = ServerMeta() }
            : new { tools };
    }

    private async Task<McpResponse> CallToolAsync(
        JsonElement root,
        JsonElement? id,
        string protocolVersion,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("name", out var nameElement))
        {
            return Error(id, -32602, "tools/call requires params.name.", 400);
        }

        var name = nameElement.GetString() ?? string.Empty;
        var arguments = parameters.TryGetProperty("arguments", out var argumentElement)
            ? argumentElement
            : JsonSerializer.SerializeToElement(new { });
        LastToolName = name;

        try
        {
            var data = name switch
            {
                "get_app_context" => GetAppContext(),
                "get_combo_generation_package" => GetGenerationPackage(arguments),
                "list_combos" => ListCombos(arguments),
                "validate_combo_pack" => ValidateComboPack(arguments),
                "import_combo_pack" => await ImportComboPackAsync(arguments, cancellationToken),
                "search_public_web" => await SearchPublicWebAsync(arguments, cancellationToken),
                _ => null
            };
            if (data is null)
            {
                return Error(id, -32602, $"Unknown tool: {name}", 400);
            }

            return Success(id, ToolResult(data, protocolVersion == ModernProtocolVersion), protocolVersion);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Success(id, ToolError(exception.Message, protocolVersion == ModernProtocolVersion), protocolVersion);
        }
    }

    private object GetAppContext()
    {
        var board = BoardLoader.Load(_boardPath);
        var patch = CurrentPatch(board);
        return new
        {
            appVersion = AppVersionInfo.Current,
            dotaPatch = patch,
            endpoint = Endpoint,
            formatVersion = 1,
            mainComboCount = board.Rows.Count,
            rankComboCounts = new { archon = board.RankCombos.Archon.Count, legend = board.RankCombos.Legend.Count },
            requiredPickOrder = new[] { "Carry", "Mid", "Support" },
            chromeExtensionLinked = _browserSyncServer.IsConnected
        };
    }

    private object GetGenerationPackage(JsonElement arguments)
    {
        var board = BoardLoader.Load(_boardPath);
        var patch = GetString(arguments, "patch") ?? CurrentPatch(board);
        var count = Math.Clamp(GetInt(arguments, "count", 15), 1, 100);
        var style = GetString(arguments, "style");
        var prompt = _comboService.CreateGenerationPrompt(patch)
            .Replace("Generate 15 unique combos.", $"Generate {count} unique combos.", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(style))
        {
            prompt += $"\n- Prioritize the '{style.Trim()}' play style while preserving practical draft balance.";
        }

        return new
        {
            formatVersion = 1,
            dotaPatch = patch,
            requestedComboCount = count,
            style = style ?? string.Empty,
            generationPrompt = prompt,
            template = _comboService.CreateTemplate(patch, prompt)
        };
    }

    private object ListCombos(JsonElement arguments)
    {
        var board = BoardLoader.Load(_boardPath);
        var rank = (GetString(arguments, "rank") ?? "all").ToLowerInvariant();
        var rows = rank switch
        {
            "all" => board.Rows,
            "archon" => board.RankCombos.Archon,
            "legend" => board.RankCombos.Legend,
            _ => throw new ArgumentException("rank must be all, archon, or legend.")
        };
        var limit = Math.Clamp(GetInt(arguments, "limit", 60), 1, 100);
        var selectedBoard = new BoardConfig { Rows = rows.Take(limit).ToList() };
        var patch = CurrentPatch(board);
        var pack = _comboService.CreatePack(selectedBoard, patch, _comboService.CreateGenerationPrompt(patch));
        return new { rank, count = pack.Combos.Count, comboPack = pack };
    }

    private object ValidateComboPack(JsonElement arguments)
    {
        var pack = ReadComboPack(arguments);
        _comboService.ValidatePack(pack);
        return new
        {
            valid = true,
            formatVersion = pack.FormatVersion,
            dotaPatch = pack.DotaPatch,
            comboCount = pack.Combos.Count,
            uniqueHeroSets = pack.Combos
                .Select(combo => string.Join("|", combo.Picks.Select(pick => pick.Hero.ToLowerInvariant())))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
    }

    private async Task<object> ImportComboPackAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!GetBool(arguments, "confirm"))
        {
            throw new InvalidOperationException("Set confirm to true before importing data.");
        }

        var mode = (GetString(arguments, "mode") ?? "append").ToLowerInvariant();
        if (mode is not "append" and not "replace")
        {
            throw new ArgumentException("mode must be append or replace.");
        }

        var pack = ReadComboPack(arguments);
        await _importGate.WaitAsync(cancellationToken);
        try
        {
            var result = _comboService.ApplyImport(_boardPath, pack, replace: mode == "replace");
            return new
            {
                imported = true,
                mode,
                receivedCount = result.ImportedCount,
                addedCount = result.AddedCount,
                skippedDuplicates = result.SkippedDuplicates,
                backupCreated = true
            };
        }
        finally
        {
            _importGate.Release();
        }
    }

    private async Task<object> SearchPublicWebAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var query = GetString(arguments, "query") ?? throw new ArgumentException("query is required.");
        var sources = arguments.TryGetProperty("sources", out var sourcesElement) && sourcesElement.ValueKind == JsonValueKind.Array
            ? sourcesElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToList()
            : new List<string> { "valve", "d2pt", "dotabuff", "general" };
        var response = await _browserSyncServer.SearchAsync(query, sources, GetInt(arguments, "maxResults", 20), cancellationToken);
        return new
        {
            response.Query,
            response.RetrievedAt,
            count = response.Results.Count,
            response.Results
        };
    }

    private ComboPack ReadComboPack(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("comboPack", out var comboPack))
        {
            throw new ArgumentException("comboPack is required.");
        }

        return _comboService.ParsePack(comboPack.GetRawText());
    }

    private string CurrentPatch(BoardConfig board)
    {
        var patch = _patchProvider();
        return !string.IsNullOrWhiteSpace(patch)
            ? patch
            : !string.IsNullOrWhiteSpace(board.RankCombos.Patch) ? board.RankCombos.Patch : "current";
    }

    private static object ToolResult(object data, bool modern)
    {
        var result = new Dictionary<string, object?>
        {
            ["content"] = new[] { new { type = "text", text = JsonSerializer.Serialize(data, JsonOptions) } },
            ["structuredContent"] = data,
            ["isError"] = false
        };
        if (modern)
        {
            result["resultType"] = "complete";
            result["_meta"] = ServerMeta();
        }

        return result;
    }

    private static object ToolError(string message, bool modern)
    {
        var result = new Dictionary<string, object?>
        {
            ["content"] = new[] { new { type = "text", text = message } },
            ["structuredContent"] = new { error = message },
            ["isError"] = true
        };
        if (modern)
        {
            result["resultType"] = "complete";
            result["_meta"] = ServerMeta();
        }

        return result;
    }

    private static object Tool(string name, string description, object inputSchema, object annotations) => new
    {
        name,
        description,
        inputSchema,
        annotations
    };

    private static object EmptySchema() => new { type = "object", properties = new { }, additionalProperties = false };

    private static object ObjectSchema(object properties, string[]? required = null) => new
    {
        type = "object",
        properties,
        required = required ?? [],
        additionalProperties = false
    };

    private static object ReadOnlyAnnotations() => new
    {
        readOnlyHint = true,
        destructiveHint = false,
        idempotentHint = true,
        openWorldHint = false
    };

    private static string ServerInstructions() =>
        "Use get_combo_generation_package before generating teams. Return Combo Pack formatVersion 1 with exactly Carry, Mid, Support picks. Validate before import; import requires confirm=true.";

    private static object ServerInfo() => new { name = ServerName, version = AppVersionInfo.Current };

    private static object ServerMeta() => new Dictionary<string, object>
    {
        ["io.modelcontextprotocol/serverInfo"] = ServerInfo()
    };

    private static McpResponse Success(JsonElement? id, object result, string? protocolVersion) => new(
        200,
        new { jsonrpc = "2.0", id, result },
        false,
        protocolVersion);

    private static McpResponse Error(JsonElement? id, int code, string message, int statusCode, object? data = null) => new(
        statusCode,
        new { jsonrpc = "2.0", id, error = new { code, message, data } },
        false,
        null);

    private static string? GetModernProtocolVersion(JsonElement root)
    {
        return root.TryGetProperty("params", out var parameters)
               && parameters.TryGetProperty("_meta", out var meta)
               && meta.TryGetProperty("io.modelcontextprotocol/protocolVersion", out var version)
            ? version.GetString()
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string propertyName, int fallback)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
               && property.TryGetInt32(out var value)
            ? value
            : fallback;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
               && property.ValueKind is JsonValueKind.True;
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https"
               && uri.Host is "127.0.0.1" or "localhost";
    }

    private static async Task<HttpRequest> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var headerMemory = new MemoryStream();
        uint tail = 0;
        while (headerMemory.Length < 32 * 1024)
        {
            var single = new byte[1];
            await ReadExactAsync(stream, single, cancellationToken);
            headerMemory.WriteByte(single[0]);
            tail = (tail << 8) | single[0];
            if (tail == 0x0D0A0D0A)
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerMemory.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var requestParts = lines.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (requestParts.Length < 2)
        {
            throw new InvalidDataException("Invalid HTTP request line.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var contentLength = headers.TryGetValue("Content-Length", out var lengthText)
                            && int.TryParse(lengthText, out var parsedLength)
            ? parsedLength
            : 0;
        if (contentLength < 0 || contentLength > MaxRequestBytes)
        {
            throw new InvalidDataException("MCP request exceeds the 2 MB limit.");
        }

        var body = new byte[contentLength];
        await ReadExactAsync(stream, body, cancellationToken);
        headers.TryGetValue("Origin", out var origin);
        headers.TryGetValue("MCP-Protocol-Version", out var protocolVersion);
        var rawPath = requestParts[1];
        var path = rawPath.Split('?', 2)[0];
        return new HttpRequest(requestParts[0].ToUpperInvariant(), path, headers, body, origin, protocolVersion ?? string.Empty);
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new IOException("Connection closed.");
            }

            offset += read;
        }
    }

    private static async Task WriteJsonAsync(
        NetworkStream stream,
        int statusCode,
        object payload,
        string? origin,
        string? protocolVersion,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var headers = CreateResponseHeaders(statusCode, "application/json; charset=utf-8", body.Length, origin, protocolVersion);
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static async Task WriteEmptyAsync(NetworkStream stream, int statusCode, string? origin, CancellationToken cancellationToken)
    {
        var headers = CreateResponseHeaders(statusCode, null, 0, origin, null);
        await stream.WriteAsync(headers, cancellationToken);
    }

    private static byte[] CreateResponseHeaders(
        int statusCode,
        string? contentType,
        int contentLength,
        string? origin,
        string? protocolVersion)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            202 => "Accepted",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            _ => "Error"
        };
        var builder = new StringBuilder($"HTTP/1.1 {statusCode} {reason}\r\nConnection: close\r\nContent-Length: {contentLength}\r\n");
        if (contentType is not null)
        {
            builder.Append($"Content-Type: {contentType}\r\n");
        }
        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            builder.Append($"MCP-Protocol-Version: {protocolVersion}\r\n");
        }
        if (!string.IsNullOrWhiteSpace(origin))
        {
            builder.Append($"Access-Control-Allow-Origin: {origin}\r\nVary: Origin\r\n");
        }
        builder.Append("Access-Control-Allow-Methods: POST, OPTIONS\r\n");
        builder.Append("Access-Control-Allow-Headers: Content-Type, MCP-Protocol-Version\r\n");
        builder.Append("Access-Control-Expose-Headers: MCP-Protocol-Version\r\n\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static async Task TryWriteErrorAsync(
        TcpClient client,
        int statusCode,
        JsonElement? id,
        int code,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            if (client.Connected)
            {
                await WriteJsonAsync(client.GetStream(), statusCode, new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code, message }
                }, null, null, cancellationToken);
            }
        }
        catch
        {
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _listener?.Stop();
        IsRunning = false;
        _importGate.Dispose();
        _shutdown.Dispose();
    }

    private sealed record HttpRequest(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body,
        string? Origin,
        string ProtocolVersion);

    private sealed record McpResponse(int StatusCode, object? Payload, bool IsNotification, string? ProtocolVersion);
}
