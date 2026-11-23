using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MirrorPulse.Adapter.Sdk;

public enum AdapterFormContentKind { Heading, Paragraph }
public enum AdapterFormFieldKind { Text, Password, SingleChoice, MultiChoice, CheckBox }

public abstract record AdapterFormControl;

public sealed record AdapterFormContent(AdapterFormContentKind Kind, string TextKey) : AdapterFormControl;
public sealed record AdapterFormImage(string Path, string AltKey) : AdapterFormControl;
public sealed record AdapterFormOption(string Id, string LabelKey);
public sealed record AdapterFormField(string Key, string LabelKey, AdapterFormFieldKind Kind,
    bool Required, string? DefaultValue, IReadOnlyList<AdapterFormOption> Options) : AdapterFormControl;
public sealed record AdapterFormCallback(string Id, string LabelKey, IReadOnlyList<string> WritableFields) : AdapterFormControl;

/// <summary>Declarative layout and field bindings for configuration owned by the Host.</summary>
public sealed partial class AdapterConfigurationForm
{
    public const int SchemaVersion = 1;
    private const int MaximumCharacters = 64 * 1024;
    private const int MaximumItems = 64;

    private AdapterConfigurationForm(List<AdapterFormControl> controls) => Controls = controls.AsReadOnly();

    public IReadOnlyList<AdapterFormControl> Controls { get; }

