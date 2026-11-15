import json
import unittest
from pathlib import Path

from orby_navigation_mock_router import MockIdea, MockNavigationRouter
from orby_navigation_mock_coordinator import MockNavigationCoordinator
from orby_navigation_contract_v1_1 import AuthenticatedSessionBinding

FIXTURE = Path(__file__).parent / "fixtures" / "orby_navigation" / "routing_cases.json"


class OrbyNavigationMockRouterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cases = json.loads(FIXTURE.read_text(encoding="utf-8"))
        cls.router = MockNavigationRouter()
        cls.default_ideas = [MockIdea("idea-solar", "Solar Hub"),
                             MockIdea("idea-garden", "Community Garden")]

    def request_for(self, case):
        return {
            "contract_version": case.get("contract_version", "1.1"),
            "request_id": "route-" + case["name"],
            "session_id": "Session 1",
            "client_capabilities": case.get(
                "client_capabilities", ["companion_navigation.v1"]),
            "navigation_context": {
                "platform": "quest_3d",
                "navigation_state": case.get("navigation_state", "idle"),
                "active_operation_id": case.get("active_operation_id"),
                "selected_idea_id": case.get("selected_idea_id"),
                "user_anchor_available": case.get("user_anchor_available", True),
                "home_anchor_available": case.get("home_anchor_available", True),
                "resumable": case.get("resumable", False),
            },
        }

    @staticmethod
    def binding(session_id="Session 1"):
        return AuthenticatedSessionBinding(session_id=session_id, subject_id="user-test",
            client_instance_id="client-test", authenticated_at_ms=500, expires_at_ms=60_000)

    def test_fixture_driven_mock_routing(self):
        names = {case["name"] for case in self.cases}
        self.assertEqual(len(names), 14)
        for case in self.cases:
            with self.subTest(case=case["name"]):
                ideas = self.default_ideas if "ideas" not in case else [
                    MockIdea(value["idea_id"], value["title"]) for value in case["ideas"]]
                result = self.router.route(self.request_for(case), case["transcript"], ideas)
                if case.get("expected_fallthrough"):
                    self.assertIsNone(result)
                    continue
                self.assertIsNotNone(result)
                self.assertEqual(result.proposed_action.value, case["expected_action"])
                self.assertEqual(result.navigation.execution_disposition,
                                 case["expected_disposition"])
                self.assertEqual(result.should_execute_action, case["expected_execute"])
                if "expected_targets" in case:
                    self.assertEqual(result.target_idea_ids, case["expected_targets"])
                if "expected_candidates" in case:
                    self.assertEqual(result.candidate_idea_ids, case["expected_candidates"])
                if "expected_relative_position" in case:
                    self.assertEqual(result.navigation.relative_position,
                                     case["expected_relative_position"])
                if "expected_failure" in case:
                    self.assertEqual(result.navigation.failure_code, case["expected_failure"])

    def test_unrecognized_navigation_text_falls_through(self):
        result = self.router.route(self.request_for({"name": "ordinary"}),
                                   "What is virtual reality?", self.default_ideas)
        self.assertIsNone(result)

    def test_mock_router_has_no_production_import_path(self):
        import app
        self.assertNotIn("orby_navigation_mock_router", {
            value.__module__ for value in vars(app).values() if hasattr(value, "__module__")})

    def test_stale_replay_and_wrong_session_commands_fail_closed(self):
        command = self.router.route(self.request_for({"name": "fresh"}),
                                   "Move to my right", self.default_ideas).model_dump(mode="json")
        base = dict(enabled=True, received_at_ms=1_000, now_ms=1_001,
                    binding=self.binding(), known_idea_ids={"idea-solar", "idea-garden"},
                    resumable=False)
        stale = MockNavigationCoordinator().accept(command, **{**base, "now_ms": 31_001})
        self.assertEqual(stale.failure_code, "stale_command")
        wrong = MockNavigationCoordinator().accept(command,
            **{**base, "binding": self.binding("Different Session")})
        self.assertEqual(wrong.failure_code, "wrong_session")
        coordinator = MockNavigationCoordinator()
        self.assertEqual(coordinator.accept(command, **base).status, "accepted")
        self.assertEqual(coordinator.accept(command, **base).failure_code, "replayed_command")

    def test_unknown_idea_and_non_resumable_resume_fail_closed(self):
        approach = self.router.route(self.request_for({"name": "approach"}),
            "Move beside the Solar Hub", self.default_ideas).model_dump(mode="json")
        context = dict(enabled=True, received_at_ms=1_000, now_ms=1_001,
                       binding=self.binding(), known_idea_ids=set(), resumable=False)
        self.assertEqual(MockNavigationCoordinator().accept(approach, **context).failure_code,
                         "unknown_idea")
        resume_request = self.request_for({"name": "resume-bound", "navigation_state": "paused",
            "active_operation_id": "nav-active-1", "resumable": True})
        resume = self.router.route(resume_request, "Resume movement",
                                   self.default_ideas).model_dump(mode="json")
        self.assertEqual(MockNavigationCoordinator().accept(resume, **context).failure_code,
                         "operation_not_resumable")

    def test_acknowledgement_and_execution_result_remain_bound(self):
        request = self.request_for({"name": "bound-idea"})
        command = self.router.route(request, "Move beside the Solar Hub",
                                    self.default_ideas).model_dump(mode="json")
        coordinator = MockNavigationCoordinator()
        acknowledgement = coordinator.accept(command, enabled=True, received_at_ms=1_000,
            now_ms=1_001, binding=self.binding(), known_idea_ids={"idea-solar"},
            resumable=False)
        self.assertEqual(acknowledgement.status, "accepted")
        result = {"contract_version": "1.1", "request_id": acknowledgement.request_id,
            "session_id": acknowledgement.session_id,
            "operation_id": acknowledgement.operation_id, "action": "approach_idea",
            "target_idea_ids": ["idea-solar"], "status": "completed", "failure_code": None}
        self.assertEqual(coordinator.record_result(result).status, "completed")

    def test_result_with_wrong_request_session_or_target_is_rejected(self):
        for field, value in (("request_id", "other-request"),
                             ("session_id", "Other Session"),
                             ("target_idea_ids", ["idea-garden"])):
            with self.subTest(field=field):
                command = self.router.route(self.request_for({"name": "binding-" + field}),
                    "Move beside the Solar Hub", self.default_ideas).model_dump(mode="json")
                coordinator = MockNavigationCoordinator()
                acknowledgement = coordinator.accept(command, enabled=True,
                    received_at_ms=1_000, now_ms=1_001, binding=self.binding(),
                    known_idea_ids={"idea-solar"}, resumable=False)
                result = {"contract_version": "1.1", "request_id": command["request_id"],
                    "session_id": command["session_id"],
                    "operation_id": acknowledgement.operation_id, "action": "approach_idea",
                    "target_idea_ids": ["idea-solar"], "status": "completed",
                    "failure_code": None}
                result[field] = value
                with self.assertRaises(ValueError):
                    coordinator.record_result(result)

    def test_stop_without_active_operation_is_blocked(self):
        result = self.router.route(self.request_for({"name": "idle-stop"}),
                                   "Stop moving", self.default_ideas)
        self.assertEqual(result.navigation.execution_disposition, "blocked")
        self.assertEqual(result.navigation.failure_code, "operation_not_active")
        self.assertFalse(result.should_execute_action)

    def test_expired_authenticated_session_and_ambiguous_command_are_rejected(self):
        command = self.router.route(self.request_for({"name": "expired"}),
                                   "Move to my right", self.default_ideas).model_dump(mode="json")
        expired = MockNavigationCoordinator().accept(command, enabled=True,
            received_at_ms=60_000, now_ms=60_000, binding=self.binding(),
            known_idea_ids=set(), resumable=False)
        self.assertEqual(expired.failure_code, "session_binding_expired")
        ambiguous = self.router.route(self.request_for({"name": "ambiguous"}),
            "Move beside an idea", self.default_ideas).model_dump(mode="json")
        rejected = MockNavigationCoordinator().accept(ambiguous, enabled=True,
            received_at_ms=1_000, now_ms=1_001, binding=self.binding(),
            known_idea_ids={"idea-solar", "idea-garden"}, resumable=False)
        self.assertEqual(rejected.failure_code, "not_authorized")


if __name__ == "__main__":
    unittest.main()
