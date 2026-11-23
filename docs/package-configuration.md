# Package configuration contract

SDK 1.0 packages use manifest schema `2`. Keep one `rootDefinitions` item and set `instancePolicy.maximumRootDefinitions` to `1`. One Adapter package can still be installed in multiple deployments. Use a canonical lowercase Adapter ID and a three-part stable or `-preview.N` package version.

Use `presentation` for the package title, description, logo, and form. These are separate from the stable Adapter ID.

```json
{
  "presentation": {
    "titleKey": "package.title",
    "descriptionKey": "package.description",
    "logoPath": "assets/logo.png",
    "formPath": "forms/config.xml"
  },
  "locales": ["en-US"],
  "localeMetadata": {
    "en-US": {
      "displayName": "English (United States)",
      "resourcePath": "locales/en-US.json"
    }
  }
}
```

Include each referenced file in the signed package inventory, with the exact same spelling and case. Resource paths use `/` and are relative to the package root. Images use PNG, JPEG, ICO or BMP. Locale files are flat JSON objects that map label keys to display text. The `en-US` table covers every label used in the form and presentation. A selected locale can omit keys to use the corresponding English text.

The separate form is declarative XML. Its supported layout items are heading, paragraph and image. Every image requires a localized accessibility label. Field keys remain stable across releases. The form also supports the following editable controls and callback buttons.

```xml
<form version="1">
  <heading textKey="setup.title" />
  <paragraph textKey="setup.instructions" />
  <image path="assets/logo.png" altKey="package.title" />
  <text key="endpoint" labelKey="field.endpoint" required="true" />
  <password key="accessToken" labelKey="field.accessToken" required="false" />
  <singleChoice key="mode" labelKey="field.mode" required="true" default="read">
    <option id="read" labelKey="mode.read" />
    <option id="write" labelKey="mode.write" />
  </singleChoice>
  <multiChoice key="scopes" labelKey="field.scopes" required="false" default="read,write">
    <option id="read" labelKey="mode.read" />
    <option id="write" labelKey="mode.write" />
  </multiChoice>
  <checkBox key="enabled" labelKey="field.enabled" required="false" default="true" />
  <callback id="authorize" labelKey="action.authorize">
    <write key="accessToken" />
    <write key="scopes" />
  </callback>
</form>
```

Use stable field keys, option IDs, and callback IDs independent of display language. The form requires a `true` or `false` value for each field's `required` attribute. Defaults are optional. Password fields cannot declare defaults. A single choice default names one option ID. Multiple choice defaults use comma-separated IDs and an empty default selects nothing. Check box defaults are `true` or `false`.

`AdapterPackageMetadata.Read` validates the versioned package metadata against the signed file inventory. `AdapterConfigurationForm.Read` validates the layout, field bindings and callback write declarations. `AdapterConfigurationTranslations.Read` verifies the fallback string table and resolves display labels. These methods operate on parsed metadata and package data supplied by the caller. The Host validates the signature and continues to own configuration, authorization transactions, validation and persistence. A callback's `write` list is the maximum set of fields the Host may accept from that callback.
