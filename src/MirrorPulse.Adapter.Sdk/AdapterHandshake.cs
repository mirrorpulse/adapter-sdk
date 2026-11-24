using System.Text.Json;
using System.Text.Json.Serialization;

namespace MirrorPulse.Adapter.Sdk;

/// <summary>The inclusive lower and upper envelope versions offered during negotiation.</summary>
public sealed record AdapterVersionRange(int Minimum, int Maximum);

/// <summary>The Worker version range and the case-sensitive capabilities it offers.</summary>
public sealed record AdapterHello(AdapterVersionRange SupportedVersions, IReadOnlyList<string> Capabilities);

/// <summary>An authorized root key with its enabled state and Host-owned settings.</summary>
/// <remarks>A disabled binding remains addressable for protocol errors and must never open a storage connection.</remarks>
public sealed record AdapterRootBinding(string RootKey, bool Enabled, IReadOnlyDictionary<string, string> Configuration);

/// <summary>The Host response selecting a protocol and supplying settings for the Worker session.</summary>
/// <remarks>Use the selected capabilities for this session. Optional offers from Hello can be absent from Ready.</remarks>
public sealed record AdapterReady(int SelectedVersion, IReadOnlyList<string> Capabilities,
    IReadOnlyList<AdapterRootBinding> Roots, IReadOnlyDictionary<string, string> Configuration)
{
    /// <summary>Indicates a temporary authorization session whose root list is empty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConfigurationOnly { get; init; }

    /// <summary>Opaque Host credential references indexed by signed password field key.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? CredentialBindings { get; init; }
}

public sealed record AdapterNegotiation(int? SelectedVersion, string? ErrorCode)
{
    public bool Accepted => SelectedVersion is not null;
}

/// <summary>Negotiates Worker protocol versions and checks the Host's Ready response.</summary>
/// <remarks>Send the initial Hello in envelope version 1, including for a v2-only Worker.</remarks>
public static class AdapterHandshake
{
    private static readonly string[] RequiredV2Capabilities =
        ["root-addresses", "stable-operations", "conditional-targets", "bounded-streams", "cancel-ack"];

    /// <summary>The mandatory capabilities for a protocol-v2 deployment session.</summary>
    public static IReadOnlyList<string> V2Capabilities { get; } = Array.AsReadOnly(RequiredV2Capabilities);

    /// <summary>Finds the highest common supported version for an ordinary deployment.</summary>
    /// <remarks>A v2 offer missing a mandatory capability is rejected. Configuration-only sessions use the separate Ready validation path.</remarks>
    public static AdapterNegotiation Negotiate(AdapterHello offer, int configuredRootCount,
        AdapterVersionRange? hostVersions = null)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentOutOfRangeException.ThrowIfNegative(configuredRootCount);
        hostVersions ??= new(1, 2);
        if (offer.SupportedVersions is null || offer.SupportedVersions.Minimum < 1 ||
            offer.SupportedVersions.Maximum < offer.SupportedVersions.Minimum ||
            hostVersions.Minimum < 1 || hostVersions.Maximum < hostVersions.Minimum)
            return new(null, "InvalidVersionOffer");
        int selected = Math.Min(2, Math.Min(offer.SupportedVersions.Maximum, hostVersions.Maximum));
        if (selected < Math.Max(offer.SupportedVersions.Minimum, hostVersions.Minimum))
            return new(null, "ProtocolVersionUnsupported");
        if (selected == 2 && (offer.Capabilities is null ||
            RequiredV2Capabilities.Any(c => !offer.Capabilities.Contains(c, StringComparer.Ordinal))))
            return new(null, "RequiredCapabilityMissing");
        if (selected == 2 && configuredRootCount == 0) return new(null, "InvalidRoots");
        if (selected == 1 && configuredRootCount > 1)
            return new(null, "MultipleRootsRequireProtocolV2");
        return new(selected, null);
    }

    /// <summary>Older Workers had no version offer; malformed offers never use this fallback.</summary>
    public static AdapterHello ReadHello(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) throw new InvalidDataException("InvalidHello");
        if (!payload.TryGetProperty("supportedVersions", out _)) return new(new(1, 1), []);
        return AdapterProtocolJson.Decode<AdapterHello>(System.Text.Encoding.UTF8.GetBytes(payload.GetRawText()));
    }

    /// <summary>Validates Ready for an ordinary deployment before the Worker selects the negotiated protocol.</summary>
    public static void ValidateReady(AdapterReady ready, AdapterHello offer) =>
        ValidateReady(ready, offer, configurationOnly: false);

    /// <summary>Checks Ready against the session mode supplied in the Worker launch arguments.</summary>
    /// <exception cref="InvalidDataException">The selected version, capabilities, root bindings or mode are invalid.</exception>
    public static void ValidateReady(AdapterReady ready, AdapterHello offer, bool configurationOnly)
    {
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(offer);
        if (ready.ConfigurationOnly != configurationOnly)
            throw new InvalidDataException("ReadySessionModeMismatch");
        if (configurationOnly)
        {
            if (ready.SelectedVersion != 2 || ready.Roots is not { Count: 0 } || ready.Configuration is null ||
                ready.Capabilities is not { Count: 1 } || ready.Capabilities[0] != AdapterConfigurationCallbackContract.Capability ||
                offer.SupportedVersions is not { Minimum: >= 1 and <= 2, Maximum: >= 2 } ||
                offer.Capabilities?.Contains(AdapterConfigurationCallbackContract.Capability, StringComparer.Ordinal) is not true)
                throw new InvalidDataException("InvalidConfigurationOnlyReady");
            return;
        }
        AdapterNegotiation result = Negotiate(offer, ready.Roots?.Count ?? 0,
            new(ready.SelectedVersion, ready.SelectedVersion));
        if (!result.Accepted) throw new InvalidDataException(result.ErrorCode);
        if (ready.SelectedVersion == 2 && (ready.Roots is null || ready.Roots.Count == 0 || ready.Capabilities is null ||
            RequiredV2Capabilities.Any(c => !ready.Capabilities.Contains(c, StringComparer.Ordinal))))
            throw new InvalidDataException("InvalidReady");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (AdapterRootBinding root in ready.Roots ?? [])
        {
            new AdapterFileAddress(root.RootKey, "").Validate();
            if (!keys.Add(root.RootKey) || root.Configuration is null) throw new InvalidDataException("InvalidRoots");
        }
    }
}
