"""Isolated Quest 3D navigation executor for contract-fixture conformance tests only."""

from dataclasses import dataclass

from orby_navigation_contract_v1_1 import (
    AuthenticatedSessionBinding, NavigationAcknowledgement, NavigationExecutionResult,
)
from orby_navigation_mock_coordinator import MockNavigationCoordinator


@dataclass(frozen=True)
class QuestNavigationSafetyPolicy:
    maximum_speed_mps: float = 1.2
    maximum_acceleration_mps2: float = 2.5
    comfortable_follow_distance_m: float = 0.9
    follow_stop_distance_m: float = 0.06
    tracking_ready_hold_seconds: float = 0.6
    minimum_tracked_head_height_m: float = 0.5


@dataclass(frozen=True)
class QuestAcceptance:
    acknowledgement: NavigationAcknowledgement | None
    immediate_result: NavigationExecutionResult | None
    interrupted_results: tuple[NavigationExecutionResult, ...]


class MockQuestNavigationExecutor:
    """Models acknowledgement ordering and client-owned Quest movement safety."""

    def __init__(self, policy: QuestNavigationSafetyPolicy | None = None):
        self.policy = policy or QuestNavigationSafetyPolicy()
        self.coordinator = MockNavigationCoordinator()
        self.autonomous_follow_enabled = True
        self.explicit_operation_id: str | None = None
        self.tracking_valid = True
        self.manually_paused = False
        self.results: list[NavigationExecutionResult] = []

    def accept(self, command: dict, *, binding: AuthenticatedSessionBinding,
               received_at_ms: int, now_ms: int, known_idea_ids: set[str],
               resumable: bool = False, active_operation_id: str | None = None,
               tracking_valid: bool = True, destination_available: bool = True,
               path_clear: bool = True, requested_speed_mps: float = 1.2,
               requested_acceleration_mps2: float = 2.5,
               enabled: bool = True) -> QuestAcceptance:
        previous_interruptions = len(self.coordinator.interrupted_results)
        acknowledgement = self.coordinator.accept(command, enabled=enabled,
            received_at_ms=received_at_ms, now_ms=now_ms, binding=binding,
            known_idea_ids=known_idea_ids, resumable=resumable,
            active_operation_id=active_operation_id)
        interruptions = tuple(self.coordinator.interrupted_results[previous_interruptions:])
        self.results.extend(interruptions)
        if acknowledgement is None or acknowledgement.status == "rejected":
            return QuestAcceptance(acknowledgement, None, interruptions)
        self.tracking_valid = tracking_valid
        self.autonomous_follow_enabled = False
        self.explicit_operation_id = acknowledgement.operation_id
        failure = None
        if not tracking_valid or requested_speed_mps > self.policy.maximum_speed_mps \
                or requested_acceleration_mps2 > self.policy.maximum_acceleration_mps2:
            failure = "movement_failed"
        elif not destination_available or not path_clear:
            failure = "destination_unavailable"
        if failure is None:
            return QuestAcceptance(acknowledgement, None, interruptions)
        result = self._finish(acknowledgement.operation_id, binding, "failed", failure)
        return QuestAcceptance(acknowledgement, result, interruptions)

    def complete(self, binding: AuthenticatedSessionBinding) -> NavigationExecutionResult:
        if not self.explicit_operation_id:
            raise ValueError("No explicit Quest navigation operation is active.")
        return self._finish(self.explicit_operation_id, binding, "completed", None)

    def lose_tracking(self, binding: AuthenticatedSessionBinding) -> NavigationExecutionResult:
        self.tracking_valid = False
        if not self.explicit_operation_id:
            raise ValueError("No explicit Quest navigation operation is active.")
        return self._finish(self.explicit_operation_id, binding, "failed", "movement_failed")

    def restore_tracking(self):
        self.tracking_valid = True
        if not self.explicit_operation_id and not self.manually_paused:
            self.autonomous_follow_enabled = True

    def _finish(self, operation_id: str, binding: AuthenticatedSessionBinding,
                status: str, failure: str | None) -> NavigationExecutionResult:
        pending = self.coordinator._pending[((binding.subject_id, binding.session_id,
                                              binding.client_instance_id), operation_id)]
        payload = {"contract_version": "1.1", "request_id": pending.request_id,
            "session_id": pending.session_id, "operation_id": pending.operation_id,
            "action": pending.action.value, "target_idea_ids": list(pending.target_idea_ids),
            "status": status, "failure_code": failure}
        result = self.coordinator.record_result(payload, binding=binding)
        self.results.append(result)
        if self.explicit_operation_id == operation_id:
            self.explicit_operation_id = None
        self.autonomous_follow_enabled = (
            self.tracking_valid and not self.manually_paused and self.explicit_operation_id is None)
        return result
