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
                          IdeaRequestRegistry, count_summary_words,
                          validate_saved_idea_summary)


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


class SummaryProvider(MockIdeaProvider):
    def __init__(self, summary: str, corrected_summary: str | None = None):
        super().__init__()
        self.summary = summary
        self.corrected_summary = corrected_summary
        self.correction_calls = 0

    async def generate(self, request):
        output = await super().generate(request)
        return output.model_copy(update={"summary": self.summary})

    async def correct_summary(self, request, invalid_output, reason):
        self.correction_calls += 1
        value = self.corrected_summary if self.corrected_summary is not None else self.summary
        return invalid_output.model_copy(update={"summary": value})


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

    # create_idea failures fall back to a locally built idea (see test_mock_and_fallback.py),
    # so the error paths are exercised with an action that has no fallback.
    def test_model_timeout(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider("timeout"), timeout_seconds=0.01)
        response = self.post(self.payload("expand_idea", "request-timeout", idea_id="idea-original"))
        self.assertEqual(response.status_code, 504)
        self.assertEqual(response.json()["error"], "AI manager timed out.")
        self.assertTrue(response.json()["fallback_spoken_response"])

    def test_malformed_model_output(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider("malformed"), timeout_seconds=1.0)
        response = self.post(self.payload("expand_idea", "request-malformed", idea_id="idea-original"))
        self.assertEqual(response.status_code, 502)
        self.assertEqual(response.json()["error"], "AI manager returned an invalid structured response.")
        self.assertTrue(response.json()["fallback_spoken_response"])

    def test_duplicate_request(self):
        payload = self.payload("create_idea", "request-duplicate")
        first = self.post(payload)
        second = self.post(payload)
        self.assertEqual(first.status_code, 200)
        self.assertEqual(second.status_code, 409)
        self.assertEqual(second.json()["error"], "Duplicate idea request.")

    def test_short_valid_summary(self):
        self.assertIsNone(validate_saved_idea_summary("A shared solar workspace for students."))

    def test_target_length_summary(self):
        summary = " ".join(f"word{i}" for i in range(52))
        self.assertEqual(count_summary_words(summary), 52)
        self.assertIsNone(validate_saved_idea_summary(summary))

    def test_exactly_eighty_words_is_valid(self):
        summary = " ".join(f"word{i}" for i in range(80))
        self.assertEqual(count_summary_words(summary), 80)
        self.assertIsNone(validate_saved_idea_summary(summary))

    def test_more_than_eighty_words_gets_one_correction(self):
        invalid = " ".join(f"word{i}" for i in range(81))
        corrected = " ".join(f"word{i}" for i in range(50))
        provider = SummaryProvider(invalid, corrected)
        backend_app.idea_manager = IdeaManager(provider, timeout_seconds=1.0)
        response = self.post(self.payload("create_idea", "request-corrected"))
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(provider.calls, 1)
        self.assertEqual(provider.correction_calls, 1)
        self.assertEqual(count_summary_words(response.json()["summary"]), 50)

    def test_invalid_correction_returns_recoverable_error(self):
        invalid = " ".join(f"word{i}" for i in range(81))
        provider = SummaryProvider(invalid, invalid)
        backend_app.idea_manager = IdeaManager(provider, timeout_seconds=1.0)
        response = self.post(self.payload("combine_ideas", "request-bad-correction",
            related_idea_ids=["idea-one", "idea-two"]))
        self.assertEqual(response.status_code, 502)
        self.assertEqual(provider.correction_calls, 1)
        self.assertEqual(response.json()["error"],
                         "AI manager returned an invalid structured response.")

    def test_single_paragraph_and_plain_text_validation(self):
        self.assertIsNotNone(validate_saved_idea_summary("First paragraph.\nSecond paragraph."))
        self.assertIsNotNone(validate_saved_idea_summary("- First bullet in a list."))
        self.assertIsNotNone(validate_saved_idea_summary("A **formatted** summary."))

    def test_expand_and_combine_apply_summary_contract(self):
        invalid = " ".join(f"word{i}" for i in range(81))
        valid = "A focused expansion that preserves the original idea while adding a practical mechanism."
        for index, (action, overrides) in enumerate([
                ("expand_idea", {"idea_id": "idea-original"}),
                ("combine_ideas", {"related_idea_ids": ["idea-one", "idea-two"]})]):
            provider = SummaryProvider(invalid, valid)
            backend_app.idea_manager = IdeaManager(provider, timeout_seconds=1.0)
            response = self.post(self.payload(action, f"request-summary-{index}", **overrides))
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(provider.correction_calls, 1)

    def test_legacy_challenge_contract_is_unchanged(self):
        long_summary = " ".join(f"word{i}" for i in range(81))
        provider = SummaryProvider(long_summary)
        backend_app.idea_manager = IdeaManager(provider, timeout_seconds=1.0)
        response = self.post(self.payload(
            "challenge_idea", "request-legacy-challenge", idea_id="idea-original"))
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["contract_version"], "1.0")
        self.assertEqual(provider.correction_calls, 0)


if __name__ == "__main__":
    unittest.main()
