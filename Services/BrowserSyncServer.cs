using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotaComboBoard.Services;

public sealed class BrowserSyncServer : IDisposable
{
    public const int Port = 37821;
    private const int MaxMessageBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserSearchResponse>> _pending = new();
    private readonly object _clientGate = new();
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private NetworkStream? _activeStream;
    private Task? _acceptLoop;
    private bool _disposed;

    public BrowserSyncServer()
    {
        PairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    public string PairingCode { get; }
    public bool IsConnected { get; private set; }
    public string ExtensionName { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;

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
            ErrorMessage = string.Empty;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
            RaiseStatusChanged();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            RaiseStatusChanged();
        }
    }

    public async Task<BrowserSearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string> sources,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is empty.", nameof(query));
        }

        NetworkStream stream;
        lock (_clientGate)
        {
            stream = _activeStream
                ?? throw new InvalidOperationException("Chrome extension is not connected.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<BrowserSearchResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;
        try
        {
            await SendJsonAsync(stream, new
            {
                type = "search_request",
                requestId,
                query = query.Trim(),
                sources,
                maxResults = Math.Clamp(maxResults, 5, 50)
            }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
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
            catch (Exception exception)
            {
                ErrorMessage = exception.Message;
                RaiseStatusChanged();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var paired = false;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var headers = await ReadHandshakeAsync(stream, cancellationToken);
            if (!headers.RequestLine.Equals("GET /dota-sync HTTP/1.1", StringComparison.OrdinalIgnoreCase)
                || !headers.Values.TryGetValue("Origin", out var origin)
                || !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
                || !headers.Values.TryGetValue("Sec-WebSocket-Key", out var webSocketKey))
            {
                client.Dispose();
                return;
            }

            var acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                webSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                var frame = await ReadFrameAsync(stream, cancellationToken);
                if (frame.Opcode == 8)
                {
                    break;
                }

                if (frame.Opcode == 9)
                {
                    await SendFrameAsync(stream, 10, frame.Payload, cancellationToken);
                    continue;
                }

                if (frame.Opcode != 1)
                {
                    continue;
                }

                using var message = JsonDocument.Parse(frame.Payload);
                var root = message.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!paired)
                {
                    if (!string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var pairingCode = root.TryGetProperty("pairingCode", out var codeElement)
                        ? codeElement.GetString()
                        : null;
                    if (!string.Equals(pairingCode, PairingCode, StringComparison.Ordinal))
                    {
                        await SendJsonAsync(stream, new { type = "pair_rejected", message = "Pairing code is incorrect." }, cancellationToken);
                        continue;
                    }

                    var extensionVersion = root.TryGetProperty("extensionVersion", out var versionElement)
                        ? versionElement.GetString()
                        : "unknown";
                    lock (_clientGate)
                    {
                        _activeClient?.Dispose();
                        _activeClient = client;
                        _activeStream = stream;
                        IsConnected = true;
                        ExtensionName = $"Chrome Extension v{extensionVersion}";
                        ErrorMessage = string.Empty;
                    }

                    paired = true;
                    await SendJsonAsync(stream, new
                    {
                        type = "hello_ack",
                        protocolVersion = 1,
                        appVersion = AppVersionInfo.Current,
                        capabilities = new[] { "public_web_search", "bulk_results" }
                    }, cancellationToken);
                    RaiseStatusChanged();
                    continue;
                }

                switch (type)
                {
                    case "heartbeat":
                        await SendJsonAsync(stream, new { type = "heartbeat_ack", at = DateTimeOffset.UtcNow }, cancellationToken);
                        break;
                    case "search_result":
                        CompleteSearch(root);
                        break;
                    case "search_error":
                        FailSearch(root);
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or JsonException)
        {
            if (exception is not OperationCanceledException)
            {
                ErrorMessage = exception.Message;
            }
        }
        finally
        {
            lock (_clientGate)
            {
                if (ReferenceEquals(_activeClient, client))
                {
                    _activeClient = null;
                    _activeStream = null;
                    IsConnected = false;
                    ExtensionName = string.Empty;
                    FailPending(new IOException("Chrome extension disconnected."));
                }
            }

            client.Dispose();
            if (paired)
            {
                RaiseStatusChanged();
            }
        }
    }

    private void CompleteSearch(JsonElement root)
    {
        var response = root.Deserialize<BrowserSearchResponse>(JsonOptions);
        if (response is not null && _pending.TryRemove(response.RequestId, out var completion))
        {
            completion.TrySetResult(response);
        }
    }

    private void FailSearch(JsonElement root)
    {
        var requestId = root.TryGetProperty("requestId", out var requestIdElement)
            ? requestIdElement.GetString() ?? string.Empty
            : string.Empty;
        var message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "Browser search failed."
            : "Browser search failed.";
        if (_pending.TryRemove(requestId, out var completion))
        {
            completion.TrySetException(new InvalidOperationException(message));
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var request in _pending.ToArray())
        {
            if (_pending.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private async Task SendJsonAsync(NetworkStream stream, object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await SendFrameAsync(stream, 1, payload, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task SendFrameAsync(
        NetworkStream stream,
        byte opcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var frame = new MemoryStream();
        frame.WriteByte((byte)(0x80 | opcode));
        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)payload.Length);
        }
        else
        {
            frame.WriteByte(127);
            var length = (ulong)payload.Length;
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                frame.WriteByte((byte)(length >> shift));
            }
        }

        frame.Write(payload.Span);
        await stream.WriteAsync(frame.GetBuffer().AsMemory(0, (int)frame.Length), cancellationToken);
    }

    private static async Task<WebSocketFrame> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        await ReadExactAsync(stream, header, cancellationToken);
        if ((header[0] & 0x80) == 0)
        {
            throw new IOException("Fragmented WebSocket messages are not supported.");
        }

        var opcode = (byte)(header[0] & 0x0F);
        var masked = (header[1] & 0x80) != 0;
        ulong length = (byte)(header[1] & 0x7F);
        if (length == 126)
        {
            var extended = new byte[2];
            await ReadExactAsync(stream, extended, cancellationToken);
            length = (ulong)((extended[0] << 8) | extended[1]);
        }
        else if (length == 127)
        {
            var extended = new byte[8];
            await ReadExactAsync(stream, extended, cancellationToken);
            length = 0;
            foreach (var value in extended)
            {
                length = (length << 8) | value;
            }
        }

        if (!masked || length > MaxMessageBytes)
        {
            throw new IOException("Invalid WebSocket frame.");
        }

        var mask = new byte[4];
        await ReadExactAsync(stream, mask, cancellationToken);
        var payload = new byte[(int)length];
        await ReadExactAsync(stream, payload, cancellationToken);
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] ^= mask[index % 4];
        }

        return new WebSocketFrame(opcode, payload);
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

    private static async Task<HandshakeHeaders> ReadHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        uint tail = 0;
        while (memory.Length < 16 * 1024)
        {
            var single = new byte[1];
            await ReadExactAsync(stream, single, cancellationToken);
            memory.WriteByte(single[0]);
            tail = (tail << 8) | single[0];
            if (tail == 0x0D0A0D0A)
            {
                break;
            }
        }

        var text = Encoding.ASCII.GetString(memory.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            throw new IOException("Invalid WebSocket handshake.");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return new HandshakeHeaders(lines[0], values);
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
        lock (_clientGate)
        {
            _activeClient?.Dispose();
            _activeClient = null;
            _activeStream = null;
            IsConnected = false;
        }

        FailPending(new ObjectDisposedException(nameof(BrowserSyncServer)));
        _shutdown.Dispose();
        _sendGate.Dispose();
    }

    private sealed record HandshakeHeaders(string RequestLine, IReadOnlyDictionary<string, string> Values);
    private sealed record WebSocketFrame(byte Opcode, byte[] Payload);
}

public sealed class BrowserSearchResponse
{
    public string Type { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTimeOffset RetrievedAt { get; set; }
    public List<BrowserSearchResult> Results { get; set; } = [];
}

public sealed class BrowserSearchResult
{
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}
