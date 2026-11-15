"""Isolated proposed Orby navigation contract fixtures.

These models intentionally live in tests. They document a capability-gated proposal without
adding navigation actions to production routing or changing the deployed 1.0 contract.
"""

import json
import sys
import unittest
from pathlib import Path

from pydantic import ValidationError

BACKEND = Path(__file__).resolve().parents[1]
if str(BACKEND) not in sys.path:
    sys.path.insert(0, str(BACKEND))

from conversation_orchestrator import ConversationAction, ConversationResponse
from orby_navigation_contract_v1_1 import (
    AuthenticatedSessionBinding, CapabilityOffer, CapabilitySelection,
    NavigationAcknowledgement, NavigationAction, NavigationCommand,
    NavigationExecutionResult, NavigationRequest, negotiate_capability,
)
from orby_navigation_mock_coordinator import MockNavigationCoordinator

FIXTURES = Path(__file__).parent / "fixtures" / "orby_navigation"
class OrbyNavigationContractFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cases = json.loads((FIXTURES / "navigation_cases.json").read_text(encoding="utf-8"))
        cls.lifecycle = json.loads(
            (FIXTURES / "integration_lifecycle.json").read_text(encoding="utf-8"))

    def test_all_proposed_navigation_fixtures_validate(self):
        expected = {"follow_user", "approach_idea", "return_home", "reposition", "stop",
                    "resume", "ambiguous_target", "unavailable_destination"}
        self.assertEqual({case["name"] for case in self.cases}, expected)
        for case in self.cases:
            with self.subTest(case=case["name"]):
                request = NavigationRequest.model_validate(case["request"])
                response = NavigationCommand.model_validate(case["response"])
                self.assertEqual(response.request_id, request.request_id)
                self.assertEqual(response.session_id, request.session_id)

    def test_navigation_requires_explicit_client_capability(self):
        payload = dict(self.cases[0]["request"])
        payload["client_capabilities"] = []
        with self.assertRaises(ValidationError):
            NavigationRequest.model_validate(payload)

    def test_navigation_is_disabled_without_bilateral_capability_negotiation(self):
        offers = (
            CapabilityOffer(contract_versions=["1.0"], capabilities=[], platform="quest_3d"),
            CapabilityOffer(contract_versions=["1.0", "1.1"], capabilities=[], platform="web_2d"),
            CapabilityOffer(contract_versions=["1.0"], capabilities=["companion_navigation.v1"],
                            platform="web_3d"),
        )
        for offer in offers:
            with self.subTest(offer=offer):
                selection = negotiate_capability(offer, server_navigation_enabled=True)
                self.assertEqual(selection.active_contract_version, "1.0")
                self.assertEqual(selection.enabled_capabilities, [])
                self.assertFalse(selection.acknowledgement_required)

    def test_navigation_requires_server_and_client_capability(self):
        offer = CapabilityOffer(contract_versions=["1.0", "1.1"],
            capabilities=["companion_navigation.v1"], platform="quest_3d")
        disabled = negotiate_capability(offer, server_navigation_enabled=False)
        enabled = negotiate_capability(offer, server_navigation_enabled=True)
        self.assertEqual(disabled.active_contract_version, "1.0")
        self.assertEqual(enabled.active_contract_version, "1.1")
        self.assertEqual(enabled.command_max_age_ms, 30_000)
        self.assertTrue(enabled.acknowledgement_required)

    def test_golden_capability_and_lifecycle_fixtures(self):
        negotiation = self.lifecycle["capability_negotiation"]
        offer = CapabilityOffer.model_validate(negotiation["offer"])
        enabled = CapabilitySelection.model_validate(negotiation["enabled_selection"])
        disabled = CapabilitySelection.model_validate(negotiation["disabled_selection"])
        self.assertEqual(negotiate_capability(offer, True), enabled)
        self.assertEqual(negotiate_capability(offer, False), disabled)
        binding = AuthenticatedSessionBinding.model_validate(
            self.lifecycle["authenticated_session_binding"])
        commands = {case["name"]: case["response"] for case in self.cases}
        self.assertEqual({case["name"] for case in self.lifecycle["cases"]},
            {"follow", "approach_idea", "reposition", "home", "stop", "resume",
             "interruption", "stale", "ambiguous", "rejected"})
        for case in self.lifecycle["cases"]:
            with self.subTest(case=case["name"]):
                command = NavigationCommand.model_validate(
                    commands[case["command_fixture"]]).model_dump(mode="json")
                coordinator = MockNavigationCoordinator()
                actual = coordinator.accept(command, enabled=case["enabled"],
                    received_at_ms=case["received_at_ms"], now_ms=case["now_ms"],
                    binding=binding, known_idea_ids=set(case["known_idea_ids"]),
                    resumable=case["resumable"])
                expected = NavigationAcknowledgement.model_validate(case["acknowledgement"])
                self.assertEqual(actual, expected)
                if "result" in case:
                    result = NavigationExecutionResult.model_validate(case["result"])
                    self.assertEqual(coordinator.record_result(case["result"]), result)

    def test_interrupted_result_closes_operation_and_cannot_be_replayed(self):
        binding = AuthenticatedSessionBinding.model_validate(
            self.lifecycle["authenticated_session_binding"])
        command = NavigationCommand.model_validate(self.cases[0]["response"]).model_dump(mode="json")
        coordinator = MockNavigationCoordinator()
        acknowledgement = coordinator.accept(command, enabled=True, received_at_ms=100_000,
            now_ms=100_001, binding=binding, known_idea_ids=set(), resumable=False)
        interrupted = {"contract_version": "1.1", "request_id": command["request_id"],
            "session_id": command["session_id"], "operation_id": acknowledgement.operation_id,
            "action": "follow_user", "target_idea_ids": [], "status": "interrupted",
            "failure_code": "interrupted_by_new_command"}
        self.assertEqual(coordinator.record_result(interrupted).status, "interrupted")
        with self.assertRaises(ValueError):
            coordinator.record_result(interrupted)

    def test_ambiguous_and_unavailable_destinations_never_execute(self):
        by_name = {case["name"]: case for case in self.cases}
        ambiguous = NavigationCommand.model_validate(by_name["ambiguous_target"]["response"])
        unavailable = NavigationCommand.model_validate(by_name["unavailable_destination"]["response"])
        self.assertTrue(ambiguous.requires_confirmation)
        self.assertFalse(ambiguous.should_execute_action)
        self.assertEqual(unavailable.navigation.failure_code, "destination_unavailable")
        self.assertFalse(unavailable.should_execute_action)

    def test_invalid_authorized_approach_without_target_is_rejected(self):
        by_name = {case["name"]: case for case in self.cases}
        payload = json.loads(json.dumps(by_name["approach_idea"]["response"]))
        payload["target_idea_ids"] = []
        with self.assertRaises(ValidationError):
            NavigationCommand.model_validate(payload)

    def test_legacy_1_0_fixture_still_uses_production_contract(self):
        legacy = json.loads((FIXTURES / "legacy_1_0_response.json").read_text(encoding="utf-8"))
        parsed = ConversationResponse.model_validate(legacy)
        self.assertEqual(parsed.contract_version, "1.0")
        self.assertEqual(parsed.proposed_action, "move_bubble")

    def test_production_1_0_actions_do_not_enable_navigation(self):
        production_actions = {action.value for action in ConversationAction}
        self.assertTrue(production_actions.isdisjoint({action.value for action in NavigationAction}))
        legacy = json.loads((FIXTURES / "legacy_1_0_response.json").read_text(encoding="utf-8"))
        legacy["contract_version"] = "1.1"
        with self.assertRaises(ValidationError):
            ConversationResponse.model_validate(legacy)


if __name__ == "__main__":
    unittest.main()
