import asyncio
import io
import os
import sys
import unittest
import wave
from pathlib import Path
from unittest.mock import patch

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app
from conversation_orchestrator import (ConversationClarificationStore, ConversationOrchestrator,
    ConversationProviderFailure, ConversationRequest)
from idea_manager import (IdeaManager, IdeaManagerMalformedOutput, IdeaManagerProviderFailure,
    IdeaProcessRequest, IdeaRequestRegistry, validate_saved_idea_summary)
from mock_providers import (MOCK_TRANSCRIPTS, MockConversationProvider, MockIdeaProvider,
    MockTranscriber, MockVoiceProvider, build_fallback_idea, keyword_title)
from voice_service import VoiceConfigurationError

TOKEN = "test-development-token"
IDEAS = [
    {"idea_id": "idea-one", "title": "Anatomy Lab", "summary": "A VR anatomy lab. Students dissect safely."},
    {"idea_id": "idea-two", "title": "Chemistry Game", "summary": "An AR chemistry game. Mix elements."},
]


class FailingIdeaProvider:
    def __init__(self, failure):
        self.failure = failure

    async def generate(self, request):
        if self.failure == "timeout":
            await asyncio.sleep(0.1)
        raise self.failure


class FailingConversationProvider:
    async def respond(self, request):
        # The OpenAI provider wraps every request failure in ConversationProviderFailure.
        raise ConversationProviderFailure("provider down")


def conversation_request(transcript, ideas=IDEAS, recent="idea-two"):
    return ConversationRequest.model_validate({
        "request_id": "mock-request", "session_id": "Session 1", "raw_transcript": transcript,
        "available_ideas": ideas,
        "context": {"recent_turns": [], "most_recent_idea_id": recent if ideas else None},
    })


def idea_request(action, transcript="I want an AR app that teaches chemistry with virtual elements.",
                 **overrides):
    value = {"request_id": "idea-request", "session_id": "Session 1", "action": action,
             "raw_transcript": transcript, "existing_ideas": IDEAS}
    value.update(overrides)
    return IdeaProcessRequest.model_validate(value)


class MockConversationTests(unittest.TestCase):
    def setUp(self):
        self.orchestrator = ConversationOrchestrator(MockConversationProvider(), timeout_seconds=1.0,
                                                     clarification_store=ConversationClarificationStore())

    def process(self, transcript, **kwargs):
        return asyncio.run(self.orchestrator.process(conversation_request(transcript, **kwargs)))

    def test_new_idea_is_created(self):
        result = self.process("A cooking app that projects recipe steps above the stove.")
        self.assertEqual(result.proposed_action, "create_idea")
        self.assertTrue(result.should_create_idea)
        self.assertFalse(result.requires_confirmation)

    def test_combine_targets_two_known_ideas(self):
        result = self.process("Combine those two ideas.")
        self.assertEqual(result.proposed_action, "combine_ideas")
        self.assertEqual(sorted(result.target_idea_ids), ["idea-one", "idea-two"])
        self.assertFalse(result.requires_confirmation)

    def test_expand_targets_most_recent_idea(self):
        result = self.process("Can you expand that idea?")
        self.assertEqual(result.proposed_action, "expand_idea")
        self.assertEqual(result.target_idea_ids, ["idea-two"])

    def test_challenge_never_creates_or_executes(self):
        result = self.process("What are the risks of that idea?")
        self.assertEqual(result.proposed_action, "conversational_challenge")
        self.assertFalse(result.should_create_idea)
        self.assertFalse(result.should_execute_action)

    def test_question_is_a_conversation(self):
        result = self.process("How do people usually brainstorm in teams?", ideas=[])
        self.assertEqual(result.proposed_action, "converse")
        self.assertFalse(result.should_create_idea)

    def test_combine_without_two_ideas_creates_instead(self):
        result = self.process("Combine this with something.", ideas=[])
        self.assertEqual(result.proposed_action, "create_idea")

    def test_every_mock_transcript_passes_orchestrator_validation(self):
        for transcript in MOCK_TRANSCRIPTS:
            with self.subTest(transcript=transcript):
                self.assertEqual(self.process(transcript).status, "completed")


