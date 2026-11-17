# Quest navigation fixture readiness

Scope: mock-only conformance material for a future Uni implementation. Nothing in this folder is
imported by production contract 1.0 or enables Quest navigation.

## References

- `quest_navigation_cases.json`: Quest command, failure, interruption, stale and replay cases.
- `navigation_cases.json`: authoritative contract 1.1 command envelopes.
- `integration_lifecycle.json`: authenticated acknowledgement/result lifecycle.
- `../../orby_quest_navigation_mock.py`: local Quest safety and arbitration model.
- `../../test_orby_quest_navigation_fixtures.py`: executable conformance tests.

## Required acknowledgement and result behavior

The Quest client validates capability, trusted receipt age, authenticated binding, replay, session,
targets and active-operation snapshots before movement. An accepted command receives one bound
acknowledgement before transform motion begins. Local destination or tracking failures after acceptance
receive exactly one bound terminal result. Rejected, stale and replayed commands never move Orby.

Tracking loss maps to the existing `failed` / `movement_failed` result; no new 1.1 wire value is added.
An unavailable or unsafe destination maps to `failed` / `destination_unavailable`. A replacement
explicit command emits `interrupted` / `interrupted_by_new_command` for the prior operation before the
replacement starts.

## Quest safety limits

The fixtures mirror the currently authored Orby placeholder values:

- maximum speed: `1.2 m/s`
- maximum acceleration: `2.5 m/s²`
- comfortable follow distance: `0.9 m`
- follow stop distance: `0.06 m`
- tracking-ready hold: `0.6 s`
- minimum valid tracked-head height: `0.5 m`

These are upper/local safety constraints, not AI-selectable parameters. Uni remains responsible for
play-space bounds, obstacle clearance, valid anchors, tracking quality, comfort review and reducing
motion below these limits when necessary.

## Autonomous-follow arbitration

1. Autonomous recovery-follow owns Orby only while no explicit operation is active.
2. An accepted explicit command suspends autonomous follow before movement.
3. A replacement command safely interrupts the prior explicit operation.
4. Tracking loss terminates explicit movement and keeps autonomous follow disabled.
5. Autonomous follow may resume only after valid tracking is held for the local ready interval, no
   explicit operation remains, and the user has not manually paused motion.
6. Stop/resume must match the active-operation snapshot associated with the outgoing request ID.

## Readiness boundary

The fixtures are ready for Uni to copy into isolated EditMode tests. Live readiness still requires the
binding-scoped lifecycle issue identified in AI-020 to be resolved across clients, an authenticated
Backend transport, approved capability negotiation, a dedicated Quest navigation controller, physical
comfort/collision testing, and an explicit enablement decision.
