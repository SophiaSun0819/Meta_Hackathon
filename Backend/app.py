import asyncio
import hmac
import io
import logging
import os
import re
import time
import wave
from collections import deque

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse, Response
from openai import AsyncOpenAI
from dotenv import load_dotenv
from pydantic import ValidationError

from idea_manager import (IdeaManager, IdeaManagerMalformedOutput, IdeaManagerProviderFailure,
    IdeaManagerTimeout, IdeaProcessRequest, IdeaRequestRegistry, OpenAIIdeaProvider, error_response)
from intent_router import (ClarificationStore, IntentRouteRequest, IntentRouter, IntentRouterMalformedOutput,
    IntentRouterProviderFailure, IntentRouterTimeout, OpenAIIntentProvider,
    error_response as intent_error_response)
from voice_service import (ElevenLabsVoiceProvider, VoiceAudioTooLarge, VoiceConfigurationError,
    VoiceInvalidAudio, VoiceProviderFailure, VoiceProviderTimeout, VoiceSynthesisRequest,
    is_probable_mp3)
from conversation_orchestrator import (ConversationClarificationStore, ConversationContextError,
    ConversationMalformedOutput, ConversationOrchestrator,
    ConversationProviderFailure, ConversationRequest, ConversationTimeout,
    OpenAIConversationProvider, error_response as conversation_error_response)
from urllib.parse import quote

load_dotenv()
logger = logging.getLogger("thinking_space.backend")
MAX_WAV_BYTES = int(os.getenv("MAX_WAV_BYTES", str(15 * 1024 * 1024)))
OPENAI_TIMEOUT_SECONDS = float(os.getenv("OPENAI_TIMEOUT_SECONDS", "40"))
TRANSCRIPTION_MODEL = os.getenv("OPENAI_TRANSCRIPTION_MODEL", "gpt-4o-mini-transcribe")
DEV_TOKEN = os.getenv("THINKING_SPACE_DEV_TOKEN", "")
MAX_REQUESTS_PER_MINUTE = int(os.getenv("MAX_REQUESTS_PER_MINUTE", "10"))
IDEA_MODEL = os.getenv("OPENAI_IDEA_MODEL", "gpt-5-mini")
IDEA_TIMEOUT_SECONDS = float(os.getenv("OPENAI_IDEA_TIMEOUT_SECONDS", "45"))
MAX_IDEA_REQUEST_BYTES = int(os.getenv("MAX_IDEA_REQUEST_BYTES", str(32 * 1024)))
MAX_IDEA_REQUESTS_PER_MINUTE = int(os.getenv("MAX_IDEA_REQUESTS_PER_MINUTE", "20"))
INTENT_MODEL = os.getenv("OPENAI_INTENT_MODEL", IDEA_MODEL)
INTENT_TIMEOUT_SECONDS = float(os.getenv("OPENAI_INTENT_TIMEOUT_SECONDS", "30"))
MAX_INTENT_REQUEST_BYTES = int(os.getenv("MAX_INTENT_REQUEST_BYTES", str(32 * 1024)))
MAX_INTENT_REQUESTS_PER_MINUTE = int(os.getenv("MAX_INTENT_REQUESTS_PER_MINUTE", "30"))
CLARIFICATION_EXPIRATION_SECONDS = float(os.getenv("CLARIFICATION_EXPIRATION_SECONDS", "120"))
MAX_PENDING_CLARIFICATIONS = int(os.getenv("MAX_PENDING_CLARIFICATIONS", "128"))
ELEVENLABS_TTS_MODEL = os.getenv("ELEVENLABS_TTS_MODEL", "eleven_flash_v2_5")
ELEVENLABS_TTS_FALLBACK_MODEL = os.getenv("ELEVENLABS_TTS_FALLBACK_MODEL", "eleven_flash_v2_5")
ELEVENLABS_EXPRESSIVE_TTS = os.getenv("ELEVENLABS_EXPRESSIVE_TTS", "false").lower() in {
    "1", "true", "yes", "on"}
