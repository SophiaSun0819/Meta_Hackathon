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
not require authentication. It returns HTTP 200 with `{ "status": "ok", "mode": "live" }` when both required
secrets are configured, or HTTP 503 with only the names of missing variables. It never returns values.
`mode` is `"mock"` when mock mode is on (see **Mock mode and fallback** below).

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
- `OPENAI_IDEA_MODEL=gpt-5-mini`
- `OPENAI_IDEA_TIMEOUT_SECONDS=45`
- `MAX_IDEA_REQUEST_BYTES=32768`
- `MAX_IDEA_REQUESTS_PER_MINUTE=20`
- `OPENAI_INTENT_MODEL=gpt-5-mini`
- `OPENAI_INTENT_TIMEOUT_SECONDS=30`
- `MAX_INTENT_REQUEST_BYTES=32768`
- `MAX_INTENT_REQUESTS_PER_MINUTE=30`

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

## AI Manager Milestone 1

The AI manager is a separate module in `idea_manager.py`. Transcription never calls it automatically.
The existing `/api/transcriptions` route and its raw/cleaned transcript fields remain independent.

`POST /api/ideas/process` accepts JSON and requires the existing `X-ThinkingSpace-Token` header.
The default JSON body limit is 32 KiB, the transcript limit is 8,000 characters, and the default
AI-manager rate limit is 20 authenticated requests per rolling minute per backend process.

The supported actions are:

- `create_idea`: creates a new concise idea from the selected transcript.
- `expand_idea`: creates a derived suggestion linked to, but not overwriting, `idea_id`.
- `challenge_idea`: creates constructive questions/risks linked to `idea_id`.
- `combine_ideas`: creates a new result linked to at least two `related_idea_ids`.

Every successful action receives a new backend-generated `idea_id`. The model cannot assign request
IDs, session IDs, idea IDs, actions, relationship IDs, status, or Unity scene operations. It returns
only validated `title`, `summary`, `category`, and `spoken_response` fields. The backend uses OpenAI
Structured Outputs and performs an additional Pydantic validation pass.

### Request contract

```json
{
  "request_id": "idea-request-001",
  "session_id": "Session 1",
  "action": "expand_idea",
  "raw_transcript": "Add community workshops to the solar workspace idea.",
  "cleaned_transcript": null,
  "transcript_source": "raw",
  "idea_id": "idea-original",
  "related_idea_ids": [],
  "existing_ideas": [
    {
      "idea_id": "idea-original",
      "title": "Solar Study Hub",
      "summary": "A solar-powered community workspace for students."
    }
  ]
}
```

`raw_transcript` is always retained as submitted. `transcript_source` defaults to `raw`. A cleaned
transcript is used only when the caller explicitly sends `transcript_source: "cleaned"` and supplies
a nonempty `cleaned_transcript`; it never overwrites the raw value.

Action-specific references:

- `create_idea`: omit `idea_id`; related IDs are optional.
- `expand_idea` / `challenge_idea`: supply a valid `idea_id`.
- `combine_ideas`: supply two to eight unique `related_idea_ids`.
- If `existing_ideas` is supplied, it must contain context for every referenced ID.

### Versioned response contract

```json
{
  "contract_version": "1.0",
  "request_id": "idea-request-001",
  "session_id": "Session 1",
  "action": "expand_idea",
  "idea_id": "idea_0123456789abcdef0123456789abcdef",
  "title": "Community Workshop Program",
  "summary": "Add scheduled peer-led workshops without changing the original hub idea.",
  "category": "Community",
  "related_idea_ids": ["idea-original"],
  "spoken_response": "You could expand the hub with peer-led community workshops.",
  "status": "completed",
  "error": null
}
```

Errors use the same response shape with `status: "error"`, null result fields, and a sanitized error.
Authentication failures retain the existing generic HTTP 401 behavior. Provider details, prompts,
credentials, and raw exceptions are never returned.

