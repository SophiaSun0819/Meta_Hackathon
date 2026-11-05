import asyncio
import os
import sys
import unittest
from pathlib import Path

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient
from pydantic import ValidationError

import app as backend_app
from idea_manager import IdeaRequestRegistry
from intent_router import (ClarificationStore, CompanionEmotion, FollowUpModelOutput,
                           IntentModelOutput, IntentRouter, IntentRouterMalformedOutput)


class FixedIntentProvider:
    def __init__(self, intent, targets=None, scale=None, direction=None, question=None,
                 response="Understood.", emotion="attentive", pending_action=None,
                 follow_up=None):
        self.intent = intent
        self.targets = targets or []
        self.scale = scale
        self.direction = direction
        self.question = question
        self.response = response
        self.emotion = emotion
        self.pending_action = pending_action
        self.follow_up = follow_up
        self.calls = 0

    async def classify(self, request):
        self.calls += 1
        return IntentModelOutput(
            intent=self.intent,
            pending_action=self.pending_action,
            target_idea_ids=self.targets,
            scale=self.scale,
            direction=self.direction,
            question=self.question,
            response_text=self.response,
            emotion=self.emotion,
        )

    async def resolve_follow_up(self, request, pending):
        self.calls += 1
        if self.follow_up is None:
            return FollowUpModelOutput(resolution="ambiguous", selected_idea_ids=[],
                                       response_text="Please clarify.", emotion="confused")
        return FollowUpModelOutput(**self.follow_up)


class TimeoutProvider:
    async def classify(self, request):
        await asyncio.sleep(0.1)


class MalformedProvider:
    async def classify(self, request):
        raise IntentRouterMalformedOutput("mock malformed output")


class InvalidResizeProvider:
    async def classify(self, request):
        try:
            return IntentModelOutput.model_validate({
                "intent": "resize_bubble",
                "target_idea_ids": ["idea-one"],
                "scale": "gigantic",
                "direction": None,
                "question": None,
                "response_text": "Making it gigantic.",
                "emotion": "attentive",
            })
        except ValidationError as exc:
            raise IntentRouterMalformedOutput("invalid resize enum") from exc


