"""Mock-only client execution boundary for contract 1.1 acknowledgements and results."""

from dataclasses import dataclass

from pydantic import ValidationError

from orby_navigation_contract_v1_1 import (
    AuthenticatedSessionBinding, COMMAND_MAX_AGE_MS, NavigationAcknowledgement,
    NavigationAction, NavigationCommand, NavigationExecutionResult,
)


@dataclass(frozen=True)
class PendingOperation:
    request_id: str
    session_id: str
    operation_id: str
    action: NavigationAction
    target_idea_ids: tuple[str, ...]


class MockNavigationCoordinator:
    """Models Webby/Uni's fail-closed local authorization boundary."""

    def __init__(self):
        self._seen: set[tuple[str, str]] = set()
        self._pending: dict[str, PendingOperation] = {}

    def accept(self, payload: dict, *, enabled: bool, received_at_ms: int, now_ms: int,
               binding: AuthenticatedSessionBinding, known_idea_ids: set[str], resumable: bool
               ) -> NavigationAcknowledgement:
        request_id = payload.get("request_id", "invalid")
        payload_session = payload.get("session_id", binding.session_id)
        failure = None
        command = None
        if not enabled:
            failure = "feature_disabled"
        elif now_ms < binding.authenticated_at_ms or now_ms >= binding.expires_at_ms:
            failure = "session_binding_expired"
        elif (not isinstance(received_at_ms, int) or not isinstance(now_ms, int)
              or received_at_ms > now_ms or now_ms - received_at_ms > COMMAND_MAX_AGE_MS):
            failure = "stale_command"
        else:
            try:
                command = NavigationCommand.model_validate(payload)
            except (ValidationError, ValueError):
                failure = "invalid_command"
        if command is not None and command.navigation.execution_disposition != "authorized":
            failure = "not_authorized"
        if command is not None and failure is None and command.session_id != binding.session_id:
            failure = "wrong_session"
        if command is not None and failure is None \
                and (command.session_id, command.request_id) in self._seen:
            failure = "replayed_command"
        if command is not None and failure is None \
                and command.proposed_action == NavigationAction.APPROACH_IDEA \
                and command.target_idea_ids[0] not in known_idea_ids:
            failure = "unknown_idea"
        if command is not None and failure is None \
                and command.proposed_action == NavigationAction.RESUME and not resumable:
            failure = "operation_not_resumable"
        if failure:
            return NavigationAcknowledgement(contract_version="1.1", request_id=request_id,
                session_id=payload_session, operation_id=None, status="rejected",
                failure_code=failure)
        operation_id = "op-" + command.request_id[:120]
        pending = PendingOperation(command.request_id, command.session_id, operation_id,
                                   command.proposed_action, tuple(command.target_idea_ids))
        self._seen.add((command.session_id, command.request_id))
        self._pending[operation_id] = pending
        return NavigationAcknowledgement(contract_version="1.1",
            request_id=command.request_id, session_id=command.session_id,
            operation_id=operation_id, status="accepted", failure_code=None)

    def record_result(self, payload: dict) -> NavigationExecutionResult:
        result = NavigationExecutionResult.model_validate(payload)
        pending = self._pending.get(result.operation_id)
        if pending is None:
            raise ValueError("Unknown navigation operation.")
        binding = (result.request_id, result.session_id, result.operation_id, result.action,
                   tuple(result.target_idea_ids))
        expected = (pending.request_id, pending.session_id, pending.operation_id, pending.action,
                    pending.target_idea_ids)
        if binding != expected:
            raise ValueError("Navigation result does not match its authorized command.")
        del self._pending[result.operation_id]
        return result
