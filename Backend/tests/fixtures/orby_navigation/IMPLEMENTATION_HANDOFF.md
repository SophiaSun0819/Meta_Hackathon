# Orby navigation contract 1.1 implementation handoff

Status: isolated, mock-only, disabled by default. Production conversation contract 1.0 remains
authoritative until Backend, Uni, and Webby separately approve and implement negotiation.

## Authoritative references

- `navigation_cases.json`: exact 1.1 command examples.
- `integration_lifecycle.json`: capability, authenticated session, acknowledgement, result,
  interruption, stale, ambiguous, and rejection examples.
- `../../orby_navigation_contract_v1_1.py`: strict proposed DTO definitions and semantics.
- `../../orby_navigation_mock_coordinator.py`: fail-closed receipt and lifecycle behavior.
- WEB-014 commit `6cf5980eb6f59dc9fe68ec7398f31cb31ff9df95`: strict local adapter reference.

## Negotiation and identity

Navigation is enabled only when all conditions are true:

1. The authenticated client offers contract `1.1` and `companion_navigation.v1`.
2. The server explicitly enables the same capability for that authenticated session and build.
3. The selected contract is `1.1`, `acknowledgement_required` is true, and
   `command_max_age_ms` is `30000`.
4. The client feature flag remains enabled when the command arrives.

Otherwise selection is contract `1.0` with no navigation capability. Capability selection is bound
to server-derived `subject_id`, `session_id`, `client_instance_id`, authentication time, and expiry.
The AI command must never be trusted to assert those values. Tokens, credentials, and identity proofs
are transport concerns and never appear in command, acknowledgement, or result JSON.

Trusted `received_at_ms` is stamped inside the authenticated client transport immediately after a
verified response is received. It is not accepted from a custom event, AI output, or JSON body.
Age is computed with one monotonic clock. Future timestamps and age greater than 30,000 ms fail closed.

## Exact DTOs

The strict command envelope is the exact key set already accepted by WEB-014:

```text
NavigationCommand {
  contract_version: "1.1"
  request_id: Identifier
  session_id: string(1..128)
  intent_category: "companion_navigation"
  proposed_action: "follow_user" | "approach_idea" | "return_companion_home" |
                   "reposition_companion" | "stop_companion_navigation" |
                   "resume_companion_navigation"
  target_idea_ids: Identifier[0..1]
  navigation: {
    destination_kind: "user" | "idea" | "home" | "viewport_anchor" | "none"
    relative_position: "front" | "front_left" | "front_right" | "left" | "right" |
                       "above" | "below" | "viewing_side" | "default"
    distance_band: "near" | "standard" | "far"
    movement_mode: "one_shot" | "continuous" | "immediate" | "resume"
    operation_scope: "companion_only"
    execution_disposition: "authorized" | "clarification_required" | "blocked"
    failure_code: "destination_unavailable" | "operation_not_active" |
                  "operation_not_resumable" | null
  }
  spoken_response: string(1..1200)
  requires_confirmation: boolean
  clarification_id: Identifier | null
  clarification_question: string | null
  candidate_idea_ids: Identifier[0..8]
  should_execute_action: boolean
  status: "completed"
  error: null
}

NavigationAcknowledgement {
  contract_version: "1.1"
  request_id: Identifier
  session_id: string(1..128)
  operation_id: Identifier | null
  status: "accepted" | "rejected"
  failure_code: "feature_disabled" | "invalid_command" | "not_authorized" |
                "operation_not_resumable" | "replayed_command" |
                "session_binding_expired" | "stale_command" | "unknown_idea" |
                "wrong_session" | null
}

NavigationExecutionResult {
  contract_version: "1.1"
  request_id: Identifier
  session_id: string(1..128)
  operation_id: Identifier
  action: NavigationAction
  target_idea_ids: Identifier[0..1]
  status: "completed" | "failed" | "interrupted" | "cancelled"
  failure_code: "destination_unavailable" | "interrupted_by_new_command" |
                "movement_failed" | "session_ended" | null
}
```

An accepted acknowledgement has exactly one client-created operation ID and no failure. A rejected
acknowledgement has no operation ID and one stable failure. A completed result has no failure; every
other terminal result has one failure. Result request, session, operation, action, and target IDs must
exactly match the accepted command. Exactly one terminal result closes an operation.

## Command semantics and examples

Full copyable JSON is in the golden files. The required mappings are:

| Golden case | Action | Destination | Mode | Target |
| --- | --- | --- | --- | --- |
| `follow_user` | `follow_user` | `user` | `continuous` | none |
| `approach_idea` | `approach_idea` | `idea` | `one_shot` | exactly one known idea |
| `reposition` | `reposition_companion` | `user` or `viewport_anchor` | `one_shot` | none |
| `return_home` | `return_companion_home` | `home` | `one_shot` | none |
| `stop` | `stop_companion_navigation` | `none` | `immediate` | current operation only |
| `resume` | `resume_companion_navigation` | `none` | `resume` | paused operation only |

Ambiguous and blocked commands are valid conversational responses but are not executable commands.
They produce a rejected acknowledgement (`not_authorized`) if they reach the local execution boundary.
Stale, replayed, wrong-session, unknown-idea, expired-session, and non-resumable commands also reject.