ELEVENLABS_TIMEOUT_SECONDS = float(os.getenv("ELEVENLABS_TIMEOUT_SECONDS", "20"))
MAX_TTS_TEXT_CHARACTERS = int(os.getenv("MAX_TTS_TEXT_CHARACTERS", "2000"))
MAX_TTS_REQUEST_BYTES = int(os.getenv("MAX_TTS_REQUEST_BYTES", str(16 * 1024)))
MAX_TTS_REQUESTS_PER_MINUTE = int(os.getenv("MAX_TTS_REQUESTS_PER_MINUTE", "20"))
MAX_TTS_AUDIO_BYTES = int(os.getenv("MAX_TTS_AUDIO_BYTES", str(5 * 1024 * 1024)))
CONVERSATION_MODEL = os.getenv("OPENAI_CONVERSATION_MODEL", INTENT_MODEL)
ORBY_HUMOR_ENABLED = os.getenv("ORBY_HUMOR_ENABLED", "true").lower() in {
    "1", "true", "yes", "on"}
CONVERSATION_TIMEOUT_SECONDS = float(os.getenv("OPENAI_CONVERSATION_TIMEOUT_SECONDS", "35"))
MAX_CONVERSATION_REQUEST_BYTES = int(os.getenv("MAX_CONVERSATION_REQUEST_BYTES", str(48 * 1024)))
MAX_CONVERSATION_REQUESTS_PER_MINUTE = int(os.getenv("MAX_CONVERSATION_REQUESTS_PER_MINUTE", "30"))
IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
app = FastAPI(title="Thinking Space Backend", version="1.4.0")
request_times: deque[float] = deque()
rate_limit_lock = asyncio.Lock()
idea_request_times: deque[float] = deque()
idea_rate_limit_lock = asyncio.Lock()
intent_request_times: deque[float] = deque()
intent_rate_limit_lock = asyncio.Lock()
voice_request_times: deque[float] = deque()
voice_rate_limit_lock = asyncio.Lock()
conversation_request_times: deque[float] = deque()
conversation_rate_limit_lock = asyncio.Lock()
idea_request_registry = IdeaRequestRegistry()
intent_request_registry = IdeaRequestRegistry()
voice_request_registry = IdeaRequestRegistry()
conversation_request_registry = IdeaRequestRegistry()
idea_manager = IdeaManager(
    OpenAIIdeaProvider(api_key=os.getenv("OPENAI_API_KEY", ""), model=IDEA_MODEL),
    timeout_seconds=IDEA_TIMEOUT_SECONDS,
)
intent_router = IntentRouter(
    OpenAIIntentProvider(api_key=os.getenv("OPENAI_API_KEY", ""), model=INTENT_MODEL),
    timeout_seconds=INTENT_TIMEOUT_SECONDS,
    clarification_store=ClarificationStore(
        expiration_seconds=CLARIFICATION_EXPIRATION_SECONDS,
        maximum_attempts=2,
        maximum_sessions=MAX_PENDING_CLARIFICATIONS),
)
voice_provider = ElevenLabsVoiceProvider(
    api_key=os.getenv("ELEVENLABS_API_KEY", ""),
    voice_id=os.getenv("ELEVENLABS_VOICE_ID", ""),
    model=ELEVENLABS_TTS_MODEL,
    timeout_seconds=ELEVENLABS_TIMEOUT_SECONDS,
    maximum_audio_bytes=MAX_TTS_AUDIO_BYTES,
    fallback_model=ELEVENLABS_TTS_FALLBACK_MODEL,
    expressive_enabled=ELEVENLABS_EXPRESSIVE_TTS,
)
conversation_orchestrator = ConversationOrchestrator(
    OpenAIConversationProvider(api_key=os.getenv("OPENAI_API_KEY", ""), model=CONVERSATION_MODEL,
                               humor_enabled=ORBY_HUMOR_ENABLED),
    timeout_seconds=CONVERSATION_TIMEOUT_SECONDS,
    clarification_store=ConversationClarificationStore(
        expiration_seconds=CLARIFICATION_EXPIRATION_SECONDS, maximum_attempts=2,
        maximum_sessions=MAX_PENDING_CLARIFICATIONS),
)

