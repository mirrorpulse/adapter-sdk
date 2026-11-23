using System.Text.Json;

namespace MirrorPulse.Adapter.Sdk;

/// <summary>Localized package labels with a required English fallback.</summary>
public sealed class AdapterConfigurationTranslations
{
    private const int MaximumBytes = 128 * 1024;
    private readonly Dictionary<string, string> _fallback;
    private readonly Dictionary<string, string> _preferred;

    private AdapterConfigurationTranslations(Dictionary<string, string> fallback, Dictionary<string, string> preferred)
    {
        _fallback = fallback;
        _preferred = preferred;
    }

    /// <summary>Reads the signed en-US string table and an optional selected locale.</summary>
    public static AdapterConfigurationTranslations Read(AdapterPackageMetadata metadata, AdapterConfigurationForm form,
        Stream fallback, Stream? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(fallback);
        Dictionary<string, string> strings = ReadStrings(fallback);
        var keys = new HashSet<string>(StringComparer.Ordinal) { metadata.TitleKey, metadata.DescriptionKey };
        foreach (AdapterFormControl control in form.Controls)
        {
            switch (control)
            {
                case AdapterFormContent text:
                    keys.Add(text.TextKey);
                    break;
                case AdapterFormImage image:
                    keys.Add(image.AltKey);
                    break;
                case AdapterFormField field:
                    keys.Add(field.LabelKey);
                    keys.UnionWith(field.Options.Select(option => option.LabelKey));
                    break;
                case AdapterFormCallback callback:
                    keys.Add(callback.LabelKey);
                    break;
            }
        }

        if (!keys.All(strings.ContainsKey)) throw new InvalidDataException("The en-US table must cover all package and form labels.");
        return new(strings, preferred is null ? new(StringComparer.Ordinal) : ReadStrings(preferred));
    }

    public string Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _preferred.TryGetValue(key, out string? value) ? value : _fallback[key];
    }

    private static Dictionary<string, string> ReadStrings(Stream stream)
    {
        byte[] buffer = new byte[MaximumBytes + 1];
        int length = stream.ReadAtLeast(buffer, buffer.Length, false);
        if (length > MaximumBytes) throw new InvalidDataException("The package string table exceeds the size limit.");

        try
        {
            using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 2 });
            AdapterPackageMetadata.RequireUniqueProperties(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The package string table must be a JSON object.");

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty item in document.RootElement.EnumerateObject())
            {
                string? value = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
                {
                    throw new InvalidDataException("Each package label must be nonempty bounded text.");
                }

                result.Add(AdapterConfigurationForm.RequireIdentifier(item.Name), value);
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The package string table must contain valid JSON.", exception);
        }
    }
}