class FallbackIdeaTests(unittest.TestCase):
    def test_keyword_title_skips_filler_words(self):
        self.assertEqual(keyword_title("I want to build an AR game that teaches chemistry."),
                         "Build AR Game Teaches Chemistry")
        self.assertEqual(keyword_title("um, uh, so"), "New idea")

    def test_create_keeps_the_transcript_and_passes_summary_rules(self):
        output = build_fallback_idea(idea_request("create_idea"))
        self.assertEqual(output.summary, "I want an AR app that teaches chemistry with virtual elements.")
        self.assertIsNone(validate_saved_idea_summary(output.summary))

    def test_long_transcript_is_trimmed_under_the_word_limit(self):
        output = build_fallback_idea(idea_request("create_idea", transcript="word " * 200))
        self.assertIsNone(validate_saved_idea_summary(output.summary))

    def test_combine_names_both_sources(self):
        output = build_fallback_idea(idea_request(
            "combine_ideas", related_idea_ids=["idea-one", "idea-two"]), mock=True)
        self.assertEqual(output.title, "Anatomy Lab + Chemistry Game")
        self.assertIn("Anatomy Lab", output.summary)
        self.assertIsNone(validate_saved_idea_summary(output.summary))

    def test_every_action_produces_a_valid_mock_idea(self):
        requests = [idea_request("create_idea"),
                    idea_request("expand_idea", idea_id="idea-one"),
                    idea_request("challenge_idea", idea_id="idea-one"),
                    idea_request("combine_ideas", related_idea_ids=["idea-one", "idea-two"])]
        for request in requests:
            with self.subTest(action=request.action.value):
                output = asyncio.run(MockIdeaProvider().generate(request))
                self.assertIsNone(validate_saved_idea_summary(output.summary))


class IdeaFallbackEndpointTests(unittest.TestCase):
    def setUp(self):
        backend_app.DEV_TOKEN = TOKEN
        backend_app.idea_request_registry = IdeaRequestRegistry()
        backend_app.idea_request_times.clear()
        self.original_manager = backend_app.idea_manager
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        backend_app.idea_manager = self.original_manager
        self.client.close()

    def post(self, request_id, action="create_idea", **overrides):
        payload = {"request_id": request_id, "session_id": "Session 1", "action": action,
                   "raw_transcript": "A rooftop garden that teaches kids about weather."}
        payload.update(overrides)
        return self.client.post("/api/ideas/process", json=payload,
                                headers={"X-ThinkingSpace-Token": TOKEN})

    def test_create_falls_back_for_each_provider_failure(self):
        failures = {"timeout": "timeout", "malformed": IdeaManagerMalformedOutput("bad"),
                    "provider": IdeaManagerProviderFailure("down")}
        for name, failure in failures.items():
            with self.subTest(failure=name):
                backend_app.idea_manager = IdeaManager(FailingIdeaProvider(failure), timeout_seconds=0.01)
                response = self.post(f"fallback-{name}")
                self.assertEqual(response.status_code, 200, response.text)
                data = response.json()
                self.assertEqual(data["status"], "completed")
                self.assertEqual(data["source"], "fallback")
                self.assertTrue(data["idea_id"].startswith("idea_"))
                self.assertEqual(data["summary"], "A rooftop garden that teaches kids about weather.")

    def test_combine_failure_still_returns_an_error_with_a_spoken_line(self):
        backend_app.idea_manager = IdeaManager(
            FailingIdeaProvider(IdeaManagerProviderFailure("down")), timeout_seconds=1.0)
        response = self.post("fallback-combine", action="combine_ideas",
                             related_idea_ids=["idea-one", "idea-two"])
        self.assertEqual(response.status_code, 502)
        self.assertEqual(response.json()["status"], "error")
        self.assertTrue(response.json()["fallback_spoken_response"])

    def test_successful_response_reports_model_source(self):
        backend_app.idea_manager = IdeaManager(MockIdeaProvider(), timeout_seconds=1.0)
        response = self.post("source-model")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["source"], "model")

    def test_validation_errors_have_no_spoken_line(self):
        response = self.client.post("/api/ideas/process", json={"request_id": "bad"},
                                    headers={"X-ThinkingSpace-Token": TOKEN})
        self.assertEqual(response.status_code, 422)
        self.assertIsNone(response.json()["fallback_spoken_response"])


