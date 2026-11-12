import asyncio
import os
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app
from idea_manager import IdeaRequestRegistry
from voice_service import (ElevenLabsVoiceProvider, VoiceConfigurationError, VoiceInvalidAudio,
                           VoiceProviderFailure, VoiceProviderTimeout)
from language_support import VOICE_PROFILES, resolve_tts_model


class FixedVoiceProvider:
    def __init__(self, audio=b"ID3\x04\x00\x00mock-mp3", error=None):
        self.audio = audio
        self.error = error
        self.calls = 0
        self.texts = []
        self.profiles = []

    async def synthesize(self, text, profile=None):
        self.calls += 1
        self.texts.append(text)
        self.profiles.append(profile)
        if self.error:
            raise self.error
        return self.audio


class FakeStreamResponse:
    status_code = 200
    async def __aenter__(self): return self
    async def __aexit__(self, *_): return False
    async def aiter_bytes(self): yield b"ID3\x04\x00\x00mock-mp3"


class FakeHttpClient:
    last_request = None
    def __init__(self, **_): pass
    async def __aenter__(self): return self
    async def __aexit__(self, *_): return False
    def stream(self, method, url, **kwargs):
        FakeHttpClient.last_request = (method, url, kwargs)
        return FakeStreamResponse()


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
        self.assertEqual(provider.calls, 1)

    def test_delivery_is_optional_and_supported_values_are_ignored(self):
        provider = FixedVoiceProvider()
        backend_app.voice_provider = provider
        plain_text = "Plain text remains unchanged."

        without_delivery = self.payload(plain_text)
        self.assertEqual(self.post(without_delivery).status_code, 200)

        supported_deliveries = (
            "neutral", "laughs", "curious", "excited", "sighs", "whispers",
            "clears_throat", "mischievously", "snorts",
        )
        for index, delivery in enumerate(supported_deliveries, start=2):
            with self.subTest(delivery=delivery):
                payload = {
                    "request_id": f"voice-delivery-{index}",
                    "session_id": "Session 1",
                    "text": plain_text,
                    "delivery": delivery,
                }
                self.assertEqual(self.post(payload).status_code, 200)

        expected_calls = 1 + len(supported_deliveries)
        self.assertEqual(provider.calls, expected_calls)
        self.assertEqual(provider.texts, [plain_text] * expected_calls)

    def test_unsupported_delivery_is_rejected_before_provider(self):
        provider = FixedVoiceProvider()
        backend_app.voice_provider = provider
        payload = self.payload("Plain text remains unchanged.")
        payload["delivery"] = "unsupported"

        response = self.post(payload)

        self.assertEqual(response.status_code, 422)
        self.assertEqual(provider.calls, 0)

    def test_all_approved_voice_profiles_are_server_resolved(self):
        provider = FixedVoiceProvider()
        backend_app.voice_provider = provider
        cases = (("orby-en", "en"), ("orby-es", "es"), ("orby-fr", "fr"),
                 ("orby-zh", "zh-CN"), ("orby-hi", "hi-IN"))
        for index, (profile_id, language) in enumerate(cases):
            payload = {"request_id": f"voice-profile-{index}", "session_id": "Session 1",
                       "text": "Validated text.", "response_language": language,
                       "voice_profile_id": profile_id}
            response = self.post(payload)
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.headers["x-thinkingspace-response-language"], language)
            self.assertEqual(response.headers["x-thinkingspace-voice-profile"], profile_id)
        self.assertEqual([profile.profile_id for profile in provider.profiles],
                         [profile_id for profile_id, _ in cases])
        self.assertEqual(provider.calls, len(cases))

    def test_language_profile_validation_and_model_fallback(self):
        provider = FixedVoiceProvider()
        backend_app.voice_provider = provider
        invalid_language = self.payload(); invalid_language["response_language"] = "xx"
        invalid_profile = self.payload(); invalid_profile["voice_profile_id"] = "user-voice"
        conflict = self.payload(); conflict.update(
            {"response_language": "es", "voice_profile_id": "orby-fr"})
        for payload in (invalid_language, invalid_profile, conflict):
            backend_app.voice_request_times.clear()
            self.assertEqual(self.post(payload).status_code, 422)
        self.assertEqual(provider.calls, 0)
        self.assertEqual(resolve_tts_model("unsupported-model"), "eleven_flash_v2_5")
        self.assertEqual(resolve_tts_model("eleven_v4_turbo"), "eleven_v4_turbo")
        self.assertEqual(VOICE_PROFILES["orby-en"].voice_id, "E584YCmNqnu35GrWlrqy")

    def test_elevenlabs_receives_server_profile_language_and_unchanged_text(self):
        provider = ElevenLabsVoiceProvider("secret-not-logged", "",
            "eleven_flash_v2_5", 5, 1024)
        profile = VOICE_PROFILES["orby-zh"]
        with patch("voice_service.httpx.AsyncClient", FakeHttpClient):
            audio = asyncio.run(provider.synthesize("原始文本 42", profile))
        self.assertTrue(audio.startswith(b"ID3"))
        method, url, kwargs = FakeHttpClient.last_request
        self.assertEqual(method, "POST")
        self.assertTrue(url.endswith("/" + profile.voice_id))
        self.assertEqual(kwargs["json"], {"text": "原始文本 42",
                                          "model_id": "eleven_flash_v2_5",
                                          "language_code": "zh"})
        self.assertNotIn("secret-not-logged", str(kwargs["json"]))

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
