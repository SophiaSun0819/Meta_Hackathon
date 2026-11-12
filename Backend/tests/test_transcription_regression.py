import io
import os
import sys
import unittest
import wave
from pathlib import Path
from types import SimpleNamespace

os.environ["OPENAI_API_KEY"] = "test-key-not-used"
os.environ["THINKING_SPACE_DEV_TOKEN"] = "test-development-token"
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient

import app as backend_app


class FakeTranscriptionClient:
    raw_text = "  exact raw transcription  "

    def __init__(self, **kwargs):
        async def create(**request):
            return SimpleNamespace(text=self.raw_text)
        self.audio = SimpleNamespace(transcriptions=SimpleNamespace(create=create))

    async def close(self):
        pass


class TranscriptionRegressionTests(unittest.TestCase):
    def test_existing_transcription_response_remains_unchanged(self):
        original_client = backend_app.AsyncOpenAI
        backend_app.AsyncOpenAI = FakeTranscriptionClient
        backend_app.DEV_TOKEN = "test-development-token"
        backend_app.request_times.clear()
        try:
            buffer = io.BytesIO()
            with wave.open(buffer, "wb") as writer:
                writer.setnchannels(1)
                writer.setsampwidth(2)
                writer.setframerate(16000)
                writer.writeframes(b"\x01\x00" * 160)
            with TestClient(backend_app.app) as client:
                response = client.post(
                    "/api/transcriptions",
                    content=buffer.getvalue(),
                    headers={
                        "Content-Type": "audio/wav",
                        "X-Recording-ID": "transcription-regression",
                        "X-Session-ID": "Session 1",
                        "X-ThinkingSpace-Token": "test-development-token",
                    },
                )
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.json()["transcript"], FakeTranscriptionClient.raw_text)
            self.assertEqual(response.json()["raw_transcript"], FakeTranscriptionClient.raw_text)
            self.assertIsNone(response.json()["cleaned_transcript"])
        finally:
            backend_app.AsyncOpenAI = original_client


if __name__ == "__main__":
    unittest.main()
