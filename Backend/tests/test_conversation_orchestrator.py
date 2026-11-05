import asyncio
import json
import os
import sys
import unittest
from pathlib import Path

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app
from conversation_orchestrator import (ConversationClarificationStore, ConversationMalformedOutput,
    ConversationModelOutput, ConversationOrchestrator)
from idea_manager import IdeaRequestRegistry


class FixedConversationProvider:
    def __init__(self, category="general_conversation", action="converse", targets=None,
                 response="Here is a helpful response.", create=False, execute=False,
                 confirmation=False, question=None, candidates=None, scale=None, direction=None):
        self.values = dict(intent_category=category, proposed_action=action,
            target_idea_ids=targets or [], parameters={"scale": scale, "direction": direction,
                                                       "settings_section": None},
            spoken_response=response, suggested_emotion="attentive",
            requires_confirmation=confirmation, clarification_question=question,
            candidate_idea_ids=candidates or [], should_create_idea=create,
            should_execute_action=execute)
        self.calls = 0

    async def respond(self, request):
        self.calls += 1
        return ConversationModelOutput(**self.values)


class TimeoutProvider:
    async def respond(self, request):
        await asyncio.sleep(.1)


class MalformedProvider:
    async def respond(self, request):
        raise ConversationMalformedOutput("mock malformed")