Duplicate `request_id` values are rejected for ten minutes after a successful request. Only one
AI-manager request per session is allowed in flight in a process. These guards are in memory and must
eventually move to shared storage if Render runs multiple instances.

### Model configuration

`OPENAI_IDEA_MODEL` selects the text model and defaults to `gpt-5-mini`. The OpenAI key remains
backend-only. `OPENAI_IDEA_TIMEOUT_SECONDS` controls the provider timeout. No custom model is trained,
and no cleanup, text-to-speech, subtitle, summarization, or Unity scene operation is performed.

### Mocked tests

The test suite makes no OpenAI requests:

```powershell
cd Backend
.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.venv\Scripts\python.exe -m unittest discover -s tests -v
```

`requirements-dev.txt` adds Starlette's current `httpx2` test transport. It is intentionally separate
from the Render production requirements. With FastAPI 0.162/Starlette 1.10, running `TestClient` with
only legacy `httpx` emits a deprecation warning and can hang rather than executing requests.

It covers all four actions, missing transcript, invalid IDs, invalid authentication, provider timeout,
malformed structured output, and duplicate request rejection.

### Future Unity integration

Unity should call the manager only after explicit user confirmation. On a completed response it may:

1. Store `idea_id` as application data, not as authority to execute code.
2. Render `title`, `summary`, and `category` into a newly created idea bubble.
3. Connect bubbles using `related_idea_ids` after verifying those IDs exist locally.
4. Send `spoken_response` to the future voice service and display the identical text as subtitles.
5. Ignore scene-operation language in text fields and use only fixed, client-owned rendering logic.

Unity must handle non-200 responses, `status: "error"`, timeouts, duplicate responses, unknown related
IDs, and contract versions it does not support. The backend never directly modifies Unity objects.

## Voice-command intent routing

`POST /api/intents/route` classifies an approved transcript before any idea or application action is
requested. It uses the existing development authentication and a separate request-size limit, rolling
rate limit, timeout, duplicate registry, and one-request-per-session overlap guard.

Supported intents:

- `create_idea`
- `expand_idea`
- `challenge_idea`
- `combine_ideas`
- `show_ideas`
- `resize_bubble`
- `move_bubble`
- `hide_bubble`
- `converse`
- `clarification_required`

Ordinary brainstorming statements can route to `create_idea`; not every transcript does. Questions
can route to `converse`, explicit application commands route to their corresponding semantic action,
and ambiguous references route to `clarification_required`.

### Routing request

```json
{
  "request_id": "route-request-001",
  "session_id": "Session 1",
  "raw_transcript": "Move the solar study hub closer.",
  "cleaned_transcript": null,
  "transcript_source": "raw",
  "available_ideas": [
    {
      "idea_id": "idea-solar-hub",
      "title": "Solar Study Hub",
      "summary": "A solar-powered community study space."
    }
  ]
}
```

`raw_transcript` remains unchanged. A cleaned transcript is routed only when the caller explicitly
selects it. The transcript can remain internal to the workflow; displaying it in the final user UI is
optional. The existing transcript panel remains useful for diagnostics and user correction.

### Companion routing response

```json
{
  "contract_version": "1.0",
  "request_id": "route-request-001",
  "session_id": "Session 1",
  "intent": "move_bubble",
  "action": "move_bubble",
  "target_idea_ids": ["idea-solar-hub"],
  "parameters": {
    "scale": null,
    "direction": "closer",
    "question": null
  },
  "spoken_response": "I'll bring the Solar Study Hub idea closer.",
  "subtitle_text": "I'll bring the Solar Study Hub idea closer.",
  "suggested_emotion": "attentive",
  "status": "completed",
  "error": null
}
```

The backend generates `spoken_response` and `subtitle_text` from one validated `response_text`, so
the two fields always match. No audio is generated.

### Command validation

