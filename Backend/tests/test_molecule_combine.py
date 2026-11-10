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
from conversation_orchestrator import (ConversationClarificationStore, ConversationModelOutput,
    ConversationOrchestrator, ConversationRequest)
from idea_manager import (IdeaAction, IdeaManager, IdeaModelOutput, IdeaProcessRequest,
    IdeaRequestRegistry, MAX_COMBINE_SOURCES, OpenAIIdeaProvider)
from mock_providers import MockIdeaProvider

TOKEN = "test-development-token"
IDEAS = [
    {"idea_id": "idea-a", "title": "Anatomy Lab", "summary": "A VR anatomy lab for students."},
    {"idea_id": "idea-b", "title": "Chemistry Game", "summary": "An AR game about mixing elements."},
    {"idea_id": "idea-c", "title": "Cooking Coach", "summary": "Recipe steps floating above the stove."},
    {"idea_id": "idea-d", "title": "Rocket Garden", "summary": "Growing vegetables on Mars."},
    {"idea_id": "center", "title": "Science Kitchen", "summary": "Learn chemistry and anatomy by cooking."},
]


class RecordingIdeaProvider:
    def __init__(self):
        self.requests = []

    async def generate(self, request):
        self.requests.append(request)
        return IdeaModelOutput(title="Molecule Idea", summary="A combined idea from the listed sources.",
                               category="General", spoken_response="Here's the combined idea.")


class FixedCombineProvider:
    def __init__(self, targets):
        self.targets = targets

    async def respond(self, request):
        return ConversationModelOutput(
            intent_category="idea_modification", proposed_action="combine_ideas",
            target_idea_ids=self.targets, authorization="authorized", response_mode="action_proposal",
            idea_relationship="combination", spoken_response="Those could work together.",
            suggested_emotion="excited")


def combine_payload(request_id, sources, center=None, **overrides):
    value = {"request_id": request_id, "session_id": "Session 1", "action": "combine_ideas",
             "raw_transcript": "Combine these ideas.", "related_idea_ids": sources,
             "existing_ideas": IDEAS}
    if center is not None:
        value["center_idea_id"] = center
    value.update(overrides)
    return value


class MoleculeRequestValidationTests(unittest.TestCase):
    def assert_invalid(self, payload, message):
        with self.assertRaises(ValueError) as caught:
            IdeaProcessRequest.model_validate(payload)
        self.assertIn(message, str(caught.exception))

    def test_at_most_three_sources(self):
        self.assertEqual(MAX_COMBINE_SOURCES, 3)
        IdeaProcessRequest.model_validate(combine_payload("three", ["idea-a", "idea-b", "idea-c"]))
        self.assert_invalid(combine_payload("four", ["idea-a", "idea-b", "idea-c", "idea-d"]),
                            "at most 3 source ideas")

    def test_center_only_for_combine(self):
        self.assert_invalid(combine_payload("expand-center", [], center="center", action="expand_idea",
                                            idea_id="idea-a"),
                            "only valid for combine_ideas")

    def test_center_cannot_be_a_source(self):
        self.assert_invalid(combine_payload("self-source", ["idea-a", "center"], center="center"),
                            "cannot also be a source")

    def test_center_needs_its_context(self):
        ideas_without_center = [idea for idea in IDEAS if idea["idea_id"] != "center"]
        self.assert_invalid(combine_payload("no-context", ["idea-a", "idea-b"], center="center",
                                            existing_ideas=ideas_without_center),
                            "requires its current context")

    def test_update_prompt_includes_current_center_and_revision_guidance(self):
        request = IdeaProcessRequest.model_validate(
            combine_payload("prompt", ["idea-a", "idea-b", "idea-c"], center="center"))
        payload = json.loads(OpenAIIdeaProvider._user_prompt(request).split("\n", 1)[1])
        self.assertEqual(payload["current_combined_idea"]["title"], "Science Kitchen")
        self.assertIn("Revise current_combined_idea",
                      OpenAIIdeaProvider._system_prompt(IdeaAction.COMBINE, updating_center=True))

    def test_new_combination_prompt_is_unchanged(self):
        request = IdeaProcessRequest.model_validate(combine_payload("new", ["idea-a", "idea-b"]))
        self.assertNotIn("current_combined_idea", OpenAIIdeaProvider._user_prompt(request))
        self.assertIn("Create one new idea that clearly combines",
                      OpenAIIdeaProvider._system_prompt(IdeaAction.COMBINE))


