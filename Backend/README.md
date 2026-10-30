# Thinking Space transcription backend

This development service accepts an in-memory 16-bit PCM WAV request, forwards it to OpenAI,
and returns JSON. It does not persist uploaded audio or transcripts.

## Setup

```powershell
cd Backend
py -m venv .venv
.venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item .env.example .env
```

Set `OPENAI_API_KEY` in the local `.env` file or process environment. The example contains no
credential and `.env` is ignored by Git. Start with:

```powershell
uvicorn app:app --host 0.0.0.0 --port 8000
```

Check `http://127.0.0.1:8000/health` from the development machine.

## Quest connectivity

The Quest cannot use `127.0.0.1` to reach the development PC. Keep both devices on the same
network. The recommended path is an HTTPS development tunnel to port 8000; configure the Unity
`SpeechTranscriptionService` component with `https://.../api/transcriptions`. This avoids shipping cleartext HTTP permission.

Direct LAN HTTP can use the PC's private IPv4 address, such as
`http://192.168.1.25:8000/api/transcriptions`, but requires firewall access to TCP 8000 and an
explicit Android cleartext-network policy. Direct HTTP is development-only; hosted deployments
must use HTTPS.

## Contract

`POST /api/transcriptions`, `Content-Type: audio/wav`, headers `X-Recording-ID` and
`X-Session-ID`, with complete WAV bytes as the body. The default limit is 15 MiB. A successful
response contains `request_id`, `session_id`, `model`, and `transcript` JSON fields.
