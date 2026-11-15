"""Mock-only client execution boundary for contract 1.1 acknowledgements and results."""

from collections import OrderedDict
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
    binding_key: tuple[str, str, str]


class MockNavigationCoordinator:
    """Models Webby/Uni's fail-closed local authorization boundary."""

    def __init__(self, ledger_limit: int = 256):
        self._seen: OrderedDict[tuple[str, str, str, str], int] = OrderedDict()
        self._pending: dict[tuple[tuple[str, str, str], str], PendingOperation] = {}
        self._active: dict[tuple[str, str, str], str] = {}
        self.ledger_limit = ledger_limit
        self.interrupted_results: list[NavigationExecutionResult] = []

    def accept(self, payload: dict, *, enabled: bool, received_at_ms: int, now_ms: int,
               binding: AuthenticatedSessionBinding, known_idea_ids: set[str], resumable: bool,
               active_operation_id: str | None = None
               ) -> NavigationAcknowledgement | None:
        if not enabled or payload.get("contract_version") == "1.0":
            return None
        request_id = payload.get("request_id", "invalid")
        payload_session = payload.get("session_id", binding.session_id)
        failure = None
        command = None
        if (not isinstance(received_at_ms, int) or not isinstance(now_ms, int)
              or received_at_ms > now_ms or now_ms - received_at_ms > COMMAND_MAX_AGE_MS):
            failure = "stale_command"
        elif now_ms < binding.authenticated_at_ms or now_ms >= binding.expires_at_ms:
            failure = "session_binding_expired"
        else:
            try:
                command = NavigationCommand.model_validate(payload)
            except (ValidationError, ValueError):
                failure = "invalid_command"
        if command is not None and command.navigation.execution_disposition != "authorized":
            failure = "not_authorized"
        if command is not None and failure is None and command.session_id != binding.session_id:
            failure = "wrong_session"
        binding_key = (binding.subject_id, binding.session_id, binding.client_instance_id)
        replay_key = binding_key + (command.request_id,) if command is not None else None
        if command is not None and failure is None and replay_key in self._seen:
            failure = "replayed_command"
        if command is not None and failure is None \
                and command.proposed_action == NavigationAction.APPROACH_IDEA \
                and command.target_idea_ids[0] not in known_idea_ids:
            failure = "unknown_idea"
        if command is not None and failure is None and command.proposed_action in {
                NavigationAction.STOP, NavigationAction.RESUME} \
                and (not resumable or not active_operation_id):
            failure = "operation_not_resumable"
        current = self._active.get(binding_key)
        if command is not None and failure is None and command.proposed_action in {
                NavigationAction.STOP, NavigationAction.RESUME} \
                and current is not None and current != active_operation_id:
            failure = "operation_not_resumable"
        if failure:
            return NavigationAcknowledgement(contract_version="1.1", request_id=request_id,
                session_id=payload_session, operation_id=None, status="rejected",
                failure_code=failure)
        operation_id = ("op-" + command.request_id)[:128]
        operation_key = (binding_key, operation_id)
        if operation_key in self._pending:
            return NavigationAcknowledgement(contract_version="1.1", request_id=request_id,
                session_id=payload_session, operation_id=None, status="rejected",
                failure_code="replayed_command")
        if current and command.proposed_action not in {NavigationAction.STOP,
                                                       NavigationAction.RESUME}:
            prior = self._pending[(binding_key, current)]
            interrupted = NavigationExecutionResult(contract_version="1.1",
                request_id=prior.request_id, session_id=prior.session_id,
                operation_id=prior.operation_id, action=prior.action,
                target_idea_ids=list(prior.target_idea_ids), status="interrupted",
                failure_code="interrupted_by_new_command")
            self.interrupted_results.append(interrupted)
            del self._pending[(binding_key, current)]
            del self._active[binding_key]
        pending = PendingOperation(command.request_id, command.session_id, operation_id,
            command.proposed_action, tuple(command.target_idea_ids), binding_key)
        self._seen[replay_key] = now_ms
        while len(self._seen) > self.ledger_limit:
            self._seen.popitem(last=False)
        self._pending[operation_key] = pending
        if command.proposed_action != NavigationAction.STOP:
            self._active[binding_key] = operation_id
        return NavigationAcknowledgement(contract_version="1.1",
            request_id=command.request_id, session_id=command.session_id,
            operation_id=operation_id, status="accepted", failure_code=None)

    def record_result(self, payload: dict, *, binding: AuthenticatedSessionBinding
                      ) -> NavigationExecutionResult:
        result = NavigationExecutionResult.model_validate(payload)
        binding_key = (binding.subject_id, binding.session_id, binding.client_instance_id)
        if result.session_id != binding.session_id:
            raise ValueError("Navigation result does not match its authenticated session.")
        operation_key = (binding_key, result.operation_id)
        pending = self._pending.get(operation_key)
        if pending is None:
            raise ValueError("Unknown navigation operation.")
        binding = (result.request_id, result.session_id, result.operation_id, result.action,
                   tuple(result.target_idea_ids))
        expected = (pending.request_id, pending.session_id, pending.operation_id, pending.action,
                    pending.target_idea_ids)
        if binding != expected:
            raise ValueError("Navigation result does not match its authorized command.")
        del self._pending[operation_key]
        if self._active.get(pending.binding_key) == result.operation_id:
            del self._active[pending.binding_key]
        return result