class MoleculeEndpointTests(unittest.TestCase):
    def setUp(self):
        backend_app.DEV_TOKEN = TOKEN
        backend_app.idea_request_registry = IdeaRequestRegistry()
        backend_app.idea_request_times.clear()
        self.original_manager = backend_app.idea_manager
        self.provider = RecordingIdeaProvider()
        backend_app.idea_manager = IdeaManager(self.provider, timeout_seconds=1.0)
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        backend_app.idea_manager = self.original_manager
        self.client.close()

    def post(self, payload):
        return self.client.post("/api/ideas/process", json=payload,
                                headers={"X-ThinkingSpace-Token": TOKEN})

    def test_new_molecule_gets_a_new_center(self):
        response = self.post(combine_payload("new-molecule", ["idea-a", "idea-b"]))
        self.assertEqual(response.status_code, 200, response.text)
        data = response.json()
        self.assertTrue(data["idea_id"].startswith("idea_"))
        self.assertFalse(data["updated_existing_idea"])
        self.assertEqual(data["related_idea_ids"], ["idea-a", "idea-b"])

    def test_adding_a_third_source_updates_the_same_center(self):
        response = self.post(combine_payload("add-source", ["idea-a", "idea-b", "idea-c"], center="center"))
        self.assertEqual(response.status_code, 200, response.text)
        data = response.json()
        self.assertEqual(data["idea_id"], "center")
        self.assertTrue(data["updated_existing_idea"])
        self.assertEqual(data["related_idea_ids"], ["idea-a", "idea-b", "idea-c"])
        self.assertEqual(self.provider.requests[0].center_idea.title, "Science Kitchen")

    def test_pulling_a_source_out_updates_with_the_remaining_two(self):
        response = self.post(combine_payload("pull-source", ["idea-a", "idea-c"], center="center"))
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["idea_id"], "center")
        self.assertEqual(response.json()["related_idea_ids"], ["idea-a", "idea-c"])

    def test_fourth_source_is_rejected_before_the_provider(self):
        response = self.post(combine_payload("too-many", ["idea-a", "idea-b", "idea-c", "idea-d"],
                                             center="center"))
        self.assertEqual(response.status_code, 422)
        self.assertEqual(self.provider.requests, [])

    def test_mock_provider_updates_the_center(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider(), timeout_seconds=1.0)
        response = self.post(combine_payload("mock-update", ["idea-a", "idea-b", "idea-c"], center="center"))
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["idea_id"], "center")
        self.assertEqual(response.json()["title"], "Anatomy Lab + Chemistry Game + Cooking Coach")


class VoiceCombineLimitTests(unittest.TestCase):
    def process(self, targets):
        orchestrator = ConversationOrchestrator(FixedCombineProvider(targets), timeout_seconds=1.0,
                                                clarification_store=ConversationClarificationStore())
        request = ConversationRequest.model_validate({
            "request_id": "voice-combine", "session_id": "Session 1",
            "raw_transcript": "Combine all of these ideas.", "available_ideas": IDEAS})
        return asyncio.run(orchestrator.process(request))

    def test_three_targets_proceed(self):
        result = self.process(["idea-a", "idea-b", "idea-c"])
        self.assertEqual(result.proposed_action, "combine_ideas")
        self.assertFalse(result.requires_confirmation)

    def test_four_targets_ask_which_three(self):
        result = self.process(["idea-a", "idea-b", "idea-c", "idea-d"])
        self.assertEqual(result.proposed_action, "combine_ideas")
        self.assertTrue(result.requires_confirmation)
        self.assertFalse(result.should_execute_action)
        self.assertIn("up to 3 ideas", result.clarification_question)
        self.assertEqual(result.candidate_idea_ids, ["idea-a", "idea-b", "idea-c", "idea-d"])
        self.assertTrue(result.clarification_id)


if __name__ == "__main__":
    unittest.main()
