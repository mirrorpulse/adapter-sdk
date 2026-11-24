# Implement a Worker

The Worker runs locally as a separate process. It keeps backend-specific
connection logic behind the SDK types. MirrorPulse owns the deployment
lifetime, settings, credential store and the Windows Cloud Files session.
Use the [Worker protocol](https://github.com/mirrorpulse/adapter-sdk/blob/sdk-v1.0.0/spec/worker-v2.md)
for the precise field, frame size and binary layout rules.

## Start and stop a session

| Step | Worker responsibility |
| --- | --- |
| Launch | Parse `AdapterWorkerProcessArguments`, connect with `AdapterNamedPipeClient`, and create an `AdapterControlChannel` scoped to the instance and session. |
| Hello | Send a bootstrap Hello with the supported versions and offered capabilities. Keep its request ID for the matching response. |
| Ready | Check the matching response and run `AdapterHandshake.ValidateReady`. Call `SelectProtocol` only after validation. Use only the selected capabilities. |
| Bind | Resolve the authorized root and its settings. For a password field, use the reference in `Ready.CredentialBindings` on the established channel. A disabled root must not connect to storage. |
| Connected | Send the event with a fresh request ID when the normal storage session is ready. |
| Stop or disconnect | Release streams and temporary transfer leases. Use a new session for the next launch. |

Keep a single frame reader and serialize complete writes. In-flight upload
handling must still accept a matching cancellation command. Credentials and
raw backend errors should stay out of responses and ordinary logs.
[Package configuration](package-configuration.md#configuration-only-worker-startup)
describes the separate startup mode for authorization callbacks before a root exists.

## List, stat and read files

Use `AdapterFileAddress` for root-qualified addresses. Validate the authorized
root key and relative path before accessing the backend. The empty path
identifies the root. Map the address to the provider's native namespace
without permitting aliases or paths that leave the configured root.

- **List** returns at most the requested page size and binds any continuation
  cursor to the root and parent directory. Every incomplete page carries a
  next cursor. Specify how your backend behaves when the directory changes
  while paging.
- **Stat** returns the current revision or the protocol's absent result.
  Decide whether a revision represents a server generation, a content hash,
  or a weaker metadata observation. Document the distinction.
- **ReadRange** checks a supplied revision and returns one bounded range. Send
  the matching `ReadRangeReady` followed by the final v2 binary chunk. Keep
  the declared length, offset, session, stream and root consistent.

For providers that send changes, keep change-feed cursors separate from
directory-list cursors. State their scope, retention and recovery behavior.
An expired cursor needs an explicit invalidation or reconciliation path
consistent with the negotiated feed.

## Apply a mutation and recover a lost reply

A request ID identifies one transport exchange. A stable operation ID identifies
the mutation across retries. Retain the source and destination addresses,
conditions, and effective content identity in its binding. A repeated ID with
changed inputs is an error. An accepted retry should recover the original
receipt or report that the outcome is unknown. A fresh request ID alone gives
no evidence that a prior operation failed.

For a conditional upload, validate the operation and its preconditions, receive
the declared bounded stream into a transfer lease, then verify the bytes before
publication. Validate the offset and checksum of each chunk. Use the backend's
conditional operation when available and report unsupported conditions before
changing storage. The SDK's frame, address and lease types do not give a storage
backend compare-and-swap or durable replay by themselves.

A cancellation during receive releases the lease before acknowledging
cancellation. If the remote mutation already committed or its response was
lost, use the stable operation binding to determine recovery. Never adopt a
response from a previous Worker session.

## Add optional features deliberately

Offer [structured health, update discovery or authorization callbacks](package-configuration.md#authorization-health-and-updates)
if the Worker actually implements them. Treat the original-directory
[admission](original-directory-admission-wire.md),
[retirement](original-directory-retirement-wire.md) and
[atomic deletion](atomic-original-directory-deletion.md) protocols as separate
capabilities. They require native history, exclusion and durable effect evidence
in addition to the SDK wire validation.