@app.post("/api/conversations/process")
async def process_conversation(request: Request,
        x_thinkingspace_token: str = Header(default="", alias="X-ThinkingSpace-Token")) -> JSONResponse:
    require_development_authentication(x_thinkingspace_token)
    try:
        await enforce_conversation_rate_limit()
    except HTTPException:
        return make_conversation_error(429, "Too many conversation requests; retry later.")
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "application/json":
        return make_conversation_error(415, "Content-Type must be application/json.")
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_CONVERSATION_REQUEST_BYTES:
                return make_conversation_error(413, "Conversation request is too large.")
        except ValueError:
            return make_conversation_error(400, "Content-Length is invalid.")
    try:
        body = await read_limited_body(request, MAX_CONVERSATION_REQUEST_BYTES,
                                       "Conversation request is too large.")
        conversation_request = ConversationRequest.model_validate_json(body)
    except HTTPException:
        return make_conversation_error(413, "Conversation request is too large.")
    except ValidationError as exc:
        field_path, category = safe_validation_issue(exc)
        logger.warning("conversation_request_validation_failed stage=request_schema status=422 "
                       "field=%s category=%s", field_path, category)
        return make_conversation_error(422, "Conversation request validation failed.",
            validation_stage="request_schema", validation_field=field_path,
            validation_category=category)
    except ValueError:
        logger.warning("conversation_request_validation_failed stage=request_json status=422 "
                       "field=$ category=value_error")
        return make_conversation_error(422, "Conversation request validation failed.",
            validation_stage="request_json", validation_field="$",
            validation_category="value_error")
    conflict = await conversation_request_registry.claim(
        conversation_request.request_id, conversation_request.session_id)
    if conflict == "duplicate":
        return make_conversation_error(409, "Duplicate conversation request.", conversation_request)
    if conflict == "overlap":
        return make_conversation_error(409,
            "Another conversation request is already active for this session.", conversation_request)
    completed = False
    try:
        result = await conversation_orchestrator.process(conversation_request)
        completed = True
        return JSONResponse(status_code=200, content=result.model_dump(mode="json"))
    except ConversationTimeout:
        await conversation_orchestrator.clear_failed_clarification(conversation_request)
        return make_conversation_error(504, "Conversation provider timed out.", conversation_request)
    except ConversationMalformedOutput as exc:
        await conversation_orchestrator.clear_failed_clarification(conversation_request)
        logger.warning("conversation_output_invalid stage=%s status=502 field=%s category=%s "
                       "completion=%s incomplete_reason=%s output_tokens=%s reasoning_tokens=%s "
                       "output_characters=%s parse_attempted=%s",
                       exc.stage, exc.field_path, exc.category, exc.completion_status,
                       exc.incomplete_reason, exc.output_tokens, exc.reasoning_tokens,
                       exc.output_characters, exc.structured_parse_attempted)
        return make_conversation_error(502,
            "Conversation provider returned an invalid structured response.", conversation_request,
            failure_stage=exc.stage, failure_field=exc.field_path,
            failure_category=exc.category, completion_status=exc.completion_status,
            incomplete_reason=exc.incomplete_reason, output_tokens=exc.output_tokens,
            reasoning_tokens=exc.reasoning_tokens, output_characters=exc.output_characters,
            structured_parse_attempted=exc.structured_parse_attempted)
    except ConversationContextError:
        return make_conversation_error(409,
            "Conversation clarification is missing, expired, or belongs to another session.",
            conversation_request)
    except ConversationProviderFailure as exc:
        await conversation_orchestrator.clear_failed_clarification(conversation_request)
        logger.warning("conversation_provider_failed stage=%s status=502 field=%s category=%s",
                       exc.stage, exc.field_path, exc.category)
        return make_conversation_error(502, "Conversation provider request failed.", conversation_request,
            failure_stage=exc.stage, failure_field=exc.field_path,
            failure_category=exc.category)
    except Exception:
        await conversation_orchestrator.clear_failed_clarification(conversation_request)
        return make_conversation_error(500, "Conversation processing failed.", conversation_request)
    finally:
        await conversation_request_registry.finish(
            conversation_request.request_id, conversation_request.session_id, completed)

