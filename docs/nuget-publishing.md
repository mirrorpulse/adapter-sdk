# SDK trusted publishing

Configure trusted publishing on nuget.org for the `MirrorPulse.Adapter.Sdk` package. Match the names in this table to the GitHub release workflow.

| Field | Stable | Preview |
| --- | --- | --- |
| Repository owner | `MirrorPulse` | `MirrorPulse` |
| Repository | `adapter-sdk` | `adapter-sdk` |
| Workflow file | `sdk-release.yml` | `sdk-release.yml` |
| Environment | `stable` | `preview` |
| Package pattern | `MirrorPulse.Adapter.Sdk` | `MirrorPulse.Adapter.Sdk` |

Restrict the `stable` environment to `main` and the `preview` environment to `develop`. Require a stable reviewer and disable self-review. Set the `NUGET_USER` secret to the owning NuGet profile name. The workflow exchanges GitHub OIDC for a temporary NuGet credential after the candidate passes conformance on both Windows architectures and the environment approves publication. [Configure trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)

Set the SDK version in `src/MirrorPulse.Adapter.Sdk/MirrorPulse.Adapter.Sdk.csproj`. Dispatch **SDK Release** from `develop` for a version with a `preview.N` suffix, or from protected `main` for a stable version. The default dispatch builds the package, specification and both native runners, then runs conformance against those exact artifacts. To publish, enable `publish` and enter `PUBLISH` in the confirmation field.

NuGet and GitHub receive the same candidate from the workflow artifact. GitHub retains the original length, SHA256 and source commit in `sdk-release.json`. If one destination accepts the version and the other fails, recover using the original artifact and source evidence before changing the version or rebuilding it.
