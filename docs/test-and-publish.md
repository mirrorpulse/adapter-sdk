# Test and publish an Adapter

Use the SDK contracts for shared wire behavior and a disposable backend to
prove the actual provider behavior. Build the release once and keep the exact
signed payload through both native qualification runs and publication.

## Run the shared and provider tests

Run `pwsh ./eng/verify.ps1` in a project created from the Adapter template.
For SDK development, use the [contract executable](getting-started.md#run-the-sdk-sample-independently).
The SDK release provides a standalone runner for Windows x64 and ARM64.
Download the matching runner and specification from the fixed SDK release
and verify their lengths and SHA-256 values in `sdk-release.json`.

The runner's single-root profile uses a controlled memory-source fixture. For
a compatible test Worker with a root key of `sample`, use the following
from PowerShell on a Windows runner of the matching architecture.

```powershell
$worker = 'C:/fixture/worker/Worker.exe'
$cache = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cache | Out-Null
./MirrorPulse.Adapter.Conformance.exe --worker $worker --transfer-cache $cache --single-root sample
```

The Worker must supply the expected fixture contents, including
`readme.txt` with UTF-8 content `Memory source: sample` followed by a newline.
The default runner mode checks the separate legacy multiple-root fixture;
independent SDK 1.0 packages should use the single-root profile where
applicable. A provider with different fixture semantics should supply an
equivalent process-level suite.

## Qualify a real deployment

| Verify | Observe |
| --- | --- |
| Startup and isolation | The exact package is accepted by a Host that trusts its signer. The correct native executable and private runtime start. Two deployments of the same package use separate configuration, credentials and state. |
| Read path | Nested directories and multiple pages enumerate correctly. Range bytes match the fixture. A stale revision, wrong root and invalid cursor are refused. |
| Writes, if supported | Preconditions are checked; disallowed moves fail; interrupted uploads release the cache; lost replies and restart preserve operation identity and recover a receipt or an explicit unknown outcome. |
| Failure and recovery | Restart or disconnect during an operation, reconnect with the same deployment, and verify that changes apply only to the intended source. |
| Configuration | Required fields are enforced. Passwords and authorization results survive a Host restart via the Host credential store. Optional health and update features report their actual state. |

Use disposable endpoints and clean directories. Record the exact source commit,
candidate package hash, architecture and observed results. A wire conformance
pass alone only qualifies the tested wire profile.

## Package and sign a release

A release package declares one root and includes both `win-x64` and
`win-arm64` entrypoints, local metadata assets, its Worker dependencies, and
required notices. Keep package ID, root key, persistent field keys and option
IDs stable when you publish a compatible update.

Use the template’s protected publication workflow for your repository. Use
the development launcher for test-signed packages. Production validation needs
the intended production signer and an installation with the production trust
policy. Retain the package bytes and companion signature together. Keep
installed versions immutable and publish changed bytes under a new version.

If the Worker implements [update discovery](package-configuration.md#health-and-updates),
return only a newer version of the same Adapter. MirrorPulse verifies the
download and lets a user choose when to move an existing deployment to it.

Publishing the SDK itself is documented under [SDK trusted publishing](nuget-publishing.md).
