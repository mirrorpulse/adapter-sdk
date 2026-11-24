# Adapter SDK documentation

A MirrorPulse deployment connects one configured storage root to a
Worker. The same Adapter package can serve several independent deployments.
MirrorPulse starts a separate process and supplies settings and credential
references for each deployment.

## Build your first Adapter

1. [Getting started](getting-started.md) walks through the template, the package
   files to edit, and the first deployment.
2. [Package configuration](package-configuration.md) covers the signed manifest,
   the localized XML form, saved settings and credentials, authorization
   callbacks, health reports and version offers.
3. [Implement a Worker](implementing-a-worker.md) covers the process handshake,
   addresses, listing and ranged reads, uploads, mutation replay and cancellation.
4. [Test and publish an Adapter](test-and-publish.md) shows what the SDK runner
   verifies and what to check against an actual backend and MirrorPulse Host.

## Protocol and API

Use the [wire specification](https://github.com/mirrorpulse/adapter-sdk/blob/sdk-v1.0.0/spec/worker-v2.md)
for envelope and binary field definitions, including the versioned examples
beside it. Use the [API reference](https://mirrorpulse.github.io/adapter/api/MirrorPulse.Adapter.Sdk.html)
for the .NET types that implement those contracts.

The following optional protocols require backend-specific evidence and
durability. Implement ordinary file operations before assessing them.

| Guide | Purpose |
| --- | --- |
| [Original directory admission](original-directory-admission-wire.md) | Preserve the original subtree identity and the ordered, conditional plan before committing any child effect. |
| [Original directory retirement](original-directory-retirement-wire.md) | Verify the accepted child history and retire the original directory using its retained evidence. |
| [Atomic original directory deletion](atomic-original-directory-deletion.md) | Commit and recover one qualified deletion of the whole admitted original subtree. |

[SDK trusted publishing](nuget-publishing.md) is the maintainer guide for
publishing the SDK and its matching wire specification and conformance tools.