class IntentRouterEndpointTests(unittest.TestCase):
    token = "test-development-token"

    def setUp(self):
        backend_app.DEV_TOKEN = self.token
        backend_app.intent_request_registry = IdeaRequestRegistry()
        backend_app.intent_request_times.clear()
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        self.client.close()

    def payload(self, request_id, transcript="Please work with my idea."):
        return {
            "request_id": request_id,
            "session_id": "Session 1",
            "raw_transcript": transcript,
            "cleaned_transcript": None,
            "transcript_source": "raw",
            "available_ideas": [
                {"idea_id": "idea-one", "title": "Solar Hub", "summary": "A solar study hub."},
                {"idea_id": "idea-two", "title": "Workshop", "summary": "Peer-led workshops."},
            ],
        }

    def post(self, payload, token=None):
        return self.client.post(
            "/api/intents/route", json=payload,
            headers={"X-ThinkingSpace-Token": token if token is not None else self.token},
        )

    def route_with(self, provider, request_id, transcript="Please work with my idea."):
        backend_app.intent_router = IntentRouter(provider, timeout_seconds=1.0,
                                                  clarification_store=ClarificationStore())
        return self.post(self.payload(request_id, transcript))

    def begin_clarification(self, provider, request_id="clarify-start"):
        backend_app.intent_router = IntentRouter(provider, timeout_seconds=1.0,
                                                  clarification_store=ClarificationStore())
        return self.post(self.payload(request_id, "Make that bigger."))

    def test_all_supported_intents(self):
        cases = [
            ("create_idea", [], None, None, None),
            ("expand_idea", ["idea-one"], None, None, None),
            ("challenge_idea", ["idea-one"], None, None, None),
            ("combine_ideas", ["idea-one", "idea-two"], None, None, None),
            ("show_ideas", [], None, None, None),
            ("resize_bubble", ["idea-one"], "larger", None, None),
            ("move_bubble", ["idea-one"], None, "left", None),
            ("hide_bubble", ["idea-one"], None, None, None),
            ("converse", [], None, None, "How could this help students?"),
            ("clarification_required", [], None, None, None),
        ]
        for index, (intent, targets, scale, direction, question) in enumerate(cases):
            with self.subTest(intent=intent):
                provider = FixedIntentProvider(
                    intent, targets, scale, direction, question,
                    response="Matching companion response.",
                    emotion="confused" if intent == "clarification_required" else "attentive")
                response = self.route_with(provider, f"all-{index}")
                self.assertEqual(response.status_code, 200, response.text)
                data = response.json()
                self.assertEqual(data["intent"], intent)
                self.assertEqual(data["spoken_response"], data["subtitle_text"])
                self.assertIn(data["suggested_emotion"], [value.value for value in CompanionEmotion])

    def test_ordinary_brainstorming_statement_routes_to_create(self):
        response = self.route_with(
            FixedIntentProvider("create_idea", response="That sounds like a new idea."),
            "ordinary-statement", "A quiet solar-powered study garden for the community.")
        self.assertEqual(response.json()["intent"], "create_idea")

    def test_direct_spatial_command_uses_semantic_direction(self):
        response = self.route_with(
            FixedIntentProvider("move_bubble", ["idea-one"], direction="closer"),
            "direct-move", "Move the solar hub idea closer.")
        data = response.json()
        self.assertEqual(data["parameters"]["direction"], "closer")
        self.assertNotIn("position", data["parameters"])

    def test_ambiguous_reference_requires_clarification(self):
        response = self.route_with(
            FixedIntentProvider("expand_idea", [], response="Which idea should I expand?"),
            "ambiguous-reference", "Expand that one.")
        self.assertEqual(response.json()["intent"], "clarification_required")
        self.assertEqual(response.json()["suggested_emotion"], "confused")
        self.assertTrue(response.json()["requires_confirmation"])
        self.assertTrue(response.json()["clarification_id"].startswith("clar_"))

    def test_unknown_idea_id_requires_clarification(self):
        response = self.route_with(
            FixedIntentProvider("hide_bubble", ["idea-invented"]),
            "unknown-id", "Hide the imaginary idea.")
        self.assertEqual(response.json()["intent"], "clarification_required")
        self.assertEqual(response.json()["target_idea_ids"], [])

    def test_invalid_resize_value_is_rejected(self):
        response = self.route_with(InvalidResizeProvider(), "invalid-resize")
        self.assertEqual(response.status_code, 502)
        self.assertEqual(response.json()["error"], "Intent router returned an invalid structured response.")

    def test_malformed_model_output(self):
        response = self.route_with(MalformedProvider(), "malformed-intent")
        self.assertEqual(response.status_code, 502)

    def test_timeout(self):
        backend_app.intent_router = IntentRouter(TimeoutProvider(), timeout_seconds=0.01)
        response = self.post(self.payload("intent-timeout"))
        self.assertEqual(response.status_code, 504)
        self.assertEqual(response.json()["error"], "Intent router timed out.")

    def test_authentication_failure(self):
        provider = FixedIntentProvider("create_idea")
        backend_app.intent_router = IntentRouter(provider, timeout_seconds=1.0)
        response = self.post(self.payload("intent-auth"), token="wrong")
        self.assertEqual(response.status_code, 401)
        self.assertEqual(provider.calls, 0)

    def test_duplicate_request(self):
        provider = FixedIntentProvider("create_idea")
        backend_app.intent_router = IntentRouter(provider, timeout_seconds=1.0)
        payload = self.payload("intent-duplicate")
        first = self.post(payload)
        second = self.post(payload)
        self.assertEqual(first.status_code, 200)
        self.assertEqual(second.status_code, 409)
        self.assertEqual(second.json()["error"], "Duplicate intent request.")

    def test_natural_language_synonyms_route_without_confirmation(self):
        cases = [("Grow this idea", "expand_idea", ["idea-one"]),
                 ("Make this bubble bigger", "resize_bubble", ["idea-one"]),
                 ("What else could we do with this?", "expand_idea", ["idea-one"]),
                 ("Combine my first and third ideas", "combine_ideas", ["idea-one", "idea-three"])]
        for index, (phrase, intent, targets) in enumerate(cases):
            provider = FixedIntentProvider(intent, targets,
                scale="larger" if intent == "resize_bubble" else None)
            payload = self.payload(f"synonym-{index}", phrase)
            payload["available_ideas"].append(
                {"idea_id": "idea-three", "title": "Garden", "summary": "A community garden."})
            backend_app.intent_router = IntentRouter(provider, timeout_seconds=1.0,
                                                      clarification_store=ClarificationStore())
            data = self.post(payload).json()
            self.assertEqual(data["action"], intent)
            self.assertEqual(data["target_idea_ids"], targets)
            self.assertFalse(data["requires_confirmation"])

    def test_yes_does_not_guess_between_multiple_single_target_candidates(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one", "idea-two"],
            response="Which idea?", pending_action="expand_idea",
            follow_up={"resolution": "confirm", "selected_idea_ids": [],
                       "response_text": "Please choose one idea.", "emotion": "confused"})
        start = self.begin_clarification(provider, "ambiguous-yes-start").json()
        payload = self.payload("ambiguous-yes-answer", "Yes")
        payload["clarification_id"] = start["clarification_id"]
        data = self.post(payload).json()
        self.assertEqual(data["intent"], "clarification_required")
        self.assertEqual(data["target_idea_ids"], [])
        self.assertTrue(data["requires_confirmation"])

    def test_first_and_second_candidate_follow_ups_resolve_pending_action(self):
        for ordinal, idea_id in [("first", "idea-one"), ("second", "idea-two")]:
            provider = FixedIntentProvider("clarification_required", ["idea-one", "idea-two"],
                response="Which bubble?", pending_action="expand_idea",
                follow_up={"resolution": "select", "selected_idea_ids": [idea_id],
                           "response_text": "I'll expand that idea.", "emotion": "attentive"})
            start = self.begin_clarification(provider, f"clarify-start-{ordinal}").json()
            payload = self.payload(f"clarify-{ordinal}", f"The {ordinal} one.")
            payload["clarification_id"] = start["clarification_id"]
            data = self.post(payload).json()
            self.assertEqual(data["action"], "expand_idea")
            self.assertEqual(data["target_idea_ids"], [idea_id])
            self.assertFalse(data["requires_confirmation"])

    def test_yes_confirms_single_candidate_and_cancel_does_not_execute(self):
        for label, transcript, resolution, expected in [
                ("yes", "Yes", "confirm", "hide_bubble"),
                ("no", "No", "cancel", "cancel"),
                ("cancel", "Cancel", "cancel", "cancel")]:
            provider = FixedIntentProvider("clarification_required", ["idea-one"],
                response="Do you mean Solar Hub?", pending_action="hide_bubble",
                follow_up={"resolution": resolution, "selected_idea_ids": [],
                           "response_text": "Okay.", "emotion": "attentive"})
            start = self.begin_clarification(provider, f"start-{label}").json()
            payload = self.payload(f"answer-{label}", transcript)
            payload["clarification_id"] = start["clarification_id"]
            self.assertEqual(self.post(payload).json()["action"], expected)

    def test_two_ambiguous_answers_cancel_pending_command(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one", "idea-two"],
            response="Which one?", pending_action="expand_idea")
        start = self.begin_clarification(provider).json()
        clarification_id = start["clarification_id"]
        for index in range(2):
            payload = self.payload(f"unclear-{index}", "The thing over there.")
            payload["clarification_id"] = clarification_id
            result = self.post(payload).json()
        self.assertEqual(result["action"], "cancel")
        self.assertFalse(result["requires_confirmation"])

    def test_clarification_is_session_scoped(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one"],
            pending_action="expand_idea", response="Which one?")
        start = self.begin_clarification(provider).json()
        payload = self.payload("wrong-session", "Yes")
        payload["session_id"] = "Session 2"
        payload["clarification_id"] = start["clarification_id"]
        data = self.post(payload).json()
        self.assertEqual(data["status"], "error")
        self.assertEqual(data["action"], "")

    def test_expired_clarification_cannot_execute(self):
        store = ClarificationStore(expiration_seconds=0)
        provider = FixedIntentProvider("clarification_required", ["idea-one"],
            pending_action="expand_idea", response="Which one?")
        backend_app.intent_router = IntentRouter(provider, 1.0, store)
        start = self.post(self.payload("expire-start")).json()
        payload = self.payload("expire-answer", "Yes")
        payload["clarification_id"] = start["clarification_id"]
        data = self.post(payload).json()
        self.assertEqual(data["status"], "error")
        self.assertIn("expired", data["error"])

    def test_selected_id_must_be_a_pending_candidate(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one"],
            pending_action="expand_idea", response="Which one?",
            follow_up={"resolution": "select", "selected_idea_ids": ["idea-two"],
                       "response_text": "I am not sure.", "emotion": "confused"})
        start = self.begin_clarification(provider).json()
        payload = self.payload("invalid-selection", "The other one")
        payload["clarification_id"] = start["clarification_id"]
        data = self.post(payload).json()
        self.assertEqual(data["intent"], "clarification_required")
        self.assertTrue(data["requires_confirmation"])

    def test_combine_result_can_be_challenged_as_most_recent_combined_idea(self):
        combine = self.route_with(FixedIntentProvider("combine_ideas", ["idea-one", "idea-two"]),
                                  "combine-before-challenge", "Combine these ideas.")
        self.assertEqual(combine.json()["action"], "combine_ideas")
        payload = self.payload("challenge-combined", "Challenge the combined idea.")
        payload["available_ideas"].append({"idea_id": "idea-combined", "title": "Solar Workshop",
            "summary": "Most recently created idea. Combined idea derived from multiple ideas."})
        backend_app.intent_router = IntentRouter(
            FixedIntentProvider("challenge_idea", ["idea-combined"]), 1.0, ClarificationStore())
        data = self.post(payload).json()
        self.assertEqual(data["action"], "challenge_idea")
        self.assertEqual(data["target_idea_ids"], ["idea-combined"])

    def test_ambiguous_challenge_resolves_with_named_idea(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one", "idea-two"],
            response="Which idea should I challenge?", pending_action="challenge_idea",
            follow_up={"resolution":"select", "selected_idea_ids":["idea-one"],
                       "response_text":"I'll challenge Solar Hub.", "emotion":"attentive"})
        start = self.begin_clarification(provider, "challenge-ambiguous").json()
        payload = self.payload("challenge-named", "The Solar Hub")
        payload["clarification_id"] = start["clarification_id"]
        data = self.post(payload).json()
        self.assertEqual(data["action"], "challenge_idea")
        self.assertEqual(data["target_idea_ids"], ["idea-one"])

    def test_cancel_then_fresh_command_is_not_treated_as_follow_up(self):
        provider = FixedIntentProvider("clarification_required", ["idea-one"],
            pending_action="challenge_idea", response="Do you mean Solar Hub?",
            follow_up={"resolution":"cancel", "selected_idea_ids":[],
                       "response_text":"Cancelled.", "emotion":"neutral"})
        start = self.begin_clarification(provider, "cancel-restart-start").json()
        cancel = self.payload("cancel-restart-cancel", "Cancel")
        cancel["clarification_id"] = start["clarification_id"]
        self.assertEqual(self.post(cancel).json()["action"], "cancel")
        backend_app.intent_router.provider = FixedIntentProvider("create_idea")
        fresh = self.post(self.payload("cancel-restart-fresh", "A new community garden idea.")).json()
        self.assertEqual(fresh["action"], "create_idea")

    def test_expired_clarification_does_not_block_new_request(self):
        store = ClarificationStore(expiration_seconds=0)
        backend_app.intent_router = IntentRouter(FixedIntentProvider("clarification_required", ["idea-one"],
            pending_action="challenge_idea", response="Which idea?"), 1.0, store)
        self.post(self.payload("expired-then-fresh-start"))
        backend_app.intent_router.provider = FixedIntentProvider("create_idea")
        data = self.post(self.payload("expired-then-fresh-new", "A fresh idea.")).json()
        self.assertEqual(data["action"], "create_idea")


if __name__ == "__main__":
    unittest.main()