- Target IDs must match IDs supplied in `available_ideas`; unknown IDs are never forwarded.
- Ambiguous, missing, or invented targets become `clarification_required` with no target IDs.
- Expand, challenge, resize, move, and hide require exactly one known target.
- Combine requires at least two known targets.
- Resize accepts only `smaller`, `larger`, `small`, `medium`, `large`, or `reset`.
- Movement accepts only `left`, `right`, `up`, `down`, `closer`, `farther`, or `reset_layout`.
- Coordinates, rotations, transforms, object names, file paths, and code are not parameters.
- The model cannot select values outside these strict enums.
- Every result is a proposed action. Unity remains responsible for authorization and execution.

Local controls—including thumbs-up recording, thumbs-down stopping, future sideways-thumb pause, and
Meta system gestures—never pass through the intent router and remain independent of AI output.

### Companion emotion contract

The allowed suggestions are `neutral`, `attentive`, `thinking`, `happy`, `excited`, `concerned`, and
`confused`. This value is advisory metadata only. Unity owns animation selection, transition timing,
safety, tracking, and fallback behavior. A clarification generated by backend validation uses
`confused`; errors use `concerned`.

### Router model configuration

- `OPENAI_INTENT_MODEL` defaults to `OPENAI_IDEA_MODEL`, currently `gpt-5-mini`.
- `OPENAI_INTENT_TIMEOUT_SECONDS` defaults to 30 seconds.
- `CLARIFICATION_EXPIRATION_SECONDS` defaults to 120 seconds.
- `MAX_PENDING_CLARIFICATIONS` defaults to 128 sessions.

### Conversational clarification

`POST /api/intents/route` accepts natural speech and does not require fixed command phrases. A clear,
reversible command returns immediately with `requires_confirmation: false`. When an action or bubble
reference is ambiguous, the response contains a `clarification_id`, `pending_action`, candidate idea IDs,
and a question. Submit the user's next utterance to the same endpoint with a new `request_id`, the same
`session_id`, and that `clarification_id`.

```json
{
  "request_id": "request-follow-up-2",
  "session_id": "Session 1",
  "raw_transcript": "The first one",
  "transcript_source": "raw",
  "clarification_id": "clar_0123456789abcdef",
  "available_ideas": [
    {"idea_id": "idea-one", "title": "Solar Hub", "summary": "A solar study hub."},
    {"idea_id": "idea-two", "title": "Workshop", "summary": "Peer-led workshops."}
  ]
}
```

The server interprets contextual yes/no, ordinal selections, idea names, “other,” “neither,” and cancel.
It validates every selected ID against the candidates before returning a final semantic action. Unity must
still authorize the action and resolve IDs to its own objects; the backend never sends coordinates, code,
or GameObject operations. `spoken_response` and `subtitle_text` are identical.

A yes response is accepted only when the pending candidates have the correct cardinality for the action.
In particular, yes cannot choose between multiple candidates for expand, challenge, resize, move, or hide;
the router asks again instead of forwarding several IDs to a single-target command.

Pending clarifications are session-scoped, expire after the configured interval, and are cancelled after
two unresolved answers. A new ambiguity for the same session replaces the older one. Clear reversible
actions do not require confirmation. Future destructive or irreversible actions must add an explicit
confirmation gate before being exposed.

This state is bounded but held in process memory. It is appropriate only for the current single-process
prototype: restarts lose pending questions, and multiple Render instances would not share them. Use a
shared store such as Redis with TTLs and atomic session keys before horizontal scaling or if recovery
across deploys is required. Do not log transcripts, tokens, or clarification payloads.
- `MAX_INTENT_REQUEST_BYTES` defaults to 32 KiB.
- `MAX_INTENT_REQUESTS_PER_MINUTE` defaults to 30 per process.

The provider uses strict OpenAI Structured Outputs and a second Pydantic validation pass. It can only
classify and propose semantic parameters; it has no tools and cannot manipulate Unity, GameObjects,
transforms, files, network resources, or arbitrary code.

### Future companion integration

Unity should implement the AI handoff as an explicit client-owned workflow:

