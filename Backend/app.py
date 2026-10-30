import asyncio
import io
import os
import re
import wave

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse
from openai import AsyncOpenAI
from dotenv import load_dotenv

load_dotenv()
MAX_WAV_BYTES = int(os.getenv("MAX_WAV_BYTES", str(15 * 1024 * 1024)))
OPENAI_TIMEOUT_SECONDS = float(os.getenv("OPENAI_TIMEOUT_SECONDS", "40"))
TRANSCRIPTION_MODEL = os.getenv("OPENAI_TRANSCRIPTION_MODEL", "gpt-4o-mini-transcribe")
IDENTIFIER_PATTERN = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
app = FastAPI(title="Thinking Space Transcription Backend", version="1.0.0")

@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}

@app.post("/api/transcriptions")
async def transcribe(request: Request, x_recording_id: str = Header(default=""),
                     x_session_id: str = Header(default="")) -> JSONResponse:
    if request.headers.get("content-type", "").split(";", 1)[0].lower() != "audio/wav":
        raise HTTPException(status_code=415, detail="Content-Type must be audio/wav.")
    if not IDENTIFIER_PATTERN.fullmatch(x_recording_id):
        raise HTTPException(status_code=400, detail="A valid X-Recording-ID header is required.")
    if x_session_id and len(x_session_id) > 128:
        raise HTTPException(status_code=400, detail="X-Session-ID is too long.")
    content_length = request.headers.get("content-length")
    if content_length and int(content_length) > MAX_WAV_BYTES:
        raise HTTPException(status_code=413, detail="WAV payload is too large.")
    wav_bytes = await request.body()
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
    transcript = getattr(response, "text", "")
    if not transcript or not transcript.strip():
        raise HTTPException(status_code=502, detail="Transcription provider returned empty text.")
    return JSONResponse({"request_id": x_recording_id, "session_id": x_session_id,
        "model": TRANSCRIPTION_MODEL, "transcript": transcript.strip()})

def validate_wav(data: bytes) -> None:
    try:
        with wave.open(io.BytesIO(data), "rb") as reader:
            if reader.getcomptype() != "NONE" or reader.getsampwidth() != 2:
                raise ValueError("WAV must contain uncompressed 16-bit PCM.")
            if reader.getnchannels() <= 0 or reader.getframerate() <= 0 or reader.getnframes() <= 0:
                raise ValueError("WAV metadata is invalid or empty.")
    except (wave.Error, EOFError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
