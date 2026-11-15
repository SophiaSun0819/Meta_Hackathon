import json
import unittest
from dataclasses import asdict
from pathlib import Path

from orby_navigation_contract_v1_1 import AuthenticatedSessionBinding, NavigationCommand
from orby_quest_navigation_mock import MockQuestNavigationExecutor, QuestNavigationSafetyPolicy

FIXTURES = Path(__file__).parent / "fixtures" / "orby_navigation"


class OrbyQuestNavigationFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.quest = json.loads((FIXTURES / "quest_navigation_cases.json").read_text("utf-8"))
        navigation = json.loads((FIXTURES / "navigation_cases.json").read_text("utf-8"))
        cls.commands = {case["name"]: NavigationCommand.model_validate(
            case["response"]).model_dump(mode="json") for case in navigation}
        cls.binding = AuthenticatedSessionBinding.model_validate(
            cls.quest["authenticated_session_binding"])

    def context(self, case):
        return dict(binding=self.binding,
            received_at_ms=case.get("received_at_ms", 100_000),
            now_ms=case.get("now_ms", 100_001),
            known_idea_ids=set(case.get("known_idea_ids", [])),
            resumable=case.get("resumable", False),
            active_operation_id=case.get("active_operation_id"),
            destination_available=case.get("destination_available", True))

    def test_fixture_inventory_and_quest_safety_limits(self):
        self.assertEqual({case["name"] for case in self.quest["cases"]}, {
            "follow", "approach_known_idea", "home", "reposition", "stop", "resume",
            "interruption", "tracking_loss", "invalid_destination", "stale", "replay"})
        self.assertEqual(asdict(QuestNavigationSafetyPolicy()), self.quest["safety_policy"])

    def test_standard_quest_commands_acknowledge_then_complete(self):
        names = {"follow", "approach_known_idea", "home", "reposition", "stop", "resume"}
        for case in (value for value in self.quest["cases"] if value["name"] in names):
            with self.subTest(case=case["name"]):
                executor = MockQuestNavigationExecutor()
                accepted = executor.accept(self.commands[case["command_fixture"]],
                                             **self.context(case))
                self.assertEqual(accepted.acknowledgement.status, "accepted")
                self.assertEqual(accepted.acknowledgement.operation_id,
                                 case["expected_operation_id"])
                self.assertIsNone(accepted.immediate_result)
                self.assertFalse(executor.autonomous_follow_enabled)
                result = executor.complete(self.binding)
                self.assertEqual(result.status, case["terminal"])
                self.assertEqual(result.failure_code, case["expected_result_failure"])
                self.assertTrue(executor.autonomous_follow_enabled)

    def test_new_explicit_command_interrupts_prior_operation(self):
        case = next(value for value in self.quest["cases"] if value["name"] == "interruption")
        executor = MockQuestNavigationExecutor()
        first = executor.accept(self.commands[case["sequence"][0]],
            binding=self.binding, received_at_ms=100_000, now_ms=100_001,
            known_idea_ids=set())
        self.assertEqual(first.acknowledgement.status, "accepted")
        second = executor.accept(self.commands[case["sequence"][1]],
            binding=self.binding, received_at_ms=100_002, now_ms=100_003,
            known_idea_ids=set())
        self.assertEqual(second.acknowledgement.status, "accepted")
        self.assertEqual(len(second.interrupted_results), 1)
        interrupted = second.interrupted_results[0]
        self.assertEqual(interrupted.operation_id, case["expected_interrupted_operation_id"])
        self.assertEqual(interrupted.failure_code, case["expected_interrupted_failure"])
        self.assertFalse(executor.autonomous_follow_enabled)

    def test_tracking_loss_fails_explicit_motion_before_autonomous_recovery(self):
        case = next(value for value in self.quest["cases"] if value["name"] == "tracking_loss")
        executor = MockQuestNavigationExecutor()
        accepted = executor.accept(self.commands[case["command_fixture"]],
            binding=self.binding, received_at_ms=100_000, now_ms=100_001,
            known_idea_ids=set())
        self.assertEqual(accepted.acknowledgement.status, "accepted")
        result = executor.lose_tracking(self.binding)
        self.assertEqual(result.failure_code, case["expected_result_failure"])
        self.assertFalse(executor.autonomous_follow_enabled)
        executor.restore_tracking()
        self.assertTrue(executor.autonomous_follow_enabled)

    def test_invalid_destination_acknowledges_then_returns_bound_failure(self):
        case = next(value for value in self.quest["cases"]
                    if value["name"] == "invalid_destination")
        executor = MockQuestNavigationExecutor()
        outcome = executor.accept(self.commands[case["command_fixture"]],
                                  **self.context(case))
        self.assertEqual(outcome.acknowledgement.status, "accepted")
        self.assertEqual(outcome.acknowledgement.operation_id, case["expected_operation_id"])
        self.assertIsNotNone(outcome.immediate_result)
        self.assertEqual(outcome.immediate_result.operation_id,
                         outcome.acknowledgement.operation_id)
        self.assertEqual(outcome.immediate_result.failure_code,
                         case["expected_result_failure"])
        self.assertTrue(executor.autonomous_follow_enabled)

    def test_stale_and_replayed_commands_never_move(self):
        stale = next(value for value in self.quest["cases"] if value["name"] == "stale")
        stale_executor = MockQuestNavigationExecutor()
        rejected = stale_executor.accept(self.commands[stale["command_fixture"]],
                                          **self.context(stale))
        self.assertEqual(rejected.acknowledgement.failure_code,
                         stale["expected_ack_failure"])
        self.assertTrue(stale_executor.autonomous_follow_enabled)
        replay = next(value for value in self.quest["cases"] if value["name"] == "replay")
        replay_executor = MockQuestNavigationExecutor()
        first = replay_executor.accept(self.commands[replay["command_fixture"]],
            binding=self.binding, received_at_ms=100_000, now_ms=100_001,
            known_idea_ids=set())
        second = replay_executor.accept(self.commands[replay["command_fixture"]],
            binding=self.binding, received_at_ms=100_002, now_ms=100_003,
            known_idea_ids=set())
        self.assertEqual(first.acknowledgement.status, "accepted")
        self.assertEqual(second.acknowledgement.failure_code,
                         replay["expected_second_ack_failure"])

    def test_speed_and_acceleration_limits_fail_locally(self):
        for field, value in (("requested_speed_mps", 1.21),
                             ("requested_acceleration_mps2", 2.51)):
            with self.subTest(field=field):
                executor = MockQuestNavigationExecutor()
                outcome = executor.accept(self.commands["reposition"], binding=self.binding,
                    received_at_ms=100_000, now_ms=100_001, known_idea_ids=set(),
                    **{field: value})
                self.assertEqual(outcome.acknowledgement.status, "accepted")
                self.assertEqual(outcome.immediate_result.status, "failed")
                self.assertEqual(outcome.immediate_result.failure_code, "movement_failed")


if __name__ == "__main__":
    unittest.main()
