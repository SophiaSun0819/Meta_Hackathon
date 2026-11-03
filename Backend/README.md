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

Generate a separate, high-entropy development token. For example:

```powershell
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$token = [Convert]::ToBase64String($bytes)
$token
```

Copy `.env.example` to `.env`, then set both `OPENAI_API_KEY` and
`THINKING_SPACE_DEV_TOKEN` in `.env`. Do not put either value in `.env.example`, Unity assets,
source code, command output committed to Git, or screenshots. `Backend/.env` is ignored by Git.
Restart Uvicorn whenever the token changes because backend configuration is loaded at startup.

Start with:

```powershell
uvicorn app:app --host 0.0.0.0 --port 8000
```

Check `http://127.0.0.1:8000/health` from the development machine. `/health` intentionally does
not require authentication. It returns HTTP 200 with `{ "status": "ok" }` when both required
secrets are configured, or HTTP 503 with only the names of missing variables. It never returns values.

## Render Starter deployment preparation

The repository-root `render.yaml` defines a Python web service with:

- Root directory: `Backend`
- Python: `3.12.11`
- Plan: Render Starter
- Build command: `pip install -r requirements.txt`
- Start command: `uvicorn app:app --host 0.0.0.0 --port $PORT`
- Health check: `/health`

`app:app` is intentional: the first `app` is `app.py`, and the second is its FastAPI object.
`uvicorn app` alone is not a valid application target. The service has no Windows paths, does not
reference `.venv`, and does not require a persistent disk.

Set these required secrets in the Render dashboard, never in `render.yaml`:

- `OPENAI_API_KEY`
- `THINKING_SPACE_DEV_TOKEN` (temporary internal testing only; see the beta security section)

The Blueprint provides non-secret defaults for:

- `PYTHON_VERSION=3.12.11`
- `OPENAI_TRANSCRIPTION_MODEL=gpt-4o-mini-transcribe`
- `OPENAI_TIMEOUT_SECONDS=40`
- `MAX_WAV_BYTES=15728640`
- `MAX_REQUESTS_PER_MINUTE=10`

Render supplies `PORT`. Do not define it manually. Syncing the Blueprint or creating a service is a
deployment action; review all settings first and do not sync until the team approves deployment.

## Quest connectivity

The Quest cannot use `127.0.0.1` to reach the development PC. Keep both devices on the same
network. The recommended path is an HTTPS development tunnel to port 8000; configure the Unity
`SpeechTranscriptionService` component with `https://.../api/transcriptions`. This avoids shipping
cleartext HTTP permission. The transcription route always requires the development token, including
through a tunnel; a missing backend token does not disable authentication and instead makes the route
return HTTP 401.

### Configure the Quest token without committing it

The Unity client reads `dev-token.txt` from the app's persistent Thinking Space directory. Run the
app once so Android creates its files directory, save only the token in a local `dev-token.txt`, then:

```powershell
adb shell mkdir -p /sdcard/Android/data/com.meta.hackathon.thinkingspace/files/ThinkingSpace
adb push .\dev-token.txt /sdcard/Android/data/com.meta.hackathon.thinkingspace/files/ThinkingSpace/dev-token.txt
```

Do not place `dev-token.txt` under the Unity project. In the Unity Editor or a desktop development
build, the service can instead read `THINKING_SPACE_DEV_TOKEN` from the process environment. A future
development UI may call `SetDevelopmentTokenForCurrentRun`; it is deliberately not serialized.

Direct LAN HTTP can use the PC's private IPv4 address, such as
`http://192.168.1.25:8000/api/transcriptions`, but requires firewall access to TCP 8000 and an
explicit Android cleartext-network policy. Direct HTTP is development-only; hosted deployments
must use HTTPS.

## Contract

`POST /api/transcriptions`, `Content-Type: audio/wav`, headers `X-ThinkingSpace-Token`,
`X-Recording-ID`, and `X-Session-ID`, with complete WAV bytes as the body. Missing or incorrect
authentication receives the same HTTP 401 response. Authentication values are never included in
application errors or logs.

The request body is streamed into memory with a default 15 MiB maximum. Authenticated transcription
requests are limited to 10 per rolling 60-second window per backend process by default. Configure
`MAX_WAV_BYTES` and `MAX_REQUESTS_PER_MINUTE` in `.env`. A successful response contains
the legacy fields expected by Unity plus a forward-compatible structured result:

```json
{
  "request_id": "20261103T120000000Z-1",
  "session_id": "Session 1",
  "model": "gpt-4o-mini-transcribe",
  "transcript": "Exact raw model output.",
  "recording_id": "20261103T120000000Z-1",
  "raw_transcript": "Exact raw model output.",
  "cleaned_transcript": null,
  "transcription_model": "gpt-4o-mini-transcribe",
  "transcription_status": "completed"
}
```