For stop and resume, the client must retain the outgoing request snapshot keyed by `request_id`,
including the active operation ID. When the response returns, it must reject execution if the current
operation no longer equals that snapshot. This prevents a delayed stop from stopping a newer movement
without adding fields that would break WEB-014's exact command envelope.

## Operation lifecycle

```text
disabled/unnegotiated -> ignore as legacy 1.0
received -> validate authenticated binding, age, exact schema, authorization, session, replay,
            targets, request snapshot and local destination
rejected -> send one rejected acknowledgement; do not move
accepted -> allocate operation_id; persist replay entry; send acknowledgement before movement
active -> client owns path, speed, collision/obstacle checks and visual state
paused -> only a bound resume may continue
interrupted -> stop safely; send old operation's terminal interrupted result before starting replacement
completed/failed/cancelled -> send one terminal result and release operation state
```

## Quest 3D safety ownership

Quest must use a dedicated Orby navigation controller. `OrbyPlaceholderView` already performs bounded
headset recovery following, but it has no command, operation, acknowledgement, or result boundary.
Explicit navigation must arbitrate with or temporarily suppress that recovery behavior. The existing
AI workflow coroutine generation is not a navigation operation ID.

Quest owns tracked-pose validity, play-space bounds, comfortable distance, maximum velocity and
acceleration, collision clearance, idea-anchor existence, home-anchor validity, pause/stop behavior,
session changes, and safe recovery after tracking loss. Backend output is intent only and never a
world-space coordinate or authority to move a transform.

## Website 2D/3D safety ownership

WEB-014 already validates the exact command envelope, age, replay, session, known idea, and resumable
state. Its current transport is a feature-gated local custom event and its receipt timestamp is event
data. Live integration must stamp receipt time inside the authenticated adapter, retain a bounded
request/replay ledger, allocate operation IDs, and emit acknowledgement/results.

Website 2D continues using safe dock/obstacle policy and reduced-motion placement. Website 3D continues
using known scene anchors and local collision-safe routes. AI never supplies pixels, transforms, paths,
CSS selectors, scene objects, or animation duration.

## Copyable Uni task

```text
TASK: UNI — Orby Navigation 1.1 Local Client Adapter
Consume Backend/tests/fixtures/orby_navigation/navigation_cases.json and
integration_lifecycle.json without changing their fields. Implement strict DTO validation and a
disabled-by-default companion_navigation.v1 capability gate. Bind negotiation to the authenticated
Quest subject/session/client instance. Stamp receipt time internally with a monotonic clock; reject
future or >30-second commands, replayed request IDs, session mismatches, stale stop/resume snapshots,
unknown ideas, and non-authorized commands. Add a dedicated Quest Orby navigation controller; do not
reuse the AI workflow coroutine ID or treat backend output as a transform. Arbitrate explicit movement
with OrbyPlaceholderView recovery follow. Validate tracked headset, home and idea anchors, play-space,
clearance, comfort speed/acceleration and tracking loss locally. Acknowledge before moving with a new
operation_id, then emit exactly one bound completed/failed/interrupted/cancelled result. Stop safely on
session change and report session_ended. Preserve production contract 1.0 and voice-to-idea. Use mock
transport and tests only; do not deploy or enable the capability.
```

## Copyable Webby task

```text
TASK: WEB — Orby Navigation 1.1 Lifecycle Adapter
Extend the feature-gated WEB-014 adapter using Backend/tests/fixtures/orby_navigation/
navigation_cases.json and integration_lifecycle.json as golden inputs. Preserve its exact strict
command validation and default-off local-mock isolation. Add authenticated capability selection,
transport-owned monotonic receivedAt, a bounded per-session request/replay ledger, outgoing request
snapshots for stop/resume, client-created operation IDs, acknowledgements, and exactly one terminal
result. Keep website 2D safe docking/obstacle avoidance/reduced-motion behavior and website 3D known
anchors/local safe routing client-owned. Reject unknown ideas, wrong sessions, stale/replayed commands,
non-authorized responses and changed stop/resume operation snapshots. Do not fetch production APIs,
enable navigation, or change contract 1.0 until Backend transport is approved.
```

## Minimum Backend transport

Before live enablement Backend must provide, under existing authenticated identity infrastructure:

1. A sanitized capability/build operation returning supported contract versions, enabled capabilities,
   maximum command age, acknowledgement requirement, and deployment identity.
2. A negotiated 1.1 conversation transport that preserves the current 1.0 endpoint behavior for legacy
   clients and binds each request to the authenticated subject/session/client instance.
3. Idempotent acknowledgement and terminal-result operations keyed by subject, session, request, and
   operation IDs. Duplicate identical submissions return the original outcome; conflicting reuse rejects.
4. Server-side bounded replay/idempotency storage and expiry. Client memory is defense in depth only.
5. Stable authentication, authorization, rate-limit and error behavior without logging transcripts,
   coordinates, credentials, or provider prompts.

Endpoint paths are intentionally not declared authoritative here. Backend ownership must approve them
alongside authentication and persistence design. No live capability may be advertised until Uni and
Webby pass the same golden fixtures and acknowledgement/result conformance tests.
