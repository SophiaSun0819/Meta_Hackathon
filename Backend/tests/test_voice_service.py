import os
import sys
import unittest
from pathlib import Path

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app
from idea_manager import IdeaRequestRegistry
from voice_service import (VoiceConfigurationError, VoiceInvalidAudio, VoiceProviderFailure,
                           VoiceProviderTimeout, VoiceSynthesisResult, ElevenLabsVoiceProvider,
                           apply_expressive_delivery, strip_tts_tags)


class FixedVoiceProvider:
    def __init__(self, audio=b"ID3\x04\x00\x00mock-mp3", error=None):
        self.audio = audio
        self.error = error
        self.calls = 0

        self.cache_identity = "test-cache-identity"

    async def synthesize(self, text, delivery="neutral"):
        self.calls += 1
        if self.error:
            raise self.error
        return VoiceSynthesisResult(self.audio, "test-model")


class RecordingElevenLabsProvider(ElevenLabsVoiceProvider):
    def __init__(self, failures=None, **kwargs):
        super().__init__(api_key="test-key", voice_id="E584YCmNqnu35GrWlrqy",
            timeout_seconds=1, maximum_audio_bytes=1024, **kwargs)
        self.requests = []
        self.failures = list(failures or [])

    async def _request(self, text, model):
        self.requests.append((text, model))
        if self.failures:
            failure = self.failures.pop(0)
            if failure:
                raise failure
        return b"ID3\x04\x00\x00mock-mp3"