1. Retain the completed transcription response and preserve `raw_transcript` exactly. A corrected
   `cleaned_transcript` remains a separate optional value.
2. After user approval, set Orby to `Thinking` and send the selected transcript plus the current session's
   locally known ideas to `POST /api/intents/route`.
3. Validate HTTP status, `contract_version`, `request_id`, `session_id`, status, target IDs, parameter enums,
   and the local allowlist before doing anything with the response.
4. For `clarification_required`, retain `clarification_id`, show/speak the question, return Orby to an
   attentive/listening state, and route the next approved utterance with the same session and a new request ID.
5. For `create_idea`, `expand_idea`, `challenge_idea`, or `combine_ideas`, build an
   `/api/ideas/process` request from the approved transcript and validated local idea context. Do not call the
   idea manager for spatial commands or ordinary conversation.
6. Validate the idea response and pass only its data contract to the future bubble manager. The bubble
   manager owns GameObject creation, transforms, relationships, and failure recovery.
7. Pass `spoken_response` to the future voice service and the same text to optional subtitles. Never treat
   either text field as executable instructions.
8. Treat `suggested_emotion` as advisory. Unity's Orby state controller owns animation and should transition
   from `Thinking` to future `Speaking` while voice plays, then to `Idle` or `Listening`. On timeout, invalid
   response, or cancellation, transition to `Error` and then a safe recoverable state.
9. Correlate every callback with the active request and session, ignore stale responses, and cancel or
   invalidate outstanding work when the session ends.

Clear spatial commands such as resize and move skip the idea-manager endpoint and go directly to the
client's allowlisted action validator. Bubble changes must use client-owned scale increments and movement
distances. The placeholder sphere and final model can consume the same client-side companion state
interface without changing this backend contract.

## Optional ElevenLabs voice synthesis

`POST /api/voice/synthesize` converts approved companion text to a complete MP3 response. It is optional:
missing ElevenLabs configuration does not affect `/health`, transcription, intent routing, or idea processing.
The route requires the same `X-ThinkingSpace-Token` development authentication as the other protected APIs.

Required server-side variables are `ELEVENLABS_API_KEY` and `ELEVENLABS_VOICE_ID`. Operational defaults are:

- `ELEVENLABS_TTS_MODEL=eleven_flash_v2_5`
- `ELEVENLABS_TIMEOUT_SECONDS=20`
- `MAX_TTS_TEXT_CHARACTERS=2000`
- `MAX_TTS_REQUEST_BYTES=16384`
- `MAX_TTS_REQUESTS_PER_MINUTE=20`
- `MAX_TTS_AUDIO_BYTES=5242880`

Unity cannot provide a voice ID, model, provider URL, output format, or credential. Those values are selected
only by server configuration. Output is fixed to ElevenLabs `mp3_44100_128`, returned as `audio/mpeg`; a future
Unity client can decode it using `UnityWebRequestMultimedia.GetAudioClip` with `AudioType.MPEG`. The full MP3 is
buffered before returning; streaming is not implemented.

```json
{
  "request_id": "voice-request-001",
  "session_id": "Session 1",
  "text": "I combined those ideas into a solar learning workshop."
}
```

A successful response is HTTP 200 with `Content-Type: audio/mpeg`, `Cache-Control: no-store`, safe request and
URL-escaped session correlation headers, `X-ThinkingSpace-Audio-Format: mp3_44100_128`, and MP3 bytes. Errors
return sanitized JSON. The backend does not log request text, credentials, or generated audio. Request bytes,
text length, response bytes, timeout, rate, duplicate IDs, and overlapping session requests are bounded.

### Controlled live ElevenLabs check

Run this only after an approved deployment and after Render contains both ElevenLabs secrets. Keep the
development token in an environment variable rather than writing it into the command:

