"""Validated multilingual controls shared by conversation and voice endpoints.

Language and profile values are application configuration, never prompt instructions.  Voice IDs
remain server-controlled and are intentionally not accepted from clients.
"""

from dataclasses import dataclass
from typing import Literal

from pydantic import BaseModel, ConfigDict

LanguageMode = Literal["auto", "explicit"]
SUPPORTED_LANGUAGE_CODES = ("en", "es", "fr", "zh-CN", "hi-IN")
SUPPORTED_VOICE_PROFILE_IDS = (
    "orby-auto", "orby-en", "orby-es", "orby-fr", "orby-zh", "orby-hi")
DEFAULT_LANGUAGE_CODE = "en"
DEFAULT_VOICE_PROFILE_ID = "orby-en"
DEFAULT_TTS_MODEL = "eleven_flash_v2_5"
SUPPORTED_TTS_MODELS = frozenset(("eleven_flash_v2_5", "eleven_v4_turbo"))

_LANGUAGE_ALIASES = {
    "en": "en", "es": "es", "fr": "fr",
    "zh": "zh-CN", "zh-cn": "zh-CN",
    "hi": "hi-IN", "hi-in": "hi-IN",
}


@dataclass(frozen=True)
class VoiceProfile:
    profile_id: str
    language_code: str
    provider_language_code: str
    voice_id: str


VOICE_PROFILES = {
    "orby-en": VoiceProfile("orby-en", "en", "en", "E584YCmNqnu35GrWlrqy"),
    "orby-es": VoiceProfile("orby-es", "es", "es", "XfkmrC8l5wyIoNYxLLdM"),
    "orby-fr": VoiceProfile("orby-fr", "fr", "fr", "vxshOjiovjkH75o87X3w"),
    "orby-zh": VoiceProfile("orby-zh", "zh-CN", "zh", "S1ewpGGAA8fDVQnRPhwe"),
    "orby-hi": VoiceProfile("orby-hi", "hi-IN", "hi", "uzFnDebxtBVkexuacaUj"),
}
VOICE_PROFILE_BY_LANGUAGE = {value.language_code: value for value in VOICE_PROFILES.values()}


class LanguageResolution(BaseModel):
    model_config = ConfigDict(frozen=True)
    language_code: str
    source: Literal["explicit", "input_language", "default"]
    voice_profile_id: str


def normalize_language_code(value: str | None) -> str | None:
    if value is None:
        return None
    return _LANGUAGE_ALIASES.get(value.strip().replace("_", "-").lower())


def normalize_voice_profile_id(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = value.strip().lower()
    return normalized if normalized in SUPPORTED_VOICE_PROFILE_IDS else None


def resolve_language(mode: LanguageMode, input_language: str | None,
                     response_language: str | None, voice_profile_id: str | None) -> LanguageResolution:
    if mode == "explicit":
        if response_language is None:
            raise ValueError("Explicit response language is required.")
        language = response_language
        source = "explicit"
    else:
        if response_language is not None:
            raise ValueError("Auto mode cannot include an explicit response language.")
        language = input_language or DEFAULT_LANGUAGE_CODE
        source = "input_language" if input_language else "default"

    requested_profile = voice_profile_id or "orby-auto"
    profile = VOICE_PROFILE_BY_LANGUAGE[language] if requested_profile == "orby-auto" \
        else VOICE_PROFILES[requested_profile]
    if profile.language_code != language:
        raise ValueError("Voice profile does not match the resolved response language.")
    return LanguageResolution(language_code=language, source=source,
                              voice_profile_id=profile.profile_id)


def resolve_voice_profile(language_code: str | None,
                          voice_profile_id: str | None) -> VoiceProfile:
    requested_profile = voice_profile_id or "orby-auto"
    language = language_code
    if language is None and requested_profile != "orby-auto":
        language = VOICE_PROFILES[requested_profile].language_code
    language = language or DEFAULT_LANGUAGE_CODE
    profile = VOICE_PROFILE_BY_LANGUAGE[language] if requested_profile == "orby-auto" \
        else VOICE_PROFILES[requested_profile]
    if profile.language_code != language:
        raise ValueError("Voice profile does not match the requested response language.")
    return profile


def resolve_tts_model(configured_model: str | None) -> str:
    value = (configured_model or "").strip()
    return value if value in SUPPORTED_TTS_MODELS else DEFAULT_TTS_MODEL


def language_display_name(language_code: str) -> str:
    return {"en": "English", "es": "Spanish", "fr": "French",
            "zh-CN": "Simplified Chinese", "hi-IN": "Hindi"}[language_code]
