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

/// <summary>A proposed package download. The Host decides whether to fetch or install it.</summary>
public sealed record AdapterPackageUpdateOffer(string AdapterId, string Version, Uri Location);

public static class AdapterDeploymentContract
{
    public const string StructuredHealthCapability = "structured-health";
    public const string UpdateDiscoveryCapability = "update-discovery";
    public const string CheckUpdateMessage = "CheckAdapterUpdate";
    public const string UpdateOfferMessage = "AdapterUpdateOffer";

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

    /// <summary>Checks package identity, version ordering and safe location before treating an update offer as actionable.</summary>
    public static void ValidateUpdateOffer(AdapterPackageIdentity installed, AdapterPackageUpdateOffer offer)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(offer);
        if (!AdapterPackageMetadata.IsCanonicalId(installed.AdapterId) || offer.AdapterId != installed.AdapterId ||
            offer.Location is null || !IsExternalHttpsUri(offer.Location))
        {
            throw new InvalidDataException("An update offer must name the installed Adapter and an HTTPS download without embedded credentials.");
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
}
