using System.Text.Json.Serialization;

namespace MirrorPulse.Adapter.Sdk;

/// <summary>A time-limited Host request carried on the authenticated deployment's Worker channel.</summary>
public sealed record AdapterConfigurationCallbackRequest(
    Guid InvocationId, string CallbackId, DateTimeOffset ExpiresAt);

[JsonConverter(typeof(JsonStringEnumConverter<AdapterConfigurationCallbackState>))]
public enum AdapterConfigurationCallbackState { Pending, Completed, Cancelled, Failed }

/// <summary>The Worker proposes one field value. Its textual representation never includes the value.</summary>
public sealed class AdapterConfigurationWrite(string key, string value)
{
    public string Key { get; } = key;
    public string Value { get; } = value;

    public override string ToString() => nameof(AdapterConfigurationWrite);
}

/// <summary>Callback progress or proposed field writes. The response does not persist configuration.</summary>
public sealed record AdapterConfigurationCallbackResult(
    Guid InvocationId,
    string CallbackId,
    AdapterConfigurationCallbackState State,
    IReadOnlyList<AdapterConfigurationWrite>? Writes = null,
    Uri? InteractionUri = null,
    string? ErrorCode = null);

/// <summary>Contract for starting, polling and cancelling a package-declared callback.</summary>
public static class AdapterConfigurationCallbackContract
{
    public const string Capability = "configuration-callbacks";
    public const string StartMessage = "ConfigurationCallbackStart";
    public const string PollMessage = "ConfigurationCallbackPoll";
    public const string CancelMessage = "ConfigurationCallbackCancel";
    public const string ResultMessage = "ConfigurationCallbackResult";

    /// <summary>Reads a start, poll or cancel request on an already authenticated configuration-only channel.</summary>
    public static AdapterConfigurationCallbackRequest ReadRequest(AdapterControlFrame frame, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ProtocolVersion != 2 || frame.IsResponse || frame.RequestId == Guid.Empty ||
            frame.MessageType is not (StartMessage or PollMessage or CancelMessage))
            throw new InvalidDataException("The frame is not an authorization request.");

        var request = AdapterProtocolJson.Decode<AdapterConfigurationCallbackRequest>(
            System.Text.Encoding.UTF8.GetBytes(frame.Payload.GetRawText()));
        if (request.InvocationId == Guid.Empty || request.ExpiresAt <= observedAt || string.IsNullOrEmpty(request.CallbackId))
            throw new InvalidDataException("The authorization request is invalid or expired.");
        AdapterConfigurationForm.RequireIdentifier(request.CallbackId);
        return request;
    }

    /// <summary>
    /// Checks a proposed response after the Host verifies its edition, deployment and Worker session.
    /// The Host applies permitted writes atomically and stores passwords as credentials.
    /// </summary>
    public static void ValidateResult(AdapterConfigurationForm form, AdapterConfigurationCallbackRequest request,
        AdapterConfigurationCallbackResult result, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        if (request.InvocationId == Guid.Empty || observedAt >= request.ExpiresAt ||
            request.InvocationId != result.InvocationId || !string.Equals(result.CallbackId, request.CallbackId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The callback response does not match a live invocation.");
        }

        AdapterFormCallback callback = form.Controls.OfType<AdapterFormCallback>().SingleOrDefault(item =>
            string.Equals(item.Id, request.CallbackId, StringComparison.Ordinal)) ??
            throw new InvalidDataException("The callback is not declared in the installed configuration form.");
        IReadOnlyList<AdapterConfigurationWrite> writes = result.Writes ?? [];
        switch (result.State)
        {
            case AdapterConfigurationCallbackState.Pending when writes.Count == 0 && result.ErrorCode is null:
                if (result.InteractionUri is { } uri && !AdapterDeploymentContract.IsExternalHttpsUri(uri))
                {
                    throw new InvalidDataException("An external callback page must be an HTTPS URL without embedded credentials.");
                }
                break;
            case AdapterConfigurationCallbackState.Completed when result.InteractionUri is null && result.ErrorCode is null:
                ValidateWrites(form, callback, writes);
                break;
            case AdapterConfigurationCallbackState.Cancelled when writes.Count == 0 && result.InteractionUri is null && result.ErrorCode is null:
                break;
            case AdapterConfigurationCallbackState.Failed when writes.Count == 0 && result.InteractionUri is null && result.ErrorCode is not null:
                AdapterConfigurationForm.RequireIdentifier(result.ErrorCode);
                break;
            default:
                throw new InvalidDataException("The callback response fields do not match its state.");
        }
    }

    private static void ValidateWrites(AdapterConfigurationForm form, AdapterFormCallback callback,
        IReadOnlyList<AdapterConfigurationWrite> writes)
    {
        if (writes.Count == 0 || writes.Count > callback.WritableFields.Count)
        {
            throw new InvalidDataException("A completed callback requires a bounded set of permitted writes.");
        }

        var fields = form.Controls.OfType<AdapterFormField>().ToDictionary(field => field.Key, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AdapterConfigurationWrite write in writes)
        {
            if (write is null || write.Key is null || !seen.Add(write.Key) || !callback.WritableFields.Contains(write.Key, StringComparer.Ordinal) ||
                !fields.TryGetValue(write.Key, out AdapterFormField? field) || !AdapterConfigurationForm.AcceptsValue(field, write.Value))
            {
                throw new InvalidDataException("The callback proposed an undeclared, repeated or invalid field write.");
            }
        }
    }
}