@app.get("/health")
async def health() -> JSONResponse:
    missing_configuration = []
    if not os.getenv("OPENAI_API_KEY"):
        missing_configuration.append("OPENAI_API_KEY")
    if not DEV_TOKEN:
        missing_configuration.append("THINKING_SPACE_DEV_TOKEN")
    if missing_configuration:
        return JSONResponse(status_code=503, content={
            "status": "not_ready",
            "missing_configuration": missing_configuration,
        })
    return JSONResponse({"status": "ok"})

@app.post("/api/voice/synthesize")
async def synthesize_voice(request: Request,
                           x_thinkingspace_token: str = Header(default="", alias="X-ThinkingSpace-Token")) -> Response:
    require_development_authentication(x_thinkingspace_token)
    try:
        await enforce_voice_rate_limit()
    except HTTPException:
        return make_voice_error(429, "Too many voice requests; retry later.")
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "application/json":
        return make_voice_error(415, "Content-Type must be application/json.")
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_TTS_REQUEST_BYTES:
                return make_voice_error(413, "Voice request is too large.")
        except ValueError:
            return make_voice_error(400, "Content-Length is invalid.")
    try:
        body = await read_limited_body(request, MAX_TTS_REQUEST_BYTES, "Voice request is too large.")
    except HTTPException:
        return make_voice_error(413, "Voice request is too large.")
    try:
        voice_request = VoiceSynthesisRequest.model_validate_json(body)
    except (ValidationError, ValueError):
        return make_voice_error(422, "Voice request validation failed.")
    if len(voice_request.text) > MAX_TTS_TEXT_CHARACTERS:
        return make_voice_error(422, "Voice text exceeds the configured character limit.", voice_request)
    conflict = await voice_request_registry.claim(voice_request.request_id, voice_request.session_id)
    if conflict == "duplicate":
        return make_voice_error(409, "Duplicate voice request.", voice_request)
    if conflict == "overlap":
        return make_voice_error(409, "Another voice request is already active for this session.", voice_request)
    completed = False
    try:
        synthesis = await asyncio.wait_for(
            voice_provider.synthesize(voice_request.text, voice_request.delivery),
            timeout=ELEVENLABS_TIMEOUT_SECONDS)
        audio = synthesis.audio
        if len(audio) > MAX_TTS_AUDIO_BYTES:
            raise VoiceAudioTooLarge("Generated voice audio exceeded the configured limit.")
        if not is_probable_mp3(audio):
            raise VoiceInvalidAudio("Voice provider returned invalid audio.")
        completed = True
        return Response(content=audio, media_type="audio/mpeg", headers={
            "Cache-Control": "no-store",
            "X-ThinkingSpace-Request-ID": voice_request.request_id,
            "X-ThinkingSpace-Session-ID": quote(voice_request.session_id, safe="._:-"),
            "X-ThinkingSpace-Audio-Format": "mp3_44100_128",
            "X-ThinkingSpace-Voice-Model": synthesis.model,
            "X-ThinkingSpace-Voice-Fallback": "true" if synthesis.used_fallback else "false",
            "X-ThinkingSpace-Voice-Cache": voice_provider.cache_identity,
        })
    except VoiceConfigurationError:
        return make_voice_error(503, "Voice service is not configured.", voice_request)
    except (VoiceProviderTimeout, asyncio.TimeoutError):
        return make_voice_error(504, "Voice provider timed out.", voice_request)
    except VoiceAudioTooLarge:
        return make_voice_error(502, "Voice provider returned audio that is too large.", voice_request)
    except VoiceInvalidAudio:
        return make_voice_error(502, "Voice provider returned invalid audio.", voice_request)
    except VoiceProviderFailure:
        return make_voice_error(502, "Voice provider request failed.", voice_request)
    except Exception:
        return make_voice_error(500, "Voice synthesis failed.", voice_request)
    finally:
        await voice_request_registry.finish(voice_request.request_id, voice_request.session_id, completed)