```powershell
$body = @{ request_id = "voice-live-001"; session_id = "voice-smoke";
  text = "Hello, I am Orby. Voice synthesis is ready." } | ConvertTo-Json
Invoke-WebRequest -Method Post -Uri "https://YOUR-SERVICE.onrender.com/api/voice/synthesize" `
  -Headers @{ "X-ThinkingSpace-Token" = $env:THINKING_SPACE_DEV_TOKEN } `
  -ContentType "application/json" -Body $body -OutFile "$env:TEMP\thinking-space-orby-test.mp3"
```

Confirm the response MIME type, MP3 header, bounded file size, and exact spoken sentence. Begin with one request
to limit cost. Never paste keys, tokens, private text, or generated audio into Render logs or Git.

## Saved idea summary contract

Results that create saved idea content (`create_idea`, `expand_idea`, and `combine_ideas`) use a concise
plain-text summary contract:

- titles are preferably 3–7 words;
- summaries normally target 40–65 words and may be shorter for simple ideas;
- summaries have a hard maximum of 80 words;
- summaries are one cohesive paragraph with no bullets or Markdown; and
- summaries preserve the central concept, mechanism, purpose, and the user's original intent without
  adding unsupported features.

The server validates this deterministically before returning the result. An invalid model result receives
at most one corrective generation attempt. If that attempt is still invalid, the endpoint returns the
existing sanitized recoverable structured-output error; it never silently truncates or rewrites a summary.
Legacy `challenge_idea` responses are unchanged, and conversational challenges never create saved ideas.

Unity currently paginates idea details by character count. The 80-word limit should normally fit within two
current pages, but word count alone cannot guarantee a character-space limit for unusually long tokens,
titles, or categories. A later Unity milestone should paginate on rendered TMP layout (or enforce a separate
display-character budget) while preserving the current readable font size.

## Conversational Orby orchestration

`POST /api/conversations/process` is the backward-compatible conversational layer. It does not replace
`/api/intents/route` or `/api/ideas/process`; existing clients can continue using both unchanged. The route uses
the same `X-ThinkingSpace-Token` authentication and accepts a bounded client-owned context containing at most
12 recent turns, 32 saved ideas, the most recently discussed idea ID, and the current application mode.

```json
{
  "request_id": "conversation-001",
  "session_id": "Session 1",
  "raw_transcript": "What are the weaknesses in my anatomy idea?",
  "cleaned_transcript": null,
  "transcript_source": "raw",
  "available_ideas": [
    {"idea_id": "idea-anatomy", "title": "VR Anatomy Lab", "summary": "An immersive anatomy lesson."}
  ],
  "context": {
    "recent_turns": [],
    "most_recent_idea_id": "idea-anatomy",
    "current_mode": "conversational"
  },
  "clarification_id": null
}
```

The versioned response is data for Unity to validate, never an instruction executed by the server:

```json
{
  "contract_version": "1.0",
  "request_id": "conversation-001",
  "session_id": "Session 1",
  "intent_category": "conversational_idea_challenge",
  "proposed_action": "conversational_challenge",
  "target_idea_ids": ["idea-anatomy"],
  "parameters": {"scale": null, "direction": null, "settings_section": null},
  "spoken_response": "A key risk is cognitive overload if too many structures appear at once.",
  "subtitle_text": "A key risk is cognitive overload if too many structures appear at once.",
  "suggested_emotion": "concerned",
  "requires_confirmation": false,
  "clarification_id": null,
  "clarification_question": null,
  "candidate_idea_ids": [],
  "should_create_idea": false,
  "should_execute_action": false,
  "status": "completed",
  "error": null
}
```

Classification policy:

- General questions, casual discussion, and requested brainstorming suggestions never create bubbles.
- A clearly stated user-owned new idea or an explicit save request may return `should_create_idea: true`.
- A possible but ambiguous idea returns a clarification and cannot be saved before confirmation.
- Challenge, critique, weakness, risk, and subsequent discussion always use `conversational_challenge` and
  never return a new idea ID or request bubble creation. The legacy idea-manager challenge action remains intact.
