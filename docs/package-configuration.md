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

## Authorization, health and updates

### Configuration-only Worker startup

MirrorPulse can start the signed Worker before any deployment exists and can use the same mode to reauthorize an existing deployment. Parse the usual `AdapterWorkerProcessArguments` and inspect `ConfigurationOnly`. In that mode, advertise `AdapterConfigurationCallbackContract.Capability` with protocol 2, then validate the Host's `Ready` with that launch mode. The accepted `Ready` has zero root bindings and includes the settings available for this authorization. Keep the normal sync handshake and startup path when `ConfigurationOnly` is false.

```csharp
var arguments = AdapterWorkerProcessArguments.Parse(args);
var offer = new AdapterHello(new(2, 2), arguments.ConfigurationOnly
    ? [AdapterConfigurationCallbackContract.Capability]
    : AdapterHandshake.V2Capabilities);

// Send Hello and decode its matching Ready response as AdapterReady.
AdapterHandshake.ValidateReady(ready, offer, arguments.ConfigurationOnly);
channel.SelectProtocol(ready.SelectedVersion);
```

In configuration-only mode, handle `ConfigurationCallbackStart`, `ConfigurationCallbackPoll`, `ConfigurationCallbackCancel`, and the Host's `Stop`. Read callback messages with `AdapterConfigurationCallbackContract.ReadRequest` after reading the frame from `AdapterControlChannel`. Track the invocation ID, callback ID and expiry in memory for this session. Send `ConfigurationCallbackResult` as a response with the matching frame request ID. A pending response can include an HTTPS authorization page. A Worker that owns its own interaction window can return pending without a URL. A successful response proposes values only for fields listed in the callback's signed `write` list. Cancel ends the interaction and discards proposed writes.

`AdapterReady.CredentialBindings` indexes opaque Host credential references by password field key. For an existing deployment, a Worker can use a supplied reference with a `CredentialRequest` and read its matching `CredentialResponse` on the authenticated channel. Password values stay out of ordinary configuration and logs. Use the temporary cache directories supplied by the Host and keep authorization state in memory. MirrorPulse accepts authorized writes when the user saves the form, stores passwords in its credential store and ends the temporary Worker session. Send file operations, directory state and change notifications from the normal sync session.

### Health and updates

Offer `AdapterDeploymentContract.StructuredHealthCapability` when the Worker can identify configuration and credential issues. If the ordinary deployment's `Ready` selects that capability, send the complete `AdapterHealthReport` as a protocol 2 notification with `AdapterDeploymentContract.HealthReportMessage`, a fresh frame request ID, and `isResponse: false`. It can arrive before `Connected` when a credential blocks startup. Replace the report as conditions change, and send an empty issue list after recovery. Include the exact signed form field key when known. Credential issues can name password fields. Source and Worker failures carry no field. Keep the report within 16 unique issues and keep arbitrary diagnostic text and credential values out of it.

Update checks are optional. A Worker can include `update-discovery` in its protocol 2 `Hello` and check that `Ready` selects it. After `Connected`, the Host can send `CheckAdapterUpdate` with an empty object. Reply with `AdapterUpdateOffer` using the same request, instance and session identifiers. This exchange applies to the selected deployment and carries no `rootKey`.

Serialize `AdapterPackageUpdateResult` as the response payload. Set `Offer` to `null` when the selected package is current. Otherwise, offer the same Adapter ID, a strictly newer version, and an HTTPS URL or the absolute path of a local Windows `.mpadapter` file, represented as a `file:` URI. HTTPS packages carry an embedded signature. The Host asks the user before acquiring the package and checks its signature and identity before registering the version. Users can then select that version for each deployment.
