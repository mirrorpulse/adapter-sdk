using System.Text.Json;

namespace MirrorPulse.Adapter.Sdk;

/// <summary>The control envelope shared by the bootstrap and negotiated Worker protocols.</summary>
public sealed record AdapterControlFrame(
    int ProtocolVersion,
    string MessageType,
    Guid RequestId,
    Guid InstanceId,
    Guid WorkerSessionId,
    bool IsResponse,
    JsonElement Payload);

/// <summary>One v2 frame, containing a control message or a binary chunk.</summary>
public sealed record AdapterWorkerFrame(AdapterControlFrame? Control, AdapterBinaryChunk? Chunk);

/// <summary>Frames control and binary messages for one MirrorPulse Worker session.</summary>
/// <remarks>The caller must serialize writes and keep a single reader. Validate the Host's Ready response before selecting its protocol.</remarks>
public sealed class AdapterControlChannel : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AdapterNamedPipeClient _pipe;
    private readonly Guid _instanceId;
    private readonly Guid _workerSessionId;
    private int _protocolVersion = 1;
    private bool _protocolSelected;

    /// <summary>The current envelope version. A new channel starts at version 1 for Hello.</summary>
    public int ProtocolVersion => _protocolVersion;

    /// <summary>Selects the protocol from the validated Ready response for this session.</summary>
    /// <exception cref="InvalidDataException">The selected version is outside the supported set.</exception>
    /// <exception cref="InvalidOperationException">A different version has already been selected.</exception>
    public void SelectProtocol(int version)
    {
        if (version is not (1 or 2)) throw new InvalidDataException("ProtocolVersionUnsupported");
        if (_protocolSelected && version != _protocolVersion)
            throw new InvalidOperationException("The session protocol is already selected.");
        _protocolVersion = version;
        _protocolSelected = true;
    }

    public AdapterControlChannel(AdapterNamedPipeClient pipe, Guid instanceId, Guid workerSessionId)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        if (instanceId == Guid.Empty || workerSessionId == Guid.Empty)
        {
            throw new ArgumentException("A control channel requires non-empty instance and session IDs.");
        }

        _instanceId = instanceId;
        _workerSessionId = workerSessionId;
    }

    /// <summary>Sends a JSON control envelope using the current version, instance and session identifiers.</summary>
    /// <remarks>Use the incoming request ID for its response and a fresh nonempty ID for an independent request or event.</remarks>
    public async ValueTask SendAsync(
        string messageType,
        Guid requestId,
        bool isResponse,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(payload);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A control request ID cannot be empty.", nameof(requestId));
        }

        var frame = new AdapterControlFrame(
            _protocolVersion, messageType, requestId, _instanceId, _workerSessionId, isResponse,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        await _pipe.WriteFrameAsync(JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads and validates one control frame when the next message is expected to be JSON.</summary>
    /// <remarks>Match the returned request ID and message type to the exchange. Use <see cref="ReadNextAsync"/> during v2 uploads.</remarks>
    public async ValueTask<AdapterControlFrame> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] bytes = await _pipe.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        return DecodeControl(bytes);
    }

    private AdapterControlFrame DecodeControl(byte[] bytes)
    {
        AdapterControlFrame frame = JsonSerializer.Deserialize<AdapterControlFrame>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The Host sent a null control frame.");
        bool selectingV2 = _protocolVersion == 1 && frame.ProtocolVersion == 2 &&
            frame.MessageType == "Ready" && frame.IsResponse;
        if ((!selectingV2 && frame.ProtocolVersion != _protocolVersion) || frame.InstanceId != _instanceId ||
            frame.WorkerSessionId != _workerSessionId || frame.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(frame.MessageType) || frame.Payload.ValueKind is JsonValueKind.Undefined)
        {
            throw new InvalidDataException("The Host sent a control frame for another session or protocol.");
        }

        return frame;
    }

    /// <summary>Reads the next control message or upload chunk on a negotiated v2 channel.</summary>
    /// <remarks>Use the same reader for both frame kinds so a Cancel command can be handled between upload chunks.</remarks>
    public async ValueTask<AdapterWorkerFrame> ReadNextAsync(CancellationToken cancellationToken = default)
    {
        if (_protocolVersion != 2) throw new InvalidOperationException("Multiplexed frames require protocol v2.");
        byte[] bytes = await _pipe.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.AsSpan().StartsWith("MPB2"u8))
        {
            AdapterBinaryChunk chunk = AdapterBinaryChunkV2Codec.Decode(bytes);
            if (chunk.InstanceId != _instanceId || chunk.WorkerSessionId != _workerSessionId)
                throw new InvalidDataException("The binary chunk belongs to another Worker session.");
            return new(null, chunk);
        }
        return new(DecodeControl(bytes), null);
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();

    /// <summary>Sends a binary chunk with the encoding selected for the session.</summary>
    public ValueTask SendChunkAsync(AdapterBinaryChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.InstanceId != _instanceId || chunk.WorkerSessionId != _workerSessionId)
        {
            throw new InvalidDataException("The binary chunk belongs to another Worker session.");
        }

        return _pipe.WriteFrameAsync(_protocolVersion == 2 ? AdapterBinaryChunkV2Codec.Encode(chunk)
            : AdapterBinaryChunkCodec.Encode(chunk), cancellationToken);
    }

    /// <summary>Reads one binary chunk when the protocol exchange expects binary data next.</summary>
    public async ValueTask<AdapterBinaryChunk> ReadChunkAsync(CancellationToken cancellationToken = default)
    {
        byte[] bytes = await _pipe.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        AdapterBinaryChunk chunk = _protocolVersion == 2 ? AdapterBinaryChunkV2Codec.Decode(bytes)
            : AdapterBinaryChunkCodec.Decode(bytes);
        if (chunk.InstanceId != _instanceId || chunk.WorkerSessionId != _workerSessionId)
        {
            throw new InvalidDataException("The binary chunk belongs to another Worker session.");
        }

        return chunk;
    }
}

/// <summary>The Host-provided Worker process identity, pipe name and launch mode.</summary>
public sealed record AdapterWorkerProcessArguments(Guid InstanceId, Guid WorkerSessionId, string PipeName)
{
    public const string ConfigurationOnlyOption = "--configuration-only";

    /// <summary>Indicates a temporary session that may handle authorization before a sync root is configured.</summary>
    public bool ConfigurationOnly { get; init; }

    /// <summary>Parses the documented argument sequence supplied by the Host.</summary>
    /// <exception cref="ArgumentException">The order, identifiers, pipe name or optional launch mode is invalid.</exception>
    public static AdapterWorkerProcessArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        bool configurationOnly = args.Count == 7 && args[6] == ConfigurationOnlyOption;
        if ((args.Count != 6 && !configurationOnly) || args[0] != "--instance-id" || args[2] != "--worker-session-id" ||
            args[4] != "--pipe-name" || !Guid.TryParse(args[1], out Guid instanceId) ||
            !Guid.TryParse(args[3], out Guid sessionId) || instanceId == Guid.Empty ||
            sessionId == Guid.Empty || string.IsNullOrWhiteSpace(args[5]))
        {
            throw new ArgumentException("Expected --instance-id, --worker-session-id, --pipe-name, and optional --configuration-only.", nameof(args));
        }

        return new(instanceId, sessionId, args[5]) { ConfigurationOnly = configurationOnly };
    }
}