`transcript` remains an alias of `raw_transcript`, so the existing Unity client continues working.
The backend checks for an empty/whitespace-only result but does not trim, rewrite, punctuate, or
otherwise mutate a nonempty model response.

### Future optional transcript cleanup

Cleanup is deliberately not invoked yet. A future, user-approved stage may populate
`cleaned_transcript` while retaining `raw_transcript` unchanged. It should correct punctuation and
obvious grammar only, preserve meaning and technical terminology, avoid adding ideas or facts, record
its own status/model metadata, and show both versions for review before anything is sent to the AI
manager. Cleanup must be independently retryable and must not silently replace the raw transcript.

## Authentication tests

The following examples use a small local WAV file. Keep the token in the PowerShell variable rather
than placing it in shell history as a literal value.

```powershell
$token = (Get-Content .env | Where-Object { $_ -like 'THINKING_SPACE_DEV_TOKEN=*' }).Split('=', 2)[1]

# Missing token: expect HTTP 401
curl.exe -i -X POST http://127.0.0.1:8000/api/transcriptions `
  -H "Content-Type: audio/wav" -H "X-Recording-ID: auth-test" --data-binary "@test.wav"

# Incorrect token: expect HTTP 401
curl.exe -i -X POST http://127.0.0.1:8000/api/transcriptions `
  -H "Content-Type: audio/wav" -H "X-Recording-ID: auth-test" `
  -H "X-ThinkingSpace-Token: incorrect" --data-binary "@test.wav"

# Correct token: authentication passes; a valid WAV proceeds to transcription
curl.exe -i -X POST http://127.0.0.1:8000/api/transcriptions `
  -H "Content-Type: audio/wav" -H "X-Recording-ID: auth-test" `
  -H "X-ThinkingSpace-Token: $token" --data-binary "@test.wav"
```

## Development security scope

This is lightweight development protection, not production authentication. The token is shared by
all development clients, the in-memory throttle is per process and resets on restart, audio is buffered
in memory, there is no user identity or authorization model, and a token stored in Quest app data can
be retrieved by someone with sufficient device access. Keep Uvicorn off the public Internet, expose it
only through the intended Cloudflare tunnel, rotate the token after sharing or suspected disclosure,
and use a production identity, rate-limit, audit, and secret-management system before deployment.

### Limited competition beta authentication

Do not distribute a public or broadly shared APK containing a permanent shared token. APK contents
can be inspected, and the current token file can be copied from a sufficiently accessible device.
A leaked shared token grants every holder the same access and cannot be revoked for only one tester.

For immediate internal teammate sideloading, use a short-lived shared token provisioned separately
from the APK, rotate it frequently, and keep the tester group small. Before wider beta distribution,
prefer this practical design:

1. Generate a separate random credential per tester/device.
2. Provision it out of band after installation, rather than embedding it in the APK.
3. Store only a hash/server-side identifier in a managed datastore.
4. Allow individual revocation, expiration, and per-device rate/usage limits.
5. Exchange a one-time enrollment code for short-lived access tokens; store refresh credentials using
   Android Keystore-backed storage when that integration is approved.

Cloudflare Access can add an outer gate for team-operated testing, but a service credential embedded
in an APK has the same extraction problem and should not be treated as end-user authentication.

## Deployment checklist

Before the first Render deployment:

- Confirm the service source branch and disable automatic deployment until the deployment owner is ready.
- Select the Starter plan and the region closest to the competition testers.
- Enter `OPENAI_API_KEY` and the temporary beta credential as secret environment variables.
- Confirm `/health` returns 200; a 503 means required configuration is missing.
- Run one authenticated WAV request and verify the structured response and existing Unity display.
- Update Unity's backend URL only in an approved build after the Render hostname is final.
- Confirm the token provisioning method for every teammate APK; never commit or bake the token in.
- Set OpenAI project budgets/usage alerts and restrict the API key to the intended project where available.
- Set Render billing alerts and identify who owns renewal, incident response, and credential rotation.
- Review `MAX_WAV_BYTES`, request timeout, and rate limits against expected recording length and tester count.
- Verify application and proxy logs do not include headers, audio bodies, transcripts, or API responses.
- Enable Render deploy-failure/service notifications and check logs/health regularly during the beta.
- Schedule monthly dependency and credential review, plus token rotation after any suspected disclosure.
- Keep a rollback plan and retain the last known-good deployment.
- Test service availability and a real Quest transcription before every distributed APK release.

No database or disk is currently used. Transcripts and audio are not intentionally persisted by this
service. The in-memory limiter is process-local, resets on restart, and is not sufficient for multiple
instances or hostile traffic.