- Expand and combine remain idea mutations, but Unity still decides whether and when to call `/api/ideas/process`.
- Spatial and application commands contain only allowlisted semantic parameters. Unity must authorize and execute
  them, then provide any completion acknowledgement itself.
- End-session and future delete actions always require confirmation and return `should_execute_action: false`
  until a confirmed follow-up is validated.

Clarifications are session-scoped, expire using `CLARIFICATION_EXPIRATION_SECONDS`, and stop after two unresolved
attempts. Only this small pending state is held in memory; recent conversation and saved ideas remain client-owned.
Render restarts lose pending clarifications, and multiple worker instances would require a shared TTL store such as
Redis. Raw transcript text and conversation turns are not logged by the application.

Operational settings:

- `OPENAI_CONVERSATION_MODEL=gpt-5-mini`
- `OPENAI_CONVERSATION_TIMEOUT_SECONDS=35`
- `MAX_CONVERSATION_REQUEST_BYTES=49152`
- `MAX_CONVERSATION_REQUESTS_PER_MINUTE=30`

### Conversational deployment checklist

1. Run the complete mocked suite under Python 3.12.
2. Add the four operational variables above in Render; keep existing secrets unchanged.
3. Deploy the backend-only commit to the existing Starter service.
4. Verify `/health`, transcription, intent routing, idea processing, and voice regression first.
5. Send one authenticated general-conversation request and confirm no idea creation is proposed.
6. Send one authenticated challenge request and confirm `should_create_idea` and `should_execute_action` are false.
7. Review Render latency, 4xx/5xx rates, OpenAI usage limits, and spend alerts before distributing a new APK.

For one controlled paid smoke test, use synthetic content and a unique request ID. Send “What is virtual reality?”
with an empty idea inventory, then verify a successful `general_conversation` response with matching spoken/subtitle
text and both action booleans false. Do not include private transcripts or run this test without explicit approval.

## Mock mode and fallback

### Mock mode

Set `THINKING_SPACE_MOCK_MODE=1` to run the whole backend without OpenAI or ElevenLabs. Only
`THINKING_SPACE_DEV_TOKEN` is still required. Mock mode is opt-in: a missing API key never switches the
service to mock data on its own, so a misconfigured deployment still shows up as a 503 on `/health`.

| Endpoint | Mock behavior |
| --- | --- |
| `/health` | `{ "status": "ok", "mode": "mock" }`; `OPENAI_API_KEY` is not required |
| `/api/transcriptions` | Validates the WAV, then cycles through fixed English transcripts (`model: "mock"`) |
| `/api/conversations/process` | Keyword rules: "combine"/"merge" with two ideas → `combine_ideas`; "expand" → `expand_idea`; "risk"/"challenge" → `conversational_challenge`; questions → `converse`; anything else → `create_idea` |
| `/api/ideas/process` | Builds every action locally; `source: "mock"` |
| `/api/voice/synthesize` | HTTP 503 "not configured", so Unity shows subtitles only |

Mock responses still go through the same orchestrator validation as live responses.
Code: `mock_providers.py`.

### Fallback in live mode

- **Idea creation:** if the idea provider times out, returns malformed output, or fails, `create_idea` still
  succeeds. The backend saves the transcript as the summary, picks up to five keywords as the title, and returns
  `source: "fallback"`. Expansion, challenge, and combination need real reasoning, so they keep returning errors.
- **Spoken line on errors:** conversation and idea error bodies include `fallback_spoken_response` for HTTP 429,
  500, 502, 503, and 504: a short line Orby can say instead of staying silent. The HTTP status, `status: "error"`,
  and diagnostic headers are unchanged, so failures stay visible. Validation errors (4xx other than 429) leave it
  `null`.
- `source` is `"model"` for normal idea responses. Unity's `JsonUtility` ignores both new fields until the client
  reads them.

Tests: `tests/test_mock_and_fallback.py`.
