import asyncio
import re
from typing import Literal, Protocol
from urllib.parse import quote

import httpx
from pydantic import BaseModel, ConfigDict, Field, field_validator

IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")


class VoiceSynthesisRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=False)
    request_id: str
    session_id: str = Field(min_length=1, max_length=128)
    text: str
    delivery: Literal[
        "neutral",
        "laughs",
        "curious",
        "excited",
        "sighs",
        "whispers",
        "clears_throat",
        "mischievously",
        "snorts",
    ] = "neutral"

    @field_validator("request_id")
    @classmethod
    def validate_request_id(cls, value: str) -> str:
        if not IDENTIFIER_PATTERN.fullmatch(value):
            raise ValueError("Invalid request identifier.")
        return value

    @field_validator("session_id")
    @classmethod
    def validate_session_id(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("Session identifier is required.")
        return value

    @field_validator("text")
    @classmethod
    def validate_text(cls, value: str) -> str:
        if not value or not value.strip():
            raise ValueError("Text is required.")
        return value


class VoiceProvider(Protocol):
    async def synthesize(self, text: str) -> bytes: ...


class VoiceConfigurationError(RuntimeError): pass
class VoiceProviderTimeout(RuntimeError): pass
class VoiceProviderFailure(RuntimeError): pass
class VoiceInvalidAudio(RuntimeError): pass
class VoiceAudioTooLarge(RuntimeError): pass


class ElevenLabsVoiceProvider:
    endpoint_root = "https://api.elevenlabs.io/v1/text-to-speech"
    output_format = "mp3_44100_128"
    mime_type = "audio/mpeg"

    def __init__(self, api_key: str, voice_id: str, model: str,
                 timeout_seconds: float, maximum_audio_bytes: int):
        self.api_key = api_key
        self.voice_id = voice_id
        self.model = model
        self.timeout_seconds = timeout_seconds
        self.maximum_audio_bytes = maximum_audio_bytes

    async def synthesize(self, text: str) -> bytes:
        if not self.api_key or not self.voice_id or not self.model:
            raise VoiceConfigurationError("Voice service is not configured.")
        url = f"{self.endpoint_root}/{quote(self.voice_id, safe='')}"
        headers = {"xi-api-key": self.api_key, "accept": self.mime_type,
                   "content-type": "application/json"}
        payload = {"text": text, "model_id": self.model}
        audio = bytearray()
        try:
            timeout = httpx.Timeout(self.timeout_seconds)
            async with httpx.AsyncClient(timeout=timeout, follow_redirects=False) as client:
                async with client.stream("POST", url, params={"output_format": self.output_format},
                                         headers=headers, json=payload) as response:
                    if response.status_code < 200 or response.status_code >= 300:
                        raise VoiceProviderFailure("Voice provider rejected the request.")
                    async for chunk in response.aiter_bytes():
                        if len(audio) + len(chunk) > self.maximum_audio_bytes:
                            raise VoiceAudioTooLarge("Generated voice audio exceeded the configured limit.")
                        audio.extend(chunk)
        except httpx.TimeoutException as exc:
            raise VoiceProviderTimeout("Voice provider timed out.") from exc
        except (VoiceProviderFailure, VoiceAudioTooLarge):
            raise
        except httpx.HTTPError as exc:
            raise VoiceProviderFailure("Voice provider request failed.") from exc
        result = bytes(audio)
        if not is_probable_mp3(result):
            raise VoiceInvalidAudio("Voice provider returned invalid audio.")
        return result


def is_probable_mp3(data: bytes) -> bool:
    if len(data) < 4:
        return False
    if data[:3] == b"ID3":
        return True
    return data[0] == 0xFF and (data[1] & 0xE0) == 0xE0
