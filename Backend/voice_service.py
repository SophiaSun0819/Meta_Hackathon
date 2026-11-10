import asyncio
from dataclasses import dataclass
import hashlib
import re
from typing import Literal, Protocol
from urllib.parse import quote

import httpx
from pydantic import BaseModel, ConfigDict, Field, field_validator

IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
SUPPORTED_TTS_MODELS = frozenset({"eleven_v4_turbo", "eleven_flash_v2_5"})
EXPRESSIVE_TAGS = {
    "laughs": "[laughs]", "curious": "[curious]", "excited": "[excited]",
    "sighs": "[sighs]", "whispers": "[whispers]", "clears_throat": "[clears throat]",
    "mischievously": "[mischievously]", "snorts": "[snorts]",
}
_KNOWN_UNSAFE_TAGS = frozenset({
    "explosion", "gunshot", "applause", "clapping", "shouts", "crying", "sings",
})
_BRACKETED_TEXT = re.compile(r"\[([^\[\]]{1,48})\]")


class VoiceSynthesisRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=False)
    request_id: str
    session_id: str = Field(min_length=1, max_length=128)
    text: str
    delivery: Literal["neutral", "laughs", "curious", "excited", "sighs", "whispers",
                      "clears_throat", "mischievously", "snorts"] = "neutral"

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


@dataclass(frozen=True)
class VoiceSynthesisResult:
    audio: bytes
    model: str
    used_fallback: bool = False


class VoiceProvider(Protocol):
    async def synthesize(self, text: str, delivery: str = "neutral") -> VoiceSynthesisResult: ...


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
                 timeout_seconds: float, maximum_audio_bytes: int,
                 fallback_model: str = "eleven_flash_v2_5", expressive_enabled: bool = True):
        self.api_key = api_key
        self.voice_id = voice_id
        self.model = model
        self.timeout_seconds = timeout_seconds
        self.maximum_audio_bytes = maximum_audio_bytes
        self.fallback_model = fallback_model
        self.expressive_enabled = expressive_enabled

    @property
    def cache_identity(self) -> str:
        raw = (f"{self.voice_id}|{self.model}|{self.fallback_model}|"
               f"expressive={self.expressive_enabled}|{self.output_format}|tags-v1")
        return hashlib.sha256(raw.encode("utf-8")).hexdigest()[:24]

    async def synthesize(self, text: str, delivery: str = "neutral") -> VoiceSynthesisResult:
        if not self.api_key or not self.voice_id or not self.model:
            raise VoiceConfigurationError("Voice service is not configured.")
        if self.model not in SUPPORTED_TTS_MODELS:
            raise VoiceConfigurationError("Configured voice model is not supported.")
        if self.fallback_model and self.fallback_model not in SUPPORTED_TTS_MODELS:
            raise VoiceConfigurationError("Configured fallback voice model is not supported.")
        plain_text = strip_tts_tags(text)
        tagged_text = apply_expressive_delivery(plain_text, delivery,
            self.expressive_enabled and self.model == "eleven_v4_turbo")
        try:
            audio = await self._request(tagged_text, self.model)
            return VoiceSynthesisResult(audio=audio, model=self.model)
        except (VoiceProviderFailure, VoiceInvalidAudio):
            if not self.fallback_model or self.fallback_model == self.model:
                raise
            audio = await self._request(plain_text, self.fallback_model)
            return VoiceSynthesisResult(audio=audio, model=self.fallback_model, used_fallback=True)

    async def _request(self, text: str, model: str) -> bytes:
        url = f"{self.endpoint_root}/{quote(self.voice_id, safe='')}"
        headers = {"xi-api-key": self.api_key, "accept": self.mime_type,
                   "content-type": "application/json"}
        payload = {"text": text, "model_id": model}
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


def apply_expressive_delivery(text: str, delivery: str, enabled: bool = True) -> str:
    plain = strip_tts_tags(text)
    if not enabled or delivery == "neutral":
        return plain
    tag = EXPRESSIVE_TAGS.get(delivery)
    return f"{tag} {plain}" if tag else plain


def strip_tts_tags(text: str) -> str:
    """Remove only known TTS directives; preserve meaningful ordinary bracketed text."""
    def replace(match: re.Match[str]) -> str:
        normalized = re.sub(r"\s+", "_", match.group(1).strip().lower())
        if normalized in EXPRESSIVE_TAGS or match.group(1).strip().lower() in _KNOWN_UNSAFE_TAGS:
            return ""
        return match.group(0)
    return re.sub(r"\s+", " ", _BRACKETED_TEXT.sub(replace, text or "")).strip()


def is_probable_mp3(data: bytes) -> bool:
    if len(data) < 4:
        return False
    if data[:3] == b"ID3":
        return True
    return data[0] == 0xFF and (data[1] & 0xE0) == 0xE0
