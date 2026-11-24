# How to build an Adapter

An Adapter connects a storage backend to MirrorPulse. It ships as a signed
`.mpadapter` containing a Windows Worker, its private runtime, and a description
of the settings MirrorPulse should collect. The **Adapter SDK** provides the
public .NET contract and conformance tools. The **Adapter template** is the
starting repository for a new implementation.

| Start here | Use it to |
| --- | --- |
| [Adapter template](https://github.com/mirrorpulse/adapter-template) | Build a runnable example, then replace the sample storage implementation. |
| [Getting started](docs/getting-started.md) | Follow the template from first build to installation and a working deployment. |
| [Package configuration](docs/package-configuration.md) | Define a package, localize its form, bind credentials, and add optional authorization, health or update support. |
| [Implement a Worker](docs/implementing-a-worker.md) | Implement the pipe lifecycle, file operations, consistency checks and recovery. |
| [Test and publish an Adapter](docs/test-and-publish.md) | Qualify the Worker and the exact signed package on x64 and ARM64. |
| [API reference](https://mirrorpulse.github.io/adapter/api/MirrorPulse.Adapter.Sdk.html) | Find the generated reference for the public SDK types. |

## Build your first package

1. Create a repository from the [Adapter template](https://github.com/mirrorpulse/adapter-template).
   Install PowerShell 7 and the .NET SDK selected by the template's `global.json`.
2. Run `Build-Adapter.cmd` on Windows or `./build-adapter.sh` on macOS or Linux.
   The launcher builds the x64 and ARM64 Windows payloads and signs a development
   package using a disposable test key. The command prints the package path
   under `artifacts/release`.
3. Edit `eng/release-manifest.json` for package identity and the root definition.
   Edit `template/package/forms/config.xml`, `template/package/locales/en-US.json`
   and `template/package/assets/logo.png` for settings and presentation.
4. Replace the sample Worker in `samples/MirrorPulse.Adapter.SampleWorker`.
   Start with a real listing and bounded read, then add write operations only
   where your backend can honor the requested preconditions.
5. Test the signed package with an appropriate trust configuration in a
   disposable MirrorPulse environment. Create a deployment, save its
   configuration, and verify the Worker reads a file from its configured source.

An SDK 1.0 package declares one root. A package can have several deployments,
each with its own settings, credential references and Worker session. The Host
owns the Windows sync root, configuration persistence and credentials. The
Worker translates between the selected protocol and its storage backend.

## Work on the SDK itself

The public NuGet package is
[`MirrorPulse.Adapter.Sdk` 1.0.0](https://www.nuget.org/packages/MirrorPulse.Adapter.Sdk/1.0.0).
SDK releases also publish the [versioned specification and native conformance
tools](https://github.com/mirrorpulse/adapter-sdk/releases/tag/sdk-v1.0.0).
Use the [Worker protocol](spec/worker-v2.md) for wire field definitions or
when implementing a Worker in another language.

From this repository, use the .NET SDK selected in [`global.json`](global.json).

```powershell
dotnet restore MirrorPulse.Adapter.sln --locked-mode
dotnet build MirrorPulse.Adapter.sln -c Release --no-restore
dotnet run --project tests/MirrorPulse.Adapter.ContractTests -c Release --no-build
```

The contract executable validates the SDK and launches the sample Worker.
[SDK trusted publishing](docs/nuget-publishing.md) describes how SDK maintainers
verify and publish the NuGet package and its companion assets.