class ConversationEndpointTests(unittest.TestCase):
    token = "test-development-token"

    def setUp(self):
        backend_app.DEV_TOKEN = self.token
        backend_app.conversation_request_registry = IdeaRequestRegistry()
        backend_app.conversation_request_times.clear()
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        self.client.close()

    def payload(self, request_id, transcript="What is virtual reality?", session="Session 1"):
        return {"request_id": request_id, "session_id": session,
            "raw_transcript": transcript, "cleaned_transcript": None,
            "transcript_source": "raw", "available_ideas": [
                {"idea_id": "idea-one", "title": "Anatomy Lab", "summary": "A VR anatomy lab."},
                {"idea_id": "idea-two", "title": "Quiz Garden", "summary": "A quiz-based garden."}],
            "context": {"recent_turns": [], "most_recent_idea_id": "idea-one",
                        "current_mode": "conversational"}, "clarification_id": None}

    def post(self, payload, token=None):
        return self.client.post("/api/conversations/process", json=payload,
            headers={"X-ThinkingSpace-Token": self.token if token is None else token})

    def post_exact_json(self, body, token=None):
        return self.client.post("/api/conversations/process", content=body.encode("utf-8"),
            headers={"Content-Type": "application/json",
                     "X-ThinkingSpace-Token": self.token if token is None else token})

    def use(self, provider, timeout=1):
        backend_app.conversation_orchestrator = ConversationOrchestrator(provider, timeout,
            ConversationClarificationStore(expiration_seconds=120, maximum_attempts=2))

    def test_general_conversation_and_brainstorming_do_not_create_ideas(self):
        for index, (category, action) in enumerate([
                ("general_conversation", "converse"),
                ("brainstorming_suggestions", "brainstorm_suggestions")]):
            self.use(FixedConversationProvider(category, action, create=True))
            data = self.post(self.payload(f"conversation-{index}")).json()
            self.assertFalse(data["should_create_idea"])
            self.assertEqual(data["spoken_response"], data["subtitle_text"])

    def assert_clear_idea_proposal(self, data):
        self.assertEqual(data["contract_version"], "1.0")
        self.assertEqual(data["intent_category"], "clear_new_idea")
        self.assertEqual(data["proposed_action"], "create_idea")
        self.assertFalse(data["requires_confirmation"])
        self.assertTrue(data["should_create_idea"])
        self.assertFalse(data["should_execute_action"])
        self.assertIsNone(data["clarification_id"])
        self.assertIsNone(data["clarification_question"])

    def test_clear_new_idea_creates_without_confirmation(self):
        self.use(FixedConversationProvider("clear_new_idea", "clarification_required",
            create=False, confirmation=True, question="What age group is this for?",
            response="Would you like me to save that?"))
        data = self.post(self.payload(
            "clear-idea", "I want to create a VR game that teaches students about human anatomy.")).json()
        self.assert_clear_idea_proposal(data)

    def test_clear_idea_with_missing_optional_details_still_creates(self):
        self.use(FixedConversationProvider("clear_new_idea", "clarification_required",
            confirmation=True, question="What learning objectives should it include?"))
        data = self.post(self.payload(
            "clear-without-options", "Let's make a card game about mythical creatures.")).json()
        self.assert_clear_idea_proposal(data)

    def test_explicit_save_request_creates_without_confirmation(self):
        self.use(FixedConversationProvider("clear_new_idea", "create_idea",
            create=False, confirmation=True, question="Should I save it?"))
        data = self.post(self.payload(
            "explicit-save", "Save my idea for a solar-powered community study space.")).json()
        self.assert_clear_idea_proposal(data)

    def test_ambiguous_potential_idea_requires_confirmation(self):
        self.use(FixedConversationProvider("ambiguous_potential_idea", "create_idea",
            create=True, confirmation=True, question="Should I save that as an idea?"))
        data = self.post(self.payload("ambiguous-idea", "Maybe quizzes could be useful.")).json()
        self.assertTrue(data["requires_confirmation"])
        self.assertFalse(data["should_create_idea"])
        self.assertTrue(data["clarification_id"].startswith("clar_"))

    def test_brainstorming_and_general_questions_remain_conversational(self):
        cases = [
            ("brainstorming_suggestions", "brainstorm_suggestions",
             "Give me ideas for an educational game."),
            ("general_conversation", "converse", "Hey Orby, what is virtual reality?"),
        ]
        for index, (category, action, transcript) in enumerate(cases):
            self.use(FixedConversationProvider(category, action, create=True, confirmation=True))
            data = self.post(self.payload(f"non-idea-{index}", transcript)).json()
            self.assertEqual(data["proposed_action"], action)
            self.assertFalse(data["should_create_idea"])
            self.assertFalse(data["requires_confirmation"])
            self.assertIsNone(data["clarification_id"])

    def test_challenge_and_multiturn_follow_up_never_create_bubbles(self):
        provider = FixedConversationProvider("conversational_idea_challenge",
            "conversational_challenge", targets=["idea-one"], create=True,
            response="The main risk is cognitive overload.")
        self.use(provider)
        first = self.post(self.payload("challenge", "Challenge my VR anatomy idea.")).json()
        self.assertFalse(first["should_create_idea"])
        provider.values["intent_category"] = "contextual_follow_up"
        provider.values["spoken_response"] = "Quizzes could help if feedback stays concise."
        follow = self.payload("challenge-follow", "Would adding quizzes help?")
        follow["context"]["recent_turns"] = [
            {"role": "user", "text": "Challenge my idea.", "related_idea_ids": ["idea-one"]},
            {"role": "orby", "text": "The risk is overload.", "related_idea_ids": ["idea-one"]}]
        second = self.post(follow).json()
        self.assertFalse(second["should_create_idea"])
        self.assertEqual(second["proposed_action"], "conversational_challenge")

    def test_expand_combine_spatial_and_session_commands_are_semantic(self):
        cases = [
            ("idea_modification", "expand_idea", ["idea-one"], False, None, None),
            ("idea_modification", "combine_ideas", ["idea-one", "idea-two"], False, None, None),
            ("spatial_command", "move_bubble", ["idea-one"], True, None, "closer"),
            ("application_command", "open_menu", [], True, None, None),
        ]
        for index, (category, action, targets, execute, scale, direction) in enumerate(cases):
            self.use(FixedConversationProvider(category, action, targets=targets,
                execute=execute, scale=scale, direction=direction))
            data = self.post(self.payload(f"command-{index}")).json()
            self.assertEqual(data["proposed_action"], action)
            self.assertEqual(data["should_execute_action"], execute)
            self.assertNotIn("position", data["parameters"])

    def test_destructive_command_requires_confirmation_and_cannot_execute(self):
        self.use(FixedConversationProvider("application_command", "end_session", execute=True))
        data = self.post(self.payload("end-session", "End this session.")).json()
        self.assertTrue(data["requires_confirmation"])
        self.assertFalse(data["should_execute_action"])

    def test_unknown_idea_reference_is_rejected(self):
        self.use(FixedConversationProvider("spatial_command", "move_bubble",
                                          targets=["invented"], execute=True))
        response = self.post(self.payload("unknown-reference"))
        self.assertEqual(response.status_code, 502)

    def test_exact_challenge_json_validates_directly_and_at_endpoint(self):
        payload = self.payload("challenge-exact", "Challenge the Solar Hub idea.")
        payload["available_ideas"] = [
            {"idea_id": "idea-solar", "title": "Solar Hub",
             "summary": "A solar-powered community study space."}]
        payload["context"]["most_recent_idea_id"] = "idea-solar"
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
        direct = backend_app.ConversationRequest.model_validate_json(body)
        self.assertEqual(direct.available_ideas[0].idea_id, "idea-solar")
        self.use(FixedConversationProvider("conversational_idea_challenge",
            "conversational_challenge", targets=["idea-solar"]))
        response = self.post_exact_json(body)
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["proposed_action"], "conversational_challenge")

    def test_idea_inventory_shapes_and_optional_most_recent_id(self):
        cases = [
            ("empty", [], None),
            ("one", [{"idea_id": "idea-one", "title": "One", "summary": "First idea."}],
             "idea-one"),
            ("multiple", [
                {"idea_id": "idea-one", "title": "One", "summary": "First idea."},
                {"idea_id": "idea-two", "title": "Two", "summary": "Second idea."}], None),
        ]
        for suffix, ideas, recent in cases:
            payload = self.payload(f"inventory-{suffix}", "Challenge this idea.")
            payload["available_ideas"] = ideas
            payload["context"]["most_recent_idea_id"] = recent
            targets = [recent] if recent else []
            self.use(FixedConversationProvider("conversational_idea_challenge",
                "conversational_challenge", targets=targets))
            response = self.post_exact_json(json.dumps(payload))
            self.assertEqual(response.status_code, 200, response.text)

    def test_invalid_extra_idea_field_reports_safe_validation_metadata(self):
        payload = self.payload("extra-field", "Challenge the Solar Hub idea.")
        payload["available_ideas"] = [{"idea_id": "idea-solar", "title": "Solar Hub",
            "summary": "A study space.", "category": "Education"}]
        payload["context"]["most_recent_idea_id"] = "idea-solar"
        response = self.post_exact_json(json.dumps(payload))
        self.assertEqual(response.status_code, 422)
        self.assertEqual(response.json()["error"], "Conversation request validation failed.")
        self.assertEqual(response.headers["X-ThinkingSpace-Validation-Stage"], "request_schema")
        self.assertEqual(response.headers["X-ThinkingSpace-Validation-Field"],
                         "available_ideas.0.category")
        self.assertEqual(response.headers["X-ThinkingSpace-Validation-Category"],
                         "extra_forbidden")
        self.assertEqual(response.headers["X-ThinkingSpace-Validation-Status"], "422")

    def test_session_isolation_for_pending_confirmation(self):
        self.use(FixedConversationProvider("ambiguous_potential_idea", "create_idea",
            confirmation=True, question="Save it?"))
        started = self.post(self.payload("isolation-start")).json()
        follow = self.payload("isolation-follow", "Yes", "Session 2")
        follow["clarification_id"] = started["clarification_id"]
        self.assertEqual(self.post(follow).status_code, 409)

    def test_confirmed_follow_up_completes_pending_new_idea(self):
        provider = FixedConversationProvider("ambiguous_potential_idea", "create_idea",
            confirmation=True, question="Should I save that idea?")
        self.use(provider)
        started = self.post(self.payload("confirm-start", "Maybe we could add quizzes.")).json()
        provider.values.update(intent_category="clear_new_idea", proposed_action="create_idea",
            requires_confirmation=False, clarification_question=None, should_create_idea=True,
            spoken_response="Yes, I'll save the quiz idea.")
        follow = self.payload("confirm-answer", "Yes")
        follow["clarification_id"] = started["clarification_id"]
        result = self.post(follow).json()
        self.assertTrue(result["should_create_idea"])
        self.assertFalse(result["requires_confirmation"])
        self.assertIsNone(result["clarification_id"])

    def test_expired_conversation_clarification_cannot_execute(self):
        provider = FixedConversationProvider("ambiguous_potential_idea", "create_idea",
            confirmation=True, question="Save it?")
        backend_app.conversation_orchestrator = ConversationOrchestrator(provider, 1,
            ConversationClarificationStore(expiration_seconds=0, maximum_attempts=2))
        started = self.post(self.payload("expired-start")).json()
        follow = self.payload("expired-follow", "Yes")
        follow["clarification_id"] = started["clarification_id"]
        self.assertEqual(self.post(follow).status_code, 409)

    def test_authentication_duplicate_timeout_and_malformed_output(self):
        provider = FixedConversationProvider()
        self.use(provider)
        self.assertEqual(self.post(self.payload("auth"), "wrong").status_code, 401)
        payload = self.payload("duplicate")
        self.assertEqual(self.post(payload).status_code, 200)
        self.assertEqual(self.post(payload).status_code, 409)
        self.use(TimeoutProvider(), timeout=.01)
        self.assertEqual(self.post(self.payload("timeout")).status_code, 504)
        self.use(MalformedProvider())
        self.assertEqual(self.post(self.payload("malformed")).status_code, 502)

    def test_conversation_size_and_rate_limits_are_independent(self):
        self.use(FixedConversationProvider())
        old_bytes = backend_app.MAX_CONVERSATION_REQUEST_BYTES
        old_rate = backend_app.MAX_CONVERSATION_REQUESTS_PER_MINUTE
        try:
            backend_app.MAX_CONVERSATION_REQUEST_BYTES = 32
            self.assertEqual(self.post(self.payload("oversized")).status_code, 413)
            backend_app.MAX_CONVERSATION_REQUEST_BYTES = old_bytes
            backend_app.MAX_CONVERSATION_REQUESTS_PER_MINUTE = 1
            backend_app.conversation_request_times.clear()
            self.assertEqual(self.post(self.payload("rate-first")).status_code, 200)
            self.assertEqual(self.post(self.payload("rate-second")).status_code, 429)
        finally:
            backend_app.MAX_CONVERSATION_REQUEST_BYTES = old_bytes
            backend_app.MAX_CONVERSATION_REQUESTS_PER_MINUTE = old_rate
            backend_app.conversation_request_times.clear()


if __name__ == "__main__":
    unittest.main()