class VoiceEndpointTests(unittest.TestCase):
    token = "test-development-token"

    def setUp(self):
        backend_app.DEV_TOKEN = self.token
        backend_app.voice_request_registry = IdeaRequestRegistry()
        backend_app.voice_request_times.clear()
        self.original_rate = backend_app.MAX_TTS_REQUESTS_PER_MINUTE
        self.original_text = backend_app.MAX_TTS_TEXT_CHARACTERS
        self.original_audio = backend_app.MAX_TTS_AUDIO_BYTES
        backend_app.MAX_TTS_REQUESTS_PER_MINUTE = 20
        backend_app.MAX_TTS_TEXT_CHARACTERS = 2000
        backend_app.MAX_TTS_AUDIO_BYTES = 5 * 1024 * 1024
        self.client = TestClient(backend_app.app)

    def tearDown(self):
        self.client.close()
        backend_app.MAX_TTS_REQUESTS_PER_MINUTE = self.original_rate
        backend_app.MAX_TTS_TEXT_CHARACTERS = self.original_text
        backend_app.MAX_TTS_AUDIO_BYTES = self.original_audio

    @staticmethod
    def payload(text="Hello from Orby."):
        return {"request_id": "voice-test-1", "session_id": "Session 1", "text": text}

    def post(self, payload=None, token=None):
        headers = {"X-ThinkingSpace-Token": self.token if token is None else token}
        return self.client.post("/api/voice/synthesize", json=payload or self.payload(), headers=headers)

    def test_valid_tts_request_returns_mp3_and_correlation_headers(self):
        provider = FixedVoiceProvider()
        backend_app.voice_provider = provider
        response = self.post()
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.headers["content-type"], "audio/mpeg")
        self.assertEqual(response.headers["x-thinkingspace-request-id"], "voice-test-1")
        self.assertEqual(response.headers["x-thinkingspace-audio-format"], "mp3_44100_128")
        self.assertEqual(response.headers["x-thinkingspace-voice-model"], "test-model")
        self.assertEqual(provider.calls, 1)

    def test_v4_model_selection_preserves_voice_and_flash_rollback(self):
        provider = RecordingElevenLabsProvider(model="eleven_v4_turbo",
            fallback_model="eleven_flash_v2_5", expressive_enabled=True,
            failures=[VoiceProviderFailure("unsupported"), None])
        result = __import__("asyncio").run(provider.synthesize("A bright idea.", "excited"))
        self.assertEqual(provider.voice_id, "E584YCmNqnu35GrWlrqy")
        self.assertEqual(provider.requests[0], ("[excited] A bright idea.", "eleven_v4_turbo"))
        self.assertEqual(provider.requests[1], ("A bright idea.", "eleven_flash_v2_5"))
        self.assertTrue(result.used_fallback)
        self.assertEqual(result.model, "eleven_flash_v2_5")

    def test_expressive_allowlist_and_plain_subtitle_text(self):
        self.assertEqual(apply_expressive_delivery("Hello.", "curious"), "[curious] Hello.")
        self.assertEqual(apply_expressive_delivery("Hello.", "explosion"), "Hello.")
        self.assertEqual(strip_tts_tags("[laughs] Fair point [Phase 2]."),
                         "Fair point [Phase 2].")
        self.assertEqual(strip_tts_tags("[explosion] Still safe."), "Still safe.")

    def test_flash_model_uses_plain_text_without_expression(self):
        provider = RecordingElevenLabsProvider(model="eleven_flash_v2_5",
            fallback_model="eleven_flash_v2_5", expressive_enabled=True)
        __import__("asyncio").run(provider.synthesize("Hello.", "laughs"))
        self.assertEqual(provider.requests, [("Hello.", "eleven_flash_v2_5")])

    def test_unsupported_model_and_delivery_are_rejected(self):
        provider = RecordingElevenLabsProvider(model="invented-model",
            fallback_model="eleven_flash_v2_5", expressive_enabled=True)
        with self.assertRaises(VoiceConfigurationError):
            __import__("asyncio").run(provider.synthesize("Hello."))
        backend_app.voice_provider = FixedVoiceProvider()
        response = self.post({"request_id":"voice-bad-delivery", "session_id":"Session 1",
                              "text":"Hello.", "delivery":"explosion"})
        self.assertEqual(response.status_code, 422)
        self.assertEqual(backend_app.voice_provider.calls, 0)

    def test_missing_and_incorrect_authentication(self):
        backend_app.voice_provider = FixedVoiceProvider()
        missing = self.client.post("/api/voice/synthesize", json=self.payload())
        incorrect = self.post(token="incorrect")
        self.assertEqual(missing.status_code, 401)
        self.assertEqual(incorrect.status_code, 401)
        self.assertEqual(backend_app.voice_provider.calls, 0)

    def test_missing_empty_and_oversized_text(self):
        backend_app.voice_provider = FixedVoiceProvider()
        missing = self.post({"request_id":"voice-missing", "session_id":"Session 1"})
        empty = self.post({"request_id":"voice-empty", "session_id":"Session 1", "text":"   "})
        backend_app.MAX_TTS_TEXT_CHARACTERS = 4
        oversized = self.post({"request_id":"voice-large", "session_id":"Session 1", "text":"12345"})
        self.assertEqual(missing.status_code, 422)
        self.assertEqual(empty.status_code, 422)
        self.assertEqual(oversized.status_code, 422)
        self.assertEqual(backend_app.voice_provider.calls, 0)

    def test_rate_limit_is_separate(self):
        backend_app.MAX_TTS_REQUESTS_PER_MINUTE = 1
        backend_app.voice_provider = FixedVoiceProvider()
        self.assertEqual(self.post().status_code, 200)
        second = self.post({"request_id":"voice-test-2", "session_id":"Session 2", "text":"Again"})
        self.assertEqual(second.status_code, 429)

    def test_timeout_provider_failure_and_missing_configuration(self):
        cases = [(VoiceProviderTimeout("timeout"), 504),
                 (VoiceProviderFailure("failure"), 502),
                 (VoiceConfigurationError("missing"), 503)]
        for index, (error, expected) in enumerate(cases):
            with self.subTest(error=type(error).__name__):
                backend_app.voice_request_times.clear()
                backend_app.voice_request_registry = IdeaRequestRegistry()
                backend_app.voice_provider = FixedVoiceProvider(error=error)
                payload={"request_id":f"voice-error-{index}", "session_id":"Session 1", "text":"Hello"}
                self.assertEqual(self.post(payload).status_code, expected)

    def test_invalid_and_oversized_audio_are_rejected(self):
        backend_app.voice_provider = FixedVoiceProvider(audio=b"not-mp3")
        self.assertEqual(self.post().status_code, 502)
        backend_app.voice_request_times.clear()
        backend_app.voice_request_registry = IdeaRequestRegistry()
        backend_app.MAX_TTS_AUDIO_BYTES = 8
        backend_app.voice_provider = FixedVoiceProvider(audio=b"ID3" + b"x" * 20)
        response = self.post({"request_id":"voice-too-large", "session_id":"Session 1", "text":"Hello"})
        self.assertEqual(response.status_code, 502)


if __name__ == "__main__":
    unittest.main()
