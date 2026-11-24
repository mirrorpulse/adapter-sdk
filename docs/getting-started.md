# Getting started

Build the template first so you have a known working package. Then replace its
in-memory backend one operation at a time and try the resulting Worker in
MirrorPulse.

## Prerequisites

- PowerShell 7 and the .NET SDK selected in the template's `global.json`.
- A Windows x64 or ARM64 machine to run a matching Worker. The shell launcher
  on macOS or Linux can cross-build both Windows payloads.
- A disposable MirrorPulse setup and a storage fixture you control for Host
  and provider tests.

## 1. Build the template

Create a repository from the [Adapter template](https://github.com/mirrorpulse/adapter-template)
and clone your new repository. Run `Build-Adapter.cmd` on Windows or
`./build-adapter.sh` on Linux or macOS. From PowerShell, you can specify a
development version.

```powershell
pwsh ./Build-Adapter.ps1 -Version 1.0.0-preview.1
```

The launcher prints the path of the `.mpadapter` below `artifacts/release`.
It builds the same package for both Windows architectures and uses a
disposable test key. Only a Host configured to trust that signer can install it.
The signed package and its companion signature should stay together.

## 2. Give the package its own identity and settings

| File in the template | What to change |
| --- | --- |
| `eng/release-manifest.json` | Set the canonical Adapter ID, publisher, root key, and protocol and package metadata. Keep the Adapter ID and root key stable after publication. |
| `template/package/forms/config.xml` | Declare the per-deployment connection settings the Host collects and mark them required or optional. |
| `template/package/locales/en-US.json` | Define the English title, description, field labels, option labels and image text. |
| `template/package/assets/logo.png` | Supply your own local logo and update the manifest path if it changes. |
| `samples/MirrorPulse.Adapter.SampleWorker/` | Replace the sample’s in-memory storage implementation. Retain the SDK-based process and frame handling. |

Start with a plain endpoint field and a password field if the backend requires
credentials. Add localized help explaining the expected endpoint format. The
Host presents the form and stores its values, including credentials. Read
normal settings and opaque credential references when the Host starts the Worker.
See [Package configuration](package-configuration.md) for complete examples.

## 3. Add the first storage operations

Use a small fixture you control. Implement listing and stat first. Follow with
a bounded range read so MirrorPulse can read the content of a listed file. Return
the selected root key on every operation, keep directory cursors scoped to
their directory, and surface a revision mismatch as a conflict. With writes
enabled, exercise retries using the original operation ID and verify what
happens after the connection or Worker stops. Follow
[Implement a Worker](implementing-a-worker.md) for the message sequence.

## 4. Try a signed package in MirrorPulse

1. Build the package again and install it using **Adapter management**, or run
   `MirrorPulse adapter install C:/path/to/your-adapter.mpadapter` from an edition
   that trusts your test signer.
2. Create a deployment for the installed version. Enter the required form
   values, save them, then enable the deployment if it is disabled.
3. Verify that the Host starts the Worker and the root shows only the
   configured fixture. Read a small file through MirrorPulse and compare the
   bytes with the source.
4. If the Adapter supports writes, verify one change through MirrorPulse and
   check the backend. Stop and reopen the Host. The deployment should keep
   its configuration, and reconnecting should preserve its scope.

Verify the actual published package separately from source or test-key
packages. See [Test and publish an Adapter](test-and-publish.md).

## Run the SDK sample independently

If you cloned the SDK repository, run the following from its root. Its
in-memory Worker and contract executable demonstrate the process protocol.

```powershell
dotnet restore MirrorPulse.Adapter.sln --locked-mode
dotnet build MirrorPulse.Adapter.sln -c Release --no-restore
dotnet run --project tests/MirrorPulse.Adapter.ContractTests -c Release --no-build
```
