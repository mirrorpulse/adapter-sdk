# SDK trusted publishing

Use this procedure when publishing the SDK itself. Adapter authors publish
their Workers through the [Adapter package workflow](test-and-publish.md).
A single SDK release binds NuGet, the wire specification and the native
conformance tools to the same source commit.

## Configure GitHub and NuGet

Configure trusted publishing on nuget.org for the `MirrorPulse.Adapter.Sdk` package. Match the names in this table to the GitHub release workflow.

| Field | Stable | Preview |
| --- | --- | --- |
| Repository owner | `MirrorPulse` | `MirrorPulse` |
| Repository | `adapter-sdk` | `adapter-sdk` |
| Workflow file | `sdk-release.yml` | `sdk-release.yml` |
| Environment | `stable` | `preview` |
| Package pattern | `MirrorPulse.Adapter.Sdk` | `MirrorPulse.Adapter.Sdk` |

Restrict the `stable` environment to `main` and the `preview` environment to `develop`. Require a stable reviewer and disable self-review. Set the `NUGET_USER` secret to the owning NuGet profile name. The workflow exchanges GitHub OIDC for a temporary NuGet credential after the candidate passes conformance on both Windows architectures and the environment approves publication. [Configure trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

## Qualify and publish one candidate

Set the SDK version in `src/MirrorPulse.Adapter.Sdk/MirrorPulse.Adapter.Sdk.csproj`. Dispatch **SDK Release** from `develop` for a version with a `preview.N` suffix, or from protected `main` for a stable version. The default dispatch builds the package, specification and both native runners, then runs conformance against those exact artifacts. To publish, enable `publish` and enter `PUBLISH` in the confirmation field.

NuGet and GitHub receive the same candidate from the workflow artifact. GitHub retains the original length, SHA256 and source commit in `sdk-release.json`. If one destination accepts the version and the other fails, recover using the original artifact and source evidence before changing the version or rebuilding it.

Check the published SDK version in the NuGet package listing and the GitHub
tag. Verify the package and companion archives against the release inventory
before updating consumer lock files. The GitHub release contains the exact
package used for the fixed-hash feed, the protocol specification, and one
standalone runner for each supported architecture.

## Diagnose a failed release

| Where it stopped | What to inspect |
| --- | --- |
| Version or source resolution | Check the project version, selected branch and channel. The version cannot replace an existing published version. |
| Native conformance | Read the failed architecture's evidence. Fix source and qualify a new candidate before publishing. |
| Environment or OIDC | Check the environment review and its branch restrictions. Match the workflow filename, repository identity and NuGet owner against the configured policy. |
| One destination published | Hold the candidate artifact and its hashes. Check which destination already accepted those bytes, then use those exact bytes to finish recovery. |

The public documentation has its own workflow. Documentation-only fixes can
deploy to the site through `main` without creating a new SDK package version.