@app.post("/api/intents/route")
async def route_intent(request: Request,
                       x_thinkingspace_token: str = Header(default="", alias="X-ThinkingSpace-Token")) -> JSONResponse:
    require_development_authentication(x_thinkingspace_token)
    try:
        await enforce_intent_rate_limit()
    except HTTPException:
        return make_intent_error(429, "Too many intent-routing requests; retry later.")
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "application/json":
        return make_intent_error(415, "Content-Type must be application/json.")
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_INTENT_REQUEST_BYTES:
                return make_intent_error(413, "Intent request is too large.")
        except ValueError:
            return make_intent_error(400, "Content-Length is invalid.")
    try:
        body = await read_limited_body(request, MAX_INTENT_REQUEST_BYTES, "Intent request is too large.")
    except HTTPException as exc:
        return make_intent_error(exc.status_code, "Intent request is too large.")
    try:
        intent_request = IntentRouteRequest.model_validate_json(body)
    except (ValidationError, ValueError):
        return make_intent_error(422, "Intent request validation failed.")

    conflict = await intent_request_registry.claim(intent_request.request_id, intent_request.session_id)
    if conflict == "duplicate":
        return make_intent_error(409, "Duplicate intent request.", intent_request)
    if conflict == "overlap":
        return make_intent_error(409, "Another intent request is already active for this session.", intent_request)

    completed = False
    try:
        result = await intent_router.route(intent_request)
        completed = True
        return JSONResponse(status_code=200, content=result.model_dump(mode="json"))
    except IntentRouterTimeout:
        return make_intent_error(504, "Intent router timed out.", intent_request)
    except IntentRouterMalformedOutput:
        return make_intent_error(502, "Intent router returned an invalid structured response.", intent_request)
    except IntentRouterProviderFailure:
        return make_intent_error(502, "Intent router provider request failed.", intent_request)
    except Exception:
        return make_intent_error(500, "Intent routing failed.", intent_request)
    finally:
        await intent_request_registry.finish(intent_request.request_id, intent_request.session_id, completed)

@app.post("/api/ideas/process")
async def process_idea(request: Request,
                       x_thinkingspace_token: str = Header(default="", alias="X-ThinkingSpace-Token")) -> JSONResponse:
    require_development_authentication(x_thinkingspace_token)
    try:
        await enforce_idea_rate_limit()
    except HTTPException:
        return make_idea_error(429, "Too many AI manager requests; retry later.")
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "application/json":
        return make_idea_error(415, "Content-Type must be application/json.")
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_IDEA_REQUEST_BYTES:
                return make_idea_error(413, "Idea request is too large.")
        except ValueError:
            return make_idea_error(400, "Content-Length is invalid.")
    try:
        body = await read_limited_body(request, MAX_IDEA_REQUEST_BYTES, "Idea request is too large.")
    except HTTPException as exc:
        return make_idea_error(exc.status_code, "Idea request is too large.")
    try:
        idea_request = IdeaProcessRequest.model_validate_json(body)
    except (ValidationError, ValueError):
        return make_idea_error(422, "Idea request validation failed.")

    conflict = await idea_request_registry.claim(idea_request.request_id, idea_request.session_id)
    if conflict == "duplicate":
        return make_idea_error(409, "Duplicate idea request.", idea_request)
    if conflict == "overlap":
        return make_idea_error(409, "Another idea request is already active for this session.", idea_request)

    completed = False
    try:
        result = await idea_manager.process(idea_request)
        completed = True
        return JSONResponse(status_code=200, content=result.model_dump(mode="json"))
    except IdeaManagerTimeout:
        return make_idea_error(504, "AI manager timed out.", idea_request)
    except IdeaManagerMalformedOutput:
        return make_idea_error(502, "AI manager returned an invalid structured response.", idea_request)
    except IdeaManagerProviderFailure:
        return make_idea_error(502, "AI manager provider request failed.", idea_request)
    except Exception:
        return make_idea_error(500, "AI manager request failed.", idea_request)
    finally:
        await idea_request_registry.finish(idea_request.request_id, idea_request.session_id, completed)

