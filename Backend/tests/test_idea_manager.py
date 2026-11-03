import asyncio
import os
import sys
import unittest
from pathlib import Path

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app
from idea_manager import (IdeaManager, IdeaManagerMalformedOutput, IdeaModelOutput,
                          IdeaRequestRegistry)


class MockIdeaProvider:
    def __init__(self, mode: str = "valid"):
        self.mode = mode
        self.calls = 0

    async def generate(self, request):
        self.calls += 1
        if self.mode == "timeout":
            await asyncio.sleep(0.1)
        if self.mode == "malformed":
            raise IdeaManagerMalformedOutput("mock malformed output")
        labels = {
            "create_idea": "Created Idea",
            "expand_idea": "Expansion Suggestion",
            "challenge_idea": "Constructive Challenge",
            "combine_ideas": "Combined Idea",
        }
        return IdeaModelOutput(
            title=labels[request.action.value],
            summary="A validated result that preserves the original input.",
            category="General",
            spoken_response="Here is the brainstorming result.",
        )


class IdeaManagerEndpointTests(unittest.TestCase):
    token = "test-development-token"

    def setUp(self):
        backend_app.DEV_TOKEN = self.token
        backend_app.idea_request_registry = IdeaRequestRegistry()
        backend_app.idea_request_times.clear()
        self.provider = MockIdeaProvider()
        backend_app.idea_manager = IdeaManager(self.provider, timeout_seconds=1.0)
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        self.client.close()

    def payload(self, action: str, request_id: str, **overrides):
        value = {
            "request_id": request_id,
            "session_id": "Session 1",
            "action": action,
            "raw_transcript": "A solar-powered community workspace for students.",
            "cleaned_transcript": None,
            "transcript_source": "raw",
            "idea_id": None,
            "related_idea_ids": [],
            "existing_ideas": [],
        }
        value.update(overrides)
        return value

    def post(self, payload, token=None):
        return self.client.post(
            "/api/ideas/process",
            json=payload,
            headers={"X-ThinkingSpace-Token": token if token is not None else self.token},
        )

    def assert_success(self, response, action):
        self.assertEqual(response.status_code, 200, response.text)
        data = response.json()
        self.assertEqual(data["contract_version"], "1.0")
        self.assertEqual(data["action"], action)
        self.assertEqual(data["status"], "completed")
        self.assertIsNone(data["error"])
        self.assertTrue(data["idea_id"].startswith("idea_"))
        self.assertTrue(data["spoken_response"])

    def test_valid_create_idea(self):
        response = self.post(self.payload("create_idea", "request-create"))
        self.assert_success(response, "create_idea")

    def test_valid_expand_idea(self):
        response = self.post(self.payload(
            "expand_idea", "request-expand", idea_id="idea-original"))
        self.assert_success(response, "expand_idea")
        self.assertEqual(response.json()["related_idea_ids"], ["idea-original"])

    def test_valid_challenge_idea(self):
        response = self.post(self.payload(
            "challenge_idea", "request-challenge", idea_id="idea-original"))
        self.assert_success(response, "challenge_idea")

    def test_valid_combine_ideas(self):
        response = self.post(self.payload(
            "combine_ideas", "request-combine",
            related_idea_ids=["idea-one", "idea-two"]))
        self.assert_success(response, "combine_ideas")
        self.assertEqual(response.json()["related_idea_ids"], ["idea-one", "idea-two"])

    def test_missing_transcript(self):
        payload = self.payload("create_idea", "request-missing")
        del payload["raw_transcript"]
        response = self.post(payload)
        self.assertEqual(response.status_code, 422)
        self.assertEqual(response.json()["status"], "error")

    def test_invalid_idea_ids(self):
        response = self.post(self.payload(
            "combine_ideas", "request-invalid",
            related_idea_ids=["invalid idea id!", "idea-two"]))
        self.assertEqual(response.status_code, 422)

    def test_invalid_authentication(self):
        response = self.post(self.payload("create_idea", "request-auth"), token="wrong")
        self.assertEqual(response.status_code, 401)
        self.assertEqual(self.provider.calls, 0)

    def test_model_timeout(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider("timeout"), timeout_seconds=0.01)
        response = self.post(self.payload("create_idea", "request-timeout"))
        self.assertEqual(response.status_code, 504)
        self.assertEqual(response.json()["error"], "AI manager timed out.")

    def test_malformed_model_output(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider("malformed"), timeout_seconds=1.0)
        response = self.post(self.payload("create_idea", "request-malformed"))
        self.assertEqual(response.status_code, 502)
        self.assertEqual(response.json()["error"], "AI manager returned an invalid structured response.")

    def test_duplicate_request(self):
        payload = self.payload("create_idea", "request-duplicate")
        first = self.post(payload)
        second = self.post(payload)
        self.assertEqual(first.status_code, 200)
        self.assertEqual(second.status_code, 409)
        self.assertEqual(second.json()["error"], "Duplicate idea request.")


if __name__ == "__main__":
    unittest.main()
