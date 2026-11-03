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

load_dotenv()
MAX_WAV_BYTES = int(os.getenv("MAX_WAV_BYTES", str(15 * 1024 * 1024)))
OPENAI_TIMEOUT_SECONDS = float(os.getenv("OPENAI_TIMEOUT_SECONDS", "40"))
TRANSCRIPTION_MODEL = os.getenv("OPENAI_TRANSCRIPTION_MODEL", "gpt-4o-mini-transcribe")
DEV_TOKEN = os.getenv("THINKING_SPACE_DEV_TOKEN", "")
MAX_REQUESTS_PER_MINUTE = int(os.getenv("MAX_REQUESTS_PER_MINUTE", "10"))
IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
app = FastAPI(title="Thinking Space Transcription Backend", version="1.0.0")
request_times: deque[float] = deque()
rate_limit_lock = asyncio.Lock()

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

async def read_limited_body(request: Request) -> bytes:
    body = bytearray()
    async for chunk in request.stream():
        if len(body) + len(chunk) > MAX_WAV_BYTES:
            raise HTTPException(status_code=413, detail="WAV payload is too large.")
        body.extend(chunk)
    return bytes(body)

def validate_wav(data: bytes) -> None:
    try:
        with wave.open(io.BytesIO(data), "rb") as reader:
            if reader.getcomptype() != "NONE" or reader.getsampwidth() != 2:
                raise ValueError("WAV must contain uncompressed 16-bit PCM.")
            if reader.getnchannels() <= 0 or reader.getframerate() <= 0 or reader.getnframes() <= 0:
                raise ValueError("WAV metadata is invalid or empty.")
    except (wave.Error, EOFError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