@app.post("/api/transcriptions")
async def transcribe(request: Request, x_recording_id: str = Header(default=""),
                     x_session_id: str = Header(default=""),
                     x_thinkingspace_token: str = Header(default="", alias="X-ThinkingSpace-Token")) -> JSONResponse:
    require_development_authentication(x_thinkingspace_token)
    await enforce_rate_limit()
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "audio/wav":
        raise HTTPException(status_code=415, detail="Content-Type must be audio/wav.")
    if not IDENTIFIER_PATTERN.fullmatch(x_recording_id):
        raise HTTPException(status_code=400, detail="A valid X-Recording-ID header is required.")
    if x_session_id and len(x_session_id) > 128:
        raise HTTPException(status_code=400, detail="X-Session-ID is too long.")
    content_length = request.headers.get("content-length")
    if content_length:
        try:
            if int(content_length) > MAX_WAV_BYTES:
                raise HTTPException(status_code=413, detail="WAV payload is too large.")
        except ValueError as exc:
            raise HTTPException(status_code=400, detail="Content-Length is invalid.") from exc
    wav_bytes = await read_limited_body(request)
    if len(wav_bytes) <= 44:
        raise HTTPException(status_code=400, detail="WAV payload is empty.")
    if len(wav_bytes) > MAX_WAV_BYTES:
        raise HTTPException(status_code=413, detail="WAV payload is too large.")
    validate_wav(wav_bytes)
    api_key = os.getenv("OPENAI_API_KEY")
    if not api_key:
        raise HTTPException(status_code=503, detail="Transcription service is not configured.")
    client = AsyncOpenAI(api_key=api_key)
    try:
        response = await asyncio.wait_for(
            client.audio.transcriptions.create(model=TRANSCRIPTION_MODEL,
                file=("recording.wav", wav_bytes, "audio/wav"), response_format="json"),
            timeout=OPENAI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError as exc:
        raise HTTPException(status_code=504, detail="Transcription provider timed out.") from exc
    except Exception as exc:
        raise HTTPException(status_code=502, detail="Transcription provider request failed.") from exc
    finally:
        await client.close()
    raw_transcript = getattr(response, "text", "")
    if not raw_transcript or not raw_transcript.strip():
        raise HTTPException(status_code=502, detail="Transcription provider returned empty text.")
    return JSONResponse({
        # Legacy fields remain unchanged for the existing Unity client.
        "request_id": x_recording_id,
        "session_id": x_session_id,
        "model": TRANSCRIPTION_MODEL,
        "transcript": raw_transcript,
        # Structured fields for the future review/cleanup workflow.
        "recording_id": x_recording_id,
        "raw_transcript": raw_transcript,
        "cleaned_transcript": None,
        "transcription_model": TRANSCRIPTION_MODEL,
        "transcription_status": "completed",
    })

def require_development_authentication(provided_token: str) -> None:
    # Use one generic response for absent, incorrect, and unconfigured credentials. Never log tokens.
    configured = bool(DEV_TOKEN)
    matches = hmac.compare_digest(provided_token.encode("utf-8"), DEV_TOKEN.encode("utf-8"))
    if not configured or not provided_token or not matches:
        raise HTTPException(status_code=401, detail="Authentication required.")

async def enforce_rate_limit() -> None:
    if MAX_REQUESTS_PER_MINUTE <= 0:
        return
    now = time.monotonic()
    cutoff = now - 60.0
    async with rate_limit_lock:
        while request_times and request_times[0] <= cutoff:
            request_times.popleft()
        if len(request_times) >= MAX_REQUESTS_PER_MINUTE:
            raise HTTPException(status_code=429, detail="Too many transcription requests; retry later.")
        request_times.append(now)

async def enforce_idea_rate_limit() -> None:
    if MAX_IDEA_REQUESTS_PER_MINUTE <= 0:
        return
    now = time.monotonic()
    cutoff = now - 60.0
    async with idea_rate_limit_lock:
        while idea_request_times and idea_request_times[0] <= cutoff:
            idea_request_times.popleft()
        if len(idea_request_times) >= MAX_IDEA_REQUESTS_PER_MINUTE:
            raise HTTPException(status_code=429, detail="Too many AI manager requests; retry later.")
        idea_request_times.append(now)

async def enforce_intent_rate_limit() -> None:
    if MAX_INTENT_REQUESTS_PER_MINUTE <= 0:
        return
    now = time.monotonic()
    cutoff = now - 60.0
    async with intent_rate_limit_lock:
        while intent_request_times and intent_request_times[0] <= cutoff:
            intent_request_times.popleft()
        if len(intent_request_times) >= MAX_INTENT_REQUESTS_PER_MINUTE:
            raise HTTPException(status_code=429, detail="Too many intent-routing requests; retry later.")
        intent_request_times.append(now)

async def enforce_voice_rate_limit() -> None:
    if MAX_TTS_REQUESTS_PER_MINUTE <= 0:
        return
    now = time.monotonic()
    cutoff = now - 60.0
    async with voice_rate_limit_lock:
        while voice_request_times and voice_request_times[0] <= cutoff:
            voice_request_times.popleft()
        if len(voice_request_times) >= MAX_TTS_REQUESTS_PER_MINUTE:
            raise HTTPException(status_code=429, detail="Too many voice requests; retry later.")
        voice_request_times.append(now)

async def enforce_conversation_rate_limit() -> None:
    if MAX_CONVERSATION_REQUESTS_PER_MINUTE <= 0:
        return
    now = time.monotonic()
    cutoff = now - 60.0
    async with conversation_rate_limit_lock:
        while conversation_request_times and conversation_request_times[0] <= cutoff:
            conversation_request_times.popleft()
        if len(conversation_request_times) >= MAX_CONVERSATION_REQUESTS_PER_MINUTE:
            raise HTTPException(status_code=429, detail="Too many conversation requests; retry later.")
        conversation_request_times.append(now)

async def read_limited_body(request: Request, maximum_bytes: int = MAX_WAV_BYTES,
                            too_large_message: str = "WAV payload is too large.") -> bytes:
    body = bytearray()
    async for chunk in request.stream():
        if len(body) + len(chunk) > maximum_bytes:
            raise HTTPException(status_code=413, detail=too_large_message)
        body.extend(chunk)
    return bytes(body)

def make_idea_error(status_code: int, message: str,
                    idea_request: IdeaProcessRequest | None = None) -> JSONResponse:
    _, response = error_response(status_code, message, idea_request)
    return JSONResponse(status_code=status_code, content=response.model_dump(mode="json"))

def make_intent_error(status_code: int, message: str,
                      intent_request: IntentRouteRequest | None = None) -> JSONResponse:
    response = intent_error_response(message, intent_request)
    return JSONResponse(status_code=status_code, content=response.model_dump(mode="json"))

def make_voice_error(status_code: int, message: str,
                     voice_request: VoiceSynthesisRequest | None = None) -> JSONResponse:
    return JSONResponse(status_code=status_code, content={
        "request_id": voice_request.request_id if voice_request else "",
        "session_id": voice_request.session_id if voice_request else "",
        "status": "error",
        "error": message,
    })

def make_conversation_error(status_code: int, message: str,
        conversation_request: ConversationRequest | None = None,
        validation_stage: str | None = None, validation_field: str | None = None,
        validation_category: str | None = None, failure_stage: str | None = None,
        failure_field: str | None = None, failure_category: str | None = None,
        completion_status: str | None = None, incomplete_reason: str | None = None,
        output_tokens: int | None = None, reasoning_tokens: int | None = None,
        output_characters: int | None = None,
        structured_parse_attempted: bool | None = None) -> JSONResponse:
    response = conversation_error_response(message, conversation_request)
    headers = None
    if validation_stage and validation_field and validation_category:
        headers = {
            "X-ThinkingSpace-Validation-Stage": _safe_diagnostic_header(validation_stage),
            "X-ThinkingSpace-Validation-Field": _safe_diagnostic_header(validation_field),
            "X-ThinkingSpace-Validation-Category": _safe_diagnostic_header(validation_category),
            "X-ThinkingSpace-Validation-Status": str(status_code),
        }
    elif failure_stage and failure_field and failure_category:
        headers = {
            "X-ThinkingSpace-Failure-Stage": _safe_diagnostic_header(failure_stage),
            "X-ThinkingSpace-Failure-Field": _safe_diagnostic_header(failure_field),
            "X-ThinkingSpace-Failure-Category": _safe_diagnostic_header(failure_category),
            "X-ThinkingSpace-Failure-Status": str(status_code),
        }
        if completion_status is not None:
            headers["X-ThinkingSpace-Completion-Status"] = _safe_diagnostic_header(completion_status)
        if incomplete_reason is not None:
            headers["X-ThinkingSpace-Incomplete-Reason"] = _safe_diagnostic_header(incomplete_reason)
        if output_tokens is not None:
            headers["X-ThinkingSpace-Output-Tokens"] = str(max(0, output_tokens))
        if reasoning_tokens is not None:
            headers["X-ThinkingSpace-Reasoning-Tokens"] = str(max(0, reasoning_tokens))
        if output_characters is not None:
            headers["X-ThinkingSpace-Output-Characters"] = str(max(0, output_characters))
        if structured_parse_attempted is not None:
            headers["X-ThinkingSpace-Structured-Parse-Attempted"] = (
                "true" if structured_parse_attempted else "false")
    return JSONResponse(status_code=status_code, content=response.model_dump(mode="json"),
                        headers=headers)

def safe_validation_issue(exc: ValidationError) -> tuple[str, str]:
    errors = exc.errors(include_url=False, include_context=False, include_input=False)
    if not errors:
        return "$", "validation_error"
    first = errors[0]
    location = first.get("loc", ())
    field_path = ".".join(str(part) for part in location) if location else "$"
    category = str(first.get("type", "validation_error"))
    return _safe_diagnostic_header(field_path), _safe_diagnostic_header(category)

def _safe_diagnostic_header(value: str) -> str:
    sanitized = re.sub(r"[^A-Za-z0-9_.$\[\]-]", "_", value)
    return sanitized[:160] or "unknown"

def validate_wav(data: bytes) -> None:
    try:
        with wave.open(io.BytesIO(data), "rb") as reader:
            if reader.getcomptype() != "NONE" or reader.getsampwidth() != 2:
                raise ValueError("WAV must contain uncompressed 16-bit PCM.")
            if reader.getnchannels() <= 0 or reader.getframerate() <= 0 or reader.getnframes() <= 0:
                raise ValueError("WAV metadata is invalid or empty.")
    except (wave.Error, EOFError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
