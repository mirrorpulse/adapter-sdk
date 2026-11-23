using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MirrorPulse.Adapter.Sdk;

/// <summary>Stable identity used by signed package indexes and installation requests.</summary>
public sealed record AdapterPackageIdentity(string AdapterId, string Version);

/// <summary>Package-local presentation and form metadata for SDK 1.0 packages.</summary>
public sealed partial record AdapterPackageMetadata(
    AdapterPackageIdentity Identity,
    string RootKey,
    string TitleKey,
    string DescriptionKey,
    string LogoPath,
    string FormPath,
    IReadOnlyDictionary<string, string> LocaleFiles)
{
    public const int SchemaVersion = 2;

    /// <summary>
    /// Validates the SDK 1.0 fields against the verified, case-sensitive file inventory.
    /// The Host remains responsible for signature, protocol, version and Worker execution checks.
    /// </summary>
    public static AdapterPackageMetadata Read(JsonElement manifest, IReadOnlyCollection<string> signedFiles,
        AdapterPackageIdentity? expectedIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(signedFiles);
        RequireUniqueProperties(manifest);
        if (ReadInteger(manifest, "schemaVersion") != SchemaVersion || manifest.TryGetProperty("configurationFields", out _))
        {
            throw new InvalidDataException("The package must use manifest schema 2 and a separate configuration form.");
        }

        var identity = new AdapterPackageIdentity(ReadString(manifest, "adapterId"), ReadString(manifest, "version"));
        if (!IsCanonicalId(identity.AdapterId) || (expectedIdentity is not null && identity != expectedIdentity))
        {
            throw new InvalidDataException("The package identity is invalid or differs from the requested identity.");
        }
        _ = ParseVersion(identity.Version);

        JsonElement roots = ReadProperty(manifest, "rootDefinitions", JsonValueKind.Array);
        if (roots.GetArrayLength() != 1 ||
            ReadInteger(ReadProperty(manifest, "instancePolicy", JsonValueKind.Object), "maximumRootDefinitions") != 1)
        {
            throw new InvalidDataException("An SDK 1.0 package must declare exactly one root per deployment.");
        }

        string rootKey = ReadString(roots[0], "key");
        new AdapterFileAddress(rootKey, string.Empty).Validate();
        var files = signedFiles.ToHashSet(StringComparer.Ordinal);
        JsonElement presentation = ReadProperty(manifest, "presentation", JsonValueKind.Object);
        string titleKey = AdapterConfigurationForm.RequireIdentifier(ReadString(presentation, "titleKey"));
        string descriptionKey = AdapterConfigurationForm.RequireIdentifier(ReadString(presentation, "descriptionKey"));
        string logoPath = RequireImagePath(ReadString(presentation, "logoPath"), files);
        string formPath = RequireAsset(ReadString(presentation, "formPath"), ".xml", files);

        string[] locales = ReadProperty(manifest, "locales", JsonValueKind.Array).EnumerateArray()
            .Select(locale => locale.ValueKind == JsonValueKind.String ? locale.GetString()! : string.Empty).ToArray();
        var localeFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty locale in ReadProperty(manifest, "localeMetadata", JsonValueKind.Object).EnumerateObject())
        {
            localeFiles.Add(locale.Name, RequireAsset(ReadString(locale.Value, "resourcePath"), ".json", files));
        }

        if (!localeFiles.ContainsKey("en-US") || locales.Length != localeFiles.Count ||
            locales.Distinct(StringComparer.OrdinalIgnoreCase).Count() != locales.Length ||
            locales.Any(locale => locale.Length is 0 or > 35 || !LocalePattern().IsMatch(locale) || !localeFiles.ContainsKey(locale)))
        {
            throw new InvalidDataException("Declared locale files must match the locale list and include en-US.");
        }

        return new(identity, rootKey, titleKey, descriptionKey, logoPath, formPath,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(localeFiles));
    }

    internal static string RequireImagePath(string path, IReadOnlySet<string> files)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".ico" or ".bmp"))
        {
            throw new InvalidDataException("The package image format is not supported.");
        }

        return RequireAsset(path, extension, files);
    }

    internal static bool IsCanonicalId(string? id) => id is { Length: <= 128 } && PackageIdPattern().IsMatch(id);

    internal static (Version Version, int? Preview) ParseVersion(string? value)
    {
        Match version = PackageVersionPattern().Match(value ?? string.Empty);
        if (!version.Success || version.Groups.Cast<Group>().Skip(1).Any(part => part.Success &&
            !int.TryParse(part.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new InvalidDataException("The Adapter package version is invalid.");
        }

        int Component(int index) => int.Parse(version.Groups[index].ValueSpan, CultureInfo.InvariantCulture);
        return (new Version(Component(1), Component(2), Component(3)), version.Groups[4].Success ? Component(4) : null);
    }

    internal static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate package metadata property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray()) RequireUniqueProperties(child);
        }
    }

    private static string RequireAsset(string path, string extension, IReadOnlySet<string> files)
    {
        if (!AdapterPackagePath.IsCanonical(path) || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || !files.Contains(path))
        {
            throw new InvalidDataException("A package resource must refer to an exact signed package file of the expected type.");
        }

        return path;
    }

    private static string ReadString(JsonElement element, string name) => ReadProperty(element, name, JsonValueKind.String).GetString()!;

    private static int ReadInteger(JsonElement element, string name) => ReadProperty(element, name, JsonValueKind.Number).TryGetInt32(out int value)
        ? value : throw new InvalidDataException($"Package metadata property '{name}' must be an integer.");

    private static JsonElement ReadProperty(JsonElement element, string name, JsonValueKind kind) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == kind
            ? value : throw new InvalidDataException($"Package metadata property '{name}' has the wrong shape or is missing.");

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"\A(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})\.(0|[1-9][0-9]{0,9})(?:-preview\.([1-9][0-9]{0,9}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex(@"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}

/// <summary>One canonical relative spelling for Windows package files and manifest resources.</summary>
public static class AdapterPackagePath
{
    public static bool IsCanonical(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.StartsWith('/') ||
            path.Contains('\\') || !path.IsNormalized(NormalizationForm.FormC)) return false;

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length is 0 or > 255 || segment is "." or ".." || segment != segment.Trim() ||
                segment.EndsWith('.') || segment.Any(character => char.IsControl(character) ||
                    character is ':' or '<' or '>' or '"' or '|' or '?' or '*' or '~')) return false;

            string stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" or "CLOCK$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                    stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'))
                return false;
        }

        return true;
    }
}
