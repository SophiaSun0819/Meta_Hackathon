import asyncio
import hmac
import io
import os
import re
import time
import wave
from collections import deque

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse
from openai import AsyncOpenAI
from dotenv import load_dotenv
from pydantic import ValidationError

from idea_manager import (IdeaManager, IdeaManagerMalformedOutput, IdeaManagerProviderFailure,
    IdeaManagerTimeout, IdeaProcessRequest, IdeaRequestRegistry, OpenAIIdeaProvider, error_response)
from intent_router import (ClarificationStore, IntentRouteRequest, IntentRouter, IntentRouterMalformedOutput,
    IntentRouterProviderFailure, IntentRouterTimeout, OpenAIIntentProvider,
    error_response as intent_error_response)

load_dotenv()
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
IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
app = FastAPI(title="Thinking Space Backend", version="1.3.0")
request_times: deque[float] = deque()
rate_limit_lock = asyncio.Lock()
idea_request_times: deque[float] = deque()
idea_rate_limit_lock = asyncio.Lock()
intent_request_times: deque[float] = deque()
intent_rate_limit_lock = asyncio.Lock()
idea_request_registry = IdeaRequestRegistry()
intent_request_registry = IdeaRequestRegistry()
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

def validate_wav(data: bytes) -> None:
    try:
        with wave.open(io.BytesIO(data), "rb") as reader:
            if reader.getcomptype() != "NONE" or reader.getsampwidth() != 2:
                raise ValueError("WAV must contain uncompressed 16-bit PCM.")
            if reader.getnchannels() <= 0 or reader.getframerate() <= 0 or reader.getnframes() <= 0:
                raise ValueError("WAV metadata is invalid or empty.")
    except (wave.Error, EOFError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
