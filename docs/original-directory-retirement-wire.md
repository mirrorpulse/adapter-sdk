# Original directory retirement messages

This optional protocol-v2 exchange is the final step after a qualified backend
has verified the original subtree and recorded its child effects. The SDK
defines the request and response. Backend support and production Host
qualification are required before it can acknowledge an original operation.
The sample Worker does not implement this capability.

The negotiated capability is `original-directory-effects-v1`. The new request
message is `RetireOriginalDirectory`; its successful response message is
`OriginalDirectoryRetired`. Ordinary `Delete`, `Move` and `MutationComplete`
messages cannot stand in for these messages. Protocol 1, absent capability,
unknown versions and differently cased capability names are refused.

## Request

`AdapterDirectoryRetirementRequest` contains the final nonrecursive `Delete` or
`Move` of one original directory. It retains:

- The immutable operation ID, exact source address, destination for a move, and
  explicit source revision precondition.
- The complete `AdapterDirectoryRetirementEvidence`: original directory witness,
  sealed manifest fingerprint, required direct-child operations, chronologically
  accepted child receipts and the terminal owned directory revision.
- The exact original source and destination direct-parent witnesses. The source
  parent's generation must equal the admitted directory's parent generation.
- Each available original parent predecessor receipt, separately from the child
  ledger. One parent may have at most one predecessor; a receipt that affects
  both parents may be supplied once. No foreign, duplicate or unrelated receipt
  can become the basis of a new parent transition.
- A canonical SHA-256 fingerprint of all these fields. Required-child set and
  parent ordering are canonical; accepted-child chronology remains significant.

The source precondition must equal the child ledger's final expected revision.
It cannot be absent, replaced by a current `Stat`, or silently taken from a
later object. The final operation cannot reuse a child operation ID. The source
cannot be a configured root; a move cannot replace a destination, target a
configured root, or move into its own subtree. Backends must additionally apply
their native path normalization and alias rules.

An author constructs the retained request and uses `CalculateFingerprint()` to
bind it. Every decoder revalidates the complete request. Recalculating a hash is
not permission to rebase retained state or replace an original child manifest.
Witness provenance and sealed-manifest ownership remain backend/Host duties.

## Response and recovery

The response payload is the existing `AdapterOwnedChildEffectReceipt`, with the
exact request fingerprint, operation, admission, instance, original directory
generation, source and destination. It must contain precisely the admitted
direct-parent transitions. Each transition extends the retained predecessor;
without a predecessor it starts at the original parent revision and has no
predecessor fingerprint. A deletion's accepted revision remains null.

`AdapterDirectoryRetirementWire.ReadRequest` and `ReadResponse` validate the
payload and envelope. Responses must match the request ID, Worker session,
instance, protocol and response role. Each payload is limited to 1 MiB, below
the existing outer frame limit. A null or ordinary mutation response is not
evidence of retirement.

These checks grant no native mutation authority. A capable backend still needs
to verify original identity/history and the complete child manifest under its
atomicity or exclusion mechanism; commit effects and durable receipts together;
and recover a lost reply without touching a later tree. The Host must persist
and compare the exact receipt before advancing its ledger or acknowledging an
original journal operation. Unsupported backends leave the original pending.
The bounded request does not introduce manifest paging; an oversized admission
requires explicit refusal rather than truncation.

## Verify the final effect

Start with an [admitted directory](original-directory-admission-wire.md) and
retain its original witnesses. Test a successful retirement with zero
children, another with several recorded child effects, and rejection after an
unrelated writer changes a parent. Drop the reply after the native effect
commits, restart the Worker, and retry with the original evidence. The same
receipt should be recovered. An unrelated object at the old name must remain
outside the operation.

The contract executable exercises encoded control-frame round trips, complete
child chains, exact parent predecessors, Delete and cross-root Move responses,
null revisions, changed bindings, replay-correlation mistakes, downgrade and
payload limits. These are SDK wire-boundary checks, not a claim that a live
Worker/backend or recursive Host acceptance is complete.