class ConversationFallbackLineTests(unittest.TestCase):
    def setUp(self):
        backend_app.DEV_TOKEN = TOKEN
        backend_app.conversation_request_registry = IdeaRequestRegistry()
        backend_app.conversation_request_times.clear()
        self.original_orchestrator = backend_app.conversation_orchestrator
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        backend_app.conversation_orchestrator = self.original_orchestrator
        self.client.close()

    def test_provider_failure_keeps_error_and_adds_spoken_line(self):
        backend_app.conversation_orchestrator = ConversationOrchestrator(
            FailingConversationProvider(), timeout_seconds=1.0)
        response = self.client.post("/api/conversations/process", headers={"X-ThinkingSpace-Token": TOKEN},
            json={"request_id": "conversation-down", "session_id": "Session 1",
                  "raw_transcript": "Hello there."})
        self.assertEqual(response.status_code, 502)
        data = response.json()
        self.assertEqual(data["status"], "error")
        self.assertEqual(data["fallback_spoken_response"], backend_app.FALLBACK_SPOKEN_RESPONSES[502])

    def test_provider_failure_uses_resolved_language_without_hiding_error(self):
        backend_app.conversation_orchestrator = ConversationOrchestrator(
            FailingConversationProvider(), timeout_seconds=1.0)
        response = self.client.post("/api/conversations/process",
            headers={"X-ThinkingSpace-Token": TOKEN}, json={
                "request_id": "conversation-down-es", "session_id": "Session 1",
                "raw_transcript": "Hola.", "context": {
                    "response_language_mode": "explicit", "response_language": "es",
                    "voice_profile_id": "orby-auto"}})
        self.assertEqual(response.status_code, 502)
        data = response.json()
        self.assertEqual(data["status"], "error")
        self.assertEqual(data["response_language"], "es")
        self.assertEqual(data["voice_profile_id"], "orby-es")
        self.assertEqual(data["fallback_spoken_response"],
                         backend_app.LOCALIZED_CONVERSATION_FALLBACKS["es"][502])


class MockModeEndpointTests(unittest.TestCase):
    def setUp(self):
        backend_app.DEV_TOKEN = TOKEN
        backend_app.request_times.clear()
        backend_app.mock_transcriber = MockTranscriber(("First mock line.", "Second mock line."))
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        self.client.close()

    def wav(self):
        buffer = io.BytesIO()
        with wave.open(buffer, "wb") as writer:
            writer.setnchannels(1)
            writer.setsampwidth(2)
            writer.setframerate(16000)
            writer.writeframes(b"\x01\x00" * 160)
        return buffer.getvalue()

    def transcribe(self, recording_id):
        return self.client.post("/api/transcriptions", content=self.wav(), headers={
            "Content-Type": "audio/wav", "X-Recording-ID": recording_id,
            "X-Session-ID": "Session 1", "X-ThinkingSpace-Token": TOKEN})

    def test_transcription_cycles_without_calling_openai(self):
        with patch.object(backend_app, "MOCK_MODE", True), \
                patch.object(backend_app, "AsyncOpenAI", side_effect=AssertionError("OpenAI called")):
            first = self.transcribe("mock-one")
            second = self.transcribe("mock-two")
        self.assertEqual(first.status_code, 200, first.text)
        self.assertEqual(first.json()["transcript"], "First mock line.")
        self.assertEqual(second.json()["raw_transcript"], "Second mock line.")
        self.assertEqual(first.json()["transcription_model"], "mock")

    def test_health_reports_mode_and_skips_openai_key_in_mock_mode(self):
        with patch.object(backend_app, "MOCK_MODE", True), patch.dict(os.environ, {"OPENAI_API_KEY": ""}):
            response = self.client.get("/health")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {"status": "ok", "mode": "mock"})
        live = self.client.get("/health")
        self.assertEqual(live.json()["mode"], "live")

    def test_mock_voice_reports_not_configured(self):
        with self.assertRaises(VoiceConfigurationError):
            asyncio.run(MockVoiceProvider().synthesize("Hello"))


if __name__ == "__main__":
    unittest.main()
