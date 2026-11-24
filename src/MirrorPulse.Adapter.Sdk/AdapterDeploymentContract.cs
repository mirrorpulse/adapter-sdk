using System.Text.Json.Serialization;

namespace MirrorPulse.Adapter.Sdk;

[JsonConverter(typeof(JsonStringEnumConverter<AdapterHealthReason>))]
public enum AdapterHealthReason
{
    MissingConfiguration,
    InvalidConfiguration,
    MissingCredential,
    ExpiredCredential,
    SourceUnavailable,
    WorkerFailure,
}

/// <summary>A machine-readable reason for an unhealthy deployment. A form field can be identified when known.</summary>
public sealed record AdapterHealthIssue(AdapterHealthReason Reason, string? FieldKey = null);

/// <summary>The current issues for one negotiated deployment session. An empty list clears previous issues.</summary>
public sealed record AdapterHealthReport(IReadOnlyList<AdapterHealthIssue> Issues);

/// <summary>A proposed package at an HTTPS URL or a local Windows file location.</summary>
public sealed record AdapterPackageUpdateOffer(
    [property: JsonPropertyName("adapterId")] string AdapterId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("location")] Uri Location);

/// <summary>The response to a deployment-wide update check. A null offer means the selected version is current.</summary>
public sealed record AdapterPackageUpdateResult(
    [property: JsonPropertyName("offer"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] AdapterPackageUpdateOffer? Offer);

/// <summary>Message names, capability identifiers and validators for deployment health and package update discovery.</summary>
/// <remarks>Advertise either capability only after implementing its exchange. A Worker sends reports and
/// responds to update requests only when the Host selects the corresponding capability.</remarks>
public static class AdapterDeploymentContract
{
    public const string StructuredHealthCapability = "structured-health";
    public const string HealthReportMessage = "AdapterHealthReport";
    public const int MaximumHealthIssues = 16;
    public const string UpdateDiscoveryCapability = "update-discovery";
    public const string CheckUpdateMessage = "CheckAdapterUpdate";
    public const string UpdateOfferMessage = "AdapterUpdateOffer";

    /// <summary>Checks that the health reason is supported and any field key is applicable in the signed form.</summary>
    /// <remarks>Credential reasons can name a password field. Source and Worker failures are deployment-wide.</remarks>
    public static void ValidateHealthIssue(AdapterConfigurationForm form, AdapterHealthIssue issue)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(issue);
        if (!Enum.IsDefined(issue.Reason)) throw new InvalidDataException("The deployment health reason is unknown.");
        if (issue.FieldKey is null) return;

        AdapterFormField? field = form.Controls.OfType<AdapterFormField>().SingleOrDefault(item => item.Key == issue.FieldKey);
        if (field is null || issue.Reason is AdapterHealthReason.SourceUnavailable or AdapterHealthReason.WorkerFailure ||
            (issue.Reason is AdapterHealthReason.MissingCredential or AdapterHealthReason.ExpiredCredential &&
                field.Kind != AdapterFormFieldKind.Password))
        {
            throw new InvalidDataException("The deployment health reason does not refer to an applicable configuration field.");
        }
    }

    /// <summary>Checks the issue count, uniqueness and field bindings of a replacement health report.</summary>
    /// <remarks>Send an empty list when the issues clear.</remarks>
    public static void ValidateHealthReport(AdapterConfigurationForm form, AdapterHealthReport report)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(report);
        if (report.Issues is null || report.Issues.Count > MaximumHealthIssues)
            throw new InvalidDataException("The deployment health report exceeds its issue limit.");

        var unique = new HashSet<AdapterHealthIssue>();
        foreach (AdapterHealthIssue issue in report.Issues)
        {
            if (issue is null || !unique.Add(issue))
                throw new InvalidDataException("The deployment health report contains an empty or repeated issue.");
            ValidateHealthIssue(form, issue);
        }
    }

    /// <summary>Checks package identity, version ordering and safe location before treating an update offer as actionable.</summary>
    /// <remarks>The Host separately verifies the downloaded package and its signature before registering the new version.</remarks>
    public static void ValidateUpdateOffer(AdapterPackageIdentity installed, AdapterPackageUpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(offer);
        if (!AdapterPackageMetadata.IsCanonicalId(installed.AdapterId) || offer.AdapterId != installed.AdapterId ||
            offer.Location is null || !IsPackageLocation(offer.Location))
        {
            throw new InvalidDataException("An update offer must name the installed Adapter and provide an HTTPS URL or an absolute local Windows .mpadapter path.");
        }

        var (oldVersion, oldPreview) = AdapterPackageMetadata.ParseVersion(installed.Version);
        var (newVersion, newPreview) = AdapterPackageMetadata.ParseVersion(offer.Version);
        if (newVersion < oldVersion || (newVersion == oldVersion &&
            (oldPreview is null || (newPreview.HasValue && newPreview.Value <= oldPreview.Value))))
        {
            throw new InvalidDataException("An update offer must be newer than the installed package.");
        }
    }

    internal static bool IsExternalHttpsUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Length != 0 && uri.UserInfo.Length == 0 && uri.OriginalString.Length <= 2048;

    private static bool IsPackageLocation(Uri uri) => uri.IsAbsoluteUri && uri.OriginalString.Length <= 2048 &&
        !uri.OriginalString.Any(char.IsControl) && uri.Fragment.Length == 0 &&
        ((IsExternalHttpsUri(uri) && !uri.IsLoopback) ||
        (uri.IsFile && !uri.IsUnc && uri.Host.Length == 0 && uri.Query.Length == 0 &&
            uri.LocalPath is { Length: >= 4 } path && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
            path[2] is '\\' or '/' && !path.AsSpan(2).Contains(':') && !path.Any(char.IsControl) &&
            string.Equals(Path.GetExtension(path), ".mpadapter", StringComparison.OrdinalIgnoreCase)));
}