    /// <summary>Reads a package-local form. The input stream remains open.</summary>
    public static AdapterConfigurationForm Read(Stream form, IReadOnlyCollection<string> signedFiles)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(signedFiles);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaximumCharacters,
        };
        XElement root;
        try
        {
            using XmlReader reader = XmlReader.Create(form, settings);
            root = XDocument.Load(reader).Root ?? throw new InvalidDataException("The configuration form is empty.");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The configuration form must be bounded XML without a DTD.", exception);
        }

        RequireShape(root, allowChildren: true, "version");
        if (root.Name != "form" || Read(root, "version") != "1" || root.Elements().Take(MaximumItems + 1).Count() > MaximumItems)
        {
            throw new InvalidDataException("The configuration form has an unsupported version or too many controls.");
        }

        var files = signedFiles.ToHashSet(StringComparer.Ordinal);
        var controls = new List<AdapterFormControl>();
        var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callbackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement element in root.Elements())
        {
            AdapterFormControl control = ReadControl(element, files);
            if ((control is AdapterFormField field && !fieldKeys.Add(field.Key)) ||
                (control is AdapterFormCallback callback && !callbackIds.Add(callback.Id)))
            {
                throw new InvalidDataException("The configuration form repeats a field key or callback ID.");
            }

            controls.Add(control);
        }

        var exactFields = controls.OfType<AdapterFormField>().Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        if (controls.OfType<AdapterFormCallback>().Any(callback => !callback.WritableFields.All(exactFields.Contains)))
        {
            throw new InvalidDataException("A callback must write only declared configuration fields with exact keys.");
        }

        return new(controls);
    }

    internal static string RequireIdentifier(string value) => value.Length <= 64 && IdentifierPattern().IsMatch(value)
        ? value : throw new InvalidDataException("Form identifiers must use letters, digits, dots, underscores or hyphens and start with a letter.");

    private static AdapterFormControl ReadControl(XElement element, IReadOnlySet<string> files)
    {
        return element.Name.LocalName switch
        {
            "heading" => ReadContent(element, AdapterFormContentKind.Heading),
            "paragraph" => ReadContent(element, AdapterFormContentKind.Paragraph),
            "image" => ReadImage(element, files),
            "text" => ReadField(element, AdapterFormFieldKind.Text),
            "password" => ReadField(element, AdapterFormFieldKind.Password),
            "singleChoice" => ReadField(element, AdapterFormFieldKind.SingleChoice),
            "multiChoice" => ReadField(element, AdapterFormFieldKind.MultiChoice),
            "checkBox" => ReadField(element, AdapterFormFieldKind.CheckBox),
            "callback" => ReadCallback(element),
            _ => throw new InvalidDataException("The configuration form declares an unsupported control."),
        };
    }

    private static AdapterFormContent ReadContent(XElement element, AdapterFormContentKind kind)
    {
        RequireShape(element, allowChildren: false, "textKey");
        return new(kind, ReadIdentifier(element, "textKey"));
    }

    private static AdapterFormImage ReadImage(XElement element, IReadOnlySet<string> files)
    {
        RequireShape(element, allowChildren: false, "path", "altKey");
        return new(AdapterPackageMetadata.RequireImagePath(Read(element, "path"), files), ReadIdentifier(element, "altKey"));
    }

    private static AdapterFormField ReadField(XElement element, AdapterFormFieldKind kind)
    {
        bool hasChoices = kind is AdapterFormFieldKind.SingleChoice or AdapterFormFieldKind.MultiChoice;
        RequireShape(element, hasChoices, "key", "labelKey", "required", "default");
        string key = ReadFieldKey(element, "key");
        string labelKey = ReadIdentifier(element, "labelKey");
        bool required = Read(element, "required") switch
        {
            "true" => true,
            "false" => false,
            _ => throw new InvalidDataException("The required attribute must be true or false."),
        };
        string? defaultValue = (string?)element.Attribute("default");
        var options = new List<AdapterFormOption>();
        if (hasChoices)
        {
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement child in element.Elements())
            {
                RequireShape(child, allowChildren: false, "id", "labelKey");
                string id = ReadIdentifier(child, "id");
                if (child.Name != "option" || !optionIds.Add(id) || options.Count >= MaximumItems)
                {
                    throw new InvalidDataException("The form choice has too many, repeated or unsupported options.");
                }

                options.Add(new(id, ReadIdentifier(child, "labelKey")));
            }

            if (options.Count == 0 || (defaultValue is not null && !IsChoiceValue(kind, defaultValue, optionIds)))
            {
                throw new InvalidDataException("The form choice requires options and a default selected from them.");
            }
        }
        else if (defaultValue is not null &&
            (kind == AdapterFormFieldKind.Password || defaultValue.Length > 1024 ||
                (kind == AdapterFormFieldKind.CheckBox && defaultValue is not ("true" or "false"))))
        {
            throw new InvalidDataException("The form field has a default value that does not match its type.");
        }

        return new(key, labelKey, kind, required, defaultValue, options.AsReadOnly());
    }

    private static bool IsChoiceValue(AdapterFormFieldKind kind, string value, HashSet<string> optionIds)
    {
        if (kind == AdapterFormFieldKind.SingleChoice) return optionIds.Contains(value);
        if (value.Length == 0) return true;
        string[] choices = value.Split(',');
        return choices.Length == choices.Distinct(StringComparer.Ordinal).Count() && choices.All(optionIds.Contains);
    }

    private static AdapterFormCallback ReadCallback(XElement element)
    {
        RequireShape(element, allowChildren: true, "id", "labelKey");
        var fields = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement child in element.Elements())
        {
            RequireShape(child, allowChildren: false, "key");
            string key = ReadFieldKey(child, "key");
            if (child.Name != "write" || !unique.Add(key) || fields.Count >= MaximumItems)
            {
                throw new InvalidDataException("The callback has repeated or unsupported writable fields.");
            }

            fields.Add(key);
        }

        if (fields.Count == 0) throw new InvalidDataException("The callback requires at least one declared writable field.");
        return new(ReadIdentifier(element, "id"), ReadIdentifier(element, "labelKey"), fields.AsReadOnly());
    }

    private static void RequireShape(XElement element, bool allowChildren, params string[] attributes)
    {
        if (element.Name.Namespace != XNamespace.None ||
            element.Attributes().Any(attribute => attribute.Name.Namespace != XNamespace.None ||
                !attributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal)) ||
            (!allowChildren && element.HasElements) || element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            throw new InvalidDataException("A form element has unexpected attributes, text or child elements.");
        }
    }

    private static string ReadFieldKey(XElement element, string name)
    {
        string key = ReadIdentifier(element, name);
        if (string.Equals(key, "credentialReference", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The configuration field key is reserved by the Host.");
        }

        return key;
    }

    private static string ReadIdentifier(XElement element, string name) => RequireIdentifier(Read(element, name));

    private static string Read(XElement element, string name) => (string?)element.Attribute(name)
        ?? throw new InvalidDataException($"The configuration form requires the '{name}' attribute.");

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
