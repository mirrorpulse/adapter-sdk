using System.Text;
using System.Text.Json;
using MirrorPulse.Adapter.Sdk;

internal static class PackageConfigurationContractChecks
{
    private static readonly string[] SignedFiles = ["assets/logo.png", "assets/help.jpg", "forms/config.xml", "locales/en-US.json", "locales/zh-CN.json"];
    private const string Manifest = """
        {"schemaVersion":2,"adapterId":"com.example.drive","version":"1.2.3-preview.4",
         "instancePolicy":{"maximumRootDefinitions":1},"rootDefinitions":[{"key":"drive"}],
         "presentation":{"titleKey":"title","descriptionKey":"description","logoPath":"assets/logo.png","formPath":"forms/config.xml"},
         "locales":["en-US","zh-CN"],"localeMetadata":{"en-US":{"resourcePath":"locales/en-US.json"},"zh-CN":{"resourcePath":"locales/zh-CN.json"}}}
        """;
    private const string Form = """
        <form version="1">
          <heading textKey="intro" />
          <paragraph textKey="instructions" />
          <image path="assets/help.jpg" altKey="diagram" />
          <text key="endpoint" labelKey="endpoint" required="true" />
          <password key="password" labelKey="password" required="false" />
          <singleChoice key="mode" labelKey="mode" required="true" default="read"><option id="read" labelKey="read" /></singleChoice>
          <multiChoice key="scopes" labelKey="scopes" required="false" default="read,write"><option id="read" labelKey="read" /><option id="write" labelKey="write" /></multiChoice>
          <checkBox key="enabled" labelKey="enabled" required="false" default="true" />
          <callback id="authorize" labelKey="authorize"><write key="password" /><write key="scopes" /></callback>
        </form>
        """;
    private const string Fallback = """
        {"title":"Sample drive","description":"Storage adapter","intro":"Setup","instructions":"Connect the drive",
         "diagram":"Connection diagram","endpoint":"Endpoint","password":"Password","mode":"Mode","read":"Read",
         "scopes":"Scopes","write":"Write","enabled":"Enabled","authorize":"Authorize"}
        """;

    public static void Run()
    {
        var expected = new AdapterPackageIdentity("com.example.drive", "1.2.3-preview.4");
        AdapterPackageMetadata metadata = ReadManifest(Manifest, expected);
        if (metadata.Identity != expected || metadata.RootKey != "drive" || metadata.FormPath != "forms/config.xml" ||
            metadata.LocaleFiles["zh-CN"] != "locales/zh-CN.json") throw new InvalidOperationException("SDK package binding failed.");

        Reject(() => ReadManifest(Manifest, expected with { Version = "1.2.3" }), "wrong package identity");
        Reject(() => ReadManifest(Manifest.Replace("[{\"key\":\"drive\"}]", "[]", StringComparison.Ordinal)), "zero roots");
        Reject(() => ReadManifest(Manifest.Replace("[{\"key\":\"drive\"}]", "[{\"key\":\"drive\"},{\"key\":\"other\"}]", StringComparison.Ordinal)), "multiple roots");
        Reject(() => ReadManifest(Manifest.Replace("assets/logo.png", "Assets/logo.png", StringComparison.Ordinal)), "case alias of signed asset");
        Reject(() => ReadManifest(Manifest.Replace("forms/config.xml", "forms/../config.xml", StringComparison.Ordinal)), "unsafe form path");
        Reject(() => ReadManifest(Manifest.Replace("locales/en-US.json", "https://example.invalid/en-US.json", StringComparison.Ordinal)), "external translations");
        Reject(() => ReadManifest(Manifest.Replace("\"schemaVersion\":2", "\"schemaVersion\":1,\"SchemaVersion\":2", StringComparison.Ordinal)), "duplicate JSON property");

        AdapterConfigurationForm form = ReadForm(Form);
        if (form.Controls.Count != 9 || form.Controls[^1] is not AdapterFormCallback callback ||
            !callback.WritableFields.SequenceEqual(["password", "scopes"])) throw new InvalidOperationException("Form callback field binding failed.");
        Reject(() => ReadForm("<!DOCTYPE form>" + Form), "XML DTD");
        Reject(() => ReadForm(Form.Replace("key=\"enabled\"", "key=\"Endpoint\"", StringComparison.Ordinal)), "duplicate field key");
        Reject(() => ReadForm(Form.Replace("<write key=\"scopes\"", "<write key=\"notDeclared\"", StringComparison.Ordinal)), "callback write outside form");
        Reject(() => ReadForm(Form.Replace("required=\"false\" />", "required=\"false\" default=\"value\" />", StringComparison.Ordinal)), "password default");
        Reject(() => ReadForm(Form.Replace("default=\"read,write\"", "default=\"read,read\"", StringComparison.Ordinal)), "repeated multi-choice default");
        Reject(() => ReadForm(Form.Replace("assets/help.jpg", "assets/other.jpg", StringComparison.Ordinal)), "unsigned form image");
        Reject(() => ReadForm("<form version=\"1\">" + new string(' ', 64 * 1024) + "</form>"), "excessive form size");

        using var fallback = FromText(Fallback);
        using var preferred = FromText("{\"title\":\"示例存储\"}");
        AdapterConfigurationTranslations strings = AdapterConfigurationTranslations.Read(metadata, form, fallback, preferred);
        if (strings.Resolve("title") != "示例存储" || strings.Resolve("authorize") != "Authorize")
            throw new InvalidOperationException("Localized selection and English fallback failed.");
        Reject(() =>
        {
            using var incomplete = FromText(Fallback.Replace(",\"authorize\":\"Authorize\"", string.Empty, StringComparison.Ordinal));
            AdapterConfigurationTranslations.Read(metadata, form, incomplete);
        }, "missing fallback label");
        Console.WriteLine("SDK 1.0 package identity, single-root, signed assets, form bindings and localized fallback checks passed.");
    }

    private static AdapterPackageMetadata ReadManifest(string text, AdapterPackageIdentity? identity = null)
    {
        using JsonDocument manifest = JsonDocument.Parse(text);
        return AdapterPackageMetadata.Read(manifest.RootElement, SignedFiles, identity);
    }

    private static AdapterConfigurationForm ReadForm(string text)
    {
        using var stream = FromText(text);
        return AdapterConfigurationForm.Read(stream, SignedFiles);
    }

    private static MemoryStream FromText(string text) => new(Encoding.UTF8.GetBytes(text));

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"The package contract accepted {reason}.");
    }
}
