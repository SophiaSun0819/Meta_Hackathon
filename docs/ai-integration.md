# AI integration spec: LLM and TTS

Technical spec for the AI layer of Thinking Space. It is written for whoever implements this layer, human or coding agent. Read it before adding AI code, and update it when a decision below changes.

Status: draft, 2026-11-02. Items marked **DECIDE** are open; see [Open decisions](#open-decisions).

**Decided:** OpenAI for both LLM and TTS (one account, one key). Backend on Cloudflare Workers, in `worker/`. With no key set, the backend runs in **mock mode** (`source: "mock"`), so the Unity side can be built first.

**Demo language: English (`en-US`).** Prompts, keywords, detail, speech and the TTS voice target English. Other languages are out of scope for the competition build.

## 1. Scope

The app uses three models:

| Model | Job | Owner |
| --- | --- | --- |
| ASR (speech to text) | Turn the user's recording into a transcript | Teammate (ASR) |
| LLM | Turn transcripts and bubbles into structured bubble operations | This spec |
| TTS (text to speech) | Speak the AI partner's short reply | This spec |

This spec covers the LLM and TTS layers, the backend proxy they share, and the contract with the ASR layer. It does not cover bubble rendering or hand interaction, except where they consume AI output.

## 2. Architecture

```
Quest app (Unity)                                   Backend proxy                Providers
┌──────────────┐  transcript  ┌──────────────┐  HTTPS  ┌─────────────┐  HTTPS  ┌─────────┐
│ ASR layer    │ ───────────▶ │ AIService    │ ──────▶ │ POST /v1/ai │ ──────▶ │ LLM     │
└──────────────┘              │ (C#)         │ ◀────── │             │ ◀────── └─────────┘
                              │              │  JSON   └─────────────┘
                              │              │ ──────▶ ┌─────────────┐ ──────▶ ┌─────────┐
                              │ TTSService   │ ◀────── │ POST /v1/tts│ ◀────── │ TTS     │
                              └──────┬───────┘  audio  └─────────────┘         └─────────┘
                                     │ AIResult
                                     ▼
                              BubbleManager (applies operations), AudioSource (plays speech)
```

Rules:

1. **API keys never ship in the APK.** The app talks only to our backend. The backend holds provider keys in environment variables.
2. **One response shape for every LLM action** (section 5), so the client has one code path.
3. **The LLM never invents IDs for existing bubbles.** The client owns bubble IDs; the LLM only refers to IDs it was given, and uses `tempId` for new bubbles.
4. **Every AI call has a fallback** (section 8). A demo must never hang on the network.
5. **TTS is optional per call.** Bubbles render as soon as the LLM result arrives; speech plays when its audio arrives.

## 3. Repo layout

```
docs/ai-integration.md          this file
worker/                         backend proxy (outside Assets/, Unity ignores it)
  src/index.ts                  routes (/v1/ai, /v1/tts, /v1/health), auth, TTS cache
  src/schema.ts                 zod schemas + JSON Schema sent to OpenAI
  src/prompts.ts                system prompt per action, temperatures
  src/sanitize.ts               drops invented IDs, clamps lengths, caps counts
  src/fallback.ts               mock and fallback responses
  src/providers/llm.ts          OpenAI chat completions, structured output, one retry
  src/providers/tts.ts          OpenAI speech (WAV) + mock chime
  test/backend.test.ts          unit tests (npm test)
  .dev.vars.example             env var names, no values (real file is gitignored)
Assets/Scripts/AI/
  AIConfig.cs                   ScriptableObject: base URL, timeouts, flags
  AIModels.cs                   request/response DTOs (Newtonsoft Json)
  IAIService.cs                 interface
  AIService.cs                  calls /v1/ai, validates, falls back
  FallbackAIService.cs          offline canned responses
  TTSService.cs                 calls /v1/tts, caches, plays
  AIDebugPanel.cs               optional in-headset log of last request/response
Assets/StreamingAssets/AI/
  fallback/*.json               offline LLM responses for demo topics
  fallback-audio/*.wav          optional pre-recorded speech for those responses
Assets/Tests/EditMode/AI/       parsing and fallback tests
```

`worker/.dev.vars`, `worker/.env` and any file holding a key must be in `.gitignore`.

## 4. Contract with the ASR layer

The ASR layer calls `IAIService` with a finished transcript. It does not call the backend directly.

```csharp
public sealed class Transcript
{
    public string Text;          // final recognized text, trimmed
    public string Language;      // BCP-47; "en-US" for the demo
    public float  DurationSec;   // length of the recording
    public float  Confidence;    // 0..1 if the ASR provides it, else -1
}
```

Rules:

- Send only final results, not partials.
- Empty or whitespace-only text: do not call the LLM; show a "didn't catch that" hint.
- `Confidence` below a threshold set in `AIConfig` (default 0.4): still send, but the UI marks the bubble as "check this".

## 5. LLM API

### 5.1 Endpoint

`POST /v1/ai` · `Content-Type: application/json` · header `X-App-Key: <shared secret>` (simple abuse guard, not real auth).

### 5.2 Request

```json
{
  "action": "summarize",
  "sessionId": "b3c1…",
  "language": "en-US",
  "transcript": "I want to plan a weekend game for dogs at the park",
  "targetBubbleIds": [],
  "bubbles": [
    { "id": "bub_01", "keywords": ["Camping"], "detail": ["One night, two days"], "color": "green", "groupId": "grp_outdoor" }
  ],
  "wantSpeech": true
}
```

| Field | Type | Notes |
| --- | --- | --- |
| `action` | enum | `summarize`, `connect`, `challenge`, `expand`, `next_steps`, `suggest_topics` |
| `sessionId` | string | client-generated UUID, used for logs only |
| `language` | string | language for keywords, detail and speech |
| `transcript` | string | required for `summarize`, else empty |
| `targetBubbleIds` | string[] | required for `challenge` and `expand` (one ID), optional for `next_steps` |
| `bubbles` | Bubble[] | current session state; send at most 40, newest first |
| `wantSpeech` | bool | when false the backend may return `speech: ""` |

### 5.3 Actions

| Action | Input | Expected output |
| --- | --- | --- |
| `summarize` | transcript + bubbles | 1 new bubble (rarely 2 if the user clearly said two ideas); optional link to an existing bubble |
| `connect` | bubbles | groups with titles, links between related bubbles, color updates per group |
| `challenge` | one target bubble + bubbles | 1 new bubble of kind `challenge` linked to the target: a counterpoint or blind spot |
| `expand` | one target bubble + bubbles | 2–3 new bubbles of kind `expansion` linked to the target |
| `next_steps` | bubbles (+ optional targets) | 1 new bubble of kind `steps`, detail = ordered steps |
| `suggest_topics` | nothing | 3–4 new bubbles of kind `topic` the user can start from |

`challenge` and `expand` are the "agentic" moments for the Best Agentic Interaction award. Their prompts should make the AI take a position, not just restate.

### 5.4 Response (all actions)

```json
{
  "action": "summarize",
  "source": "llm",
  "bubblesToCreate": [
    {
      "tempId": "new_1",
      "kind": "idea",
      "keywords": ["Game", "Dogs", "Park"],
      "detail": ["A game to play with dogs at the park", "Good for a family weekend"],
      "color": "orange",
      "relatedTo": null
    }
  ],
  "bubblesToUpdate": [ { "id": "bub_01", "color": "green", "groupId": "grp_outdoor" } ],
  "links": [ { "from": "new_1", "to": "bub_01", "label": "both outdoors" } ],
  "groups": [ { "groupId": "grp_outdoor", "title": "Outdoor activities", "bubbleIds": ["bub_01", "new_1"] } ],
  "speech": "Got it: game, dogs, park. I grouped it with camping.",
  "warnings": []
}
```

| Field | Rules |
| --- | --- |
| `source` | `llm`, `mock` (no key set), `fallback_backend` or `fallback_client` |
| `kind` | `idea`, `challenge`, `expansion`, `steps`, `topic` |
| `keywords` | 1–3 items, each ≤ 3 words |
| `detail` | 0–4 bullets, each ≤ 15 words |
| `color` | `red`, `orange`, `yellow`, `green`, `blue`, `purple` |
| `relatedTo` | existing bubble ID or `null` |
| `links[].from/to` | existing IDs or `tempId`s from this response only |
| `groups` | full membership for groups this response touches |
| `speech` | ≤ 2 sentences, ≤ 30 words; plain text, no markdown or emoji |
| `warnings` | backend notes, e.g. an ID the model invented that was dropped |

Arrays that don't apply are `[]`, never missing.

### 5.5 Backend validation

1. Ask the provider for structured output (JSON schema, tool use or JSON mode, whichever the chosen provider supports) with the schema in `worker/src/schema.ts`.
2. Validate the result against the same schema (e.g. zod).
3. Drop references to IDs that were not in the request or in `tempId`s; add a `warnings` entry.
4. Clamp lengths (truncate keywords and detail, cut `speech` at the last sentence end).
5. If parsing or validation fails: retry once, sending the validation error back to the model. If it fails again, return a backend fallback (section 8) with `source: "fallback_backend"`. Always HTTP 200 with a valid body for model errors; use 4xx only for bad client requests.

### 5.6 Prompting

- One system prompt per action in `worker/src/prompts.ts`, plus a shared preamble: role (a thinking partner in a spatial brainstorming app), output rules from 5.4, and the language rule ("write keywords, detail and speech in `{language}`").
- Send bubbles as compact JSON, not prose.
- Temperature low for `summarize` and `connect` (consistency), higher for `expand` and `challenge` (variety). Exact values: tune during testing.
- Keep a small set of golden test inputs per action in `worker/test/fixtures/` and check outputs by eye after each prompt change.

## 6. TTS API

### 6.1 Endpoint

`POST /v1/tts` · JSON in, audio bytes out.

```json
{ "text": "Got it: game, dogs, park.", "language": "en-US", "voice": "default", "format": "wav" }
```

Response: `200` with `Content-Type: audio/wav` (or `audio/ogg`) and the audio as the body. Errors: `4xx/5xx` with a JSON body `{ "error": "…" }`; the client then skips speech silently.

### 6.2 Rules

- **Format: prefer 16-bit PCM WAV, mono, 22.05 or 24 kHz.** Unity decodes WAV reliably on Android. If size matters, test OGG Vorbis on the Quest; test MP3 on the device before relying on it.
- Load on the client with `UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV)` (POST via a custom `UnityWebRequest` with `DownloadHandlerAudioClip`).
- **Cache** by `hash(text + voice + language)` on the backend (and optionally in memory on the client). Demo lines repeat; caching removes most TTS latency during a demo.
- Cap text at 200 characters; the backend rejects longer input with 400.
- One voice for the AI partner across the app. **DECIDE** the provider and voice; check English quality and naturalness by ear.

### 6.3 Playback

- Play through one `AudioSource` attached to the AI partner object (spatial blend ~0.6 so the voice comes from the partner, not from inside the head).
- New speech interrupts old speech. Never queue more than one clip.
- User setting "AI voice on/off" in the session menu; when off, send `wantSpeech: false` and never call TTS.

## 7. Client (C#)

```csharp
public interface IAIService
{
    Task<AIResult> RunAsync(AIRequest request, CancellationToken ct);
}

public interface ITTSService
{
    Task<AudioClip> SynthesizeAsync(string text, string language, CancellationToken ct); // null on failure
}
```

- `AIService` wraps the HTTP call; on timeout, network error or invalid JSON it returns `FallbackAIService.RunAsync(request)` with `source = "fallback_client"`. It never throws to callers except on cancellation.
- Parse with Newtonsoft Json (`com.unity.nuget.newtonsoft-json` is installed). Use `[JsonProperty]` names that match section 5 exactly. Treat unknown enum values as `idea` / no color rather than failing.
- `AIConfig` (ScriptableObject in `Assets/Settings/AI/`): `baseUrl`, `appKey`, `llmTimeoutSec` (default 8), `ttsTimeoutSec` (default 6), `forceFallback` (demo switch), `minAsrConfidence`.
- Only one in-flight LLM request per session. A new user request cancels the previous one (`CancellationTokenSource`).
- Flow for one `summarize`:
  1. Show the waiting animation on the recording bubble immediately.
  2. `await RunAsync(...)`.
  3. Apply `bubblesToCreate`, `bubblesToUpdate`, `links`, `groups` through `BubbleManager` and map each `tempId` to a real ID.
  4. If `speech` is not empty and voice is on, start `SynthesizeAsync` without awaiting it in the render path; play when ready.
- `AIDebugPanel` (dev builds only): last request, last response, latency, source. Very useful on the headset where there is no console.

Unity notes:

- `async/await` with `UnityWebRequest`: await the operation on the main thread; don't touch Unity objects from background threads.
- Android: `INTERNET` permission is already set. HTTPS only (cleartext HTTP is blocked by default on Android).

## 8. Fallbacks

| Layer | Trigger | Behavior |
| --- | --- | --- |
| Backend | Model error, timeout, invalid JSON after one retry | Return canned response for the action; `source: "fallback_backend"` |
| Client | No network, HTTP error, client timeout, `forceFallback` on | `FallbackAIService` builds a response locally; `source: "fallback_client"` |
| TTS | Any failure | No speech; if a pre-recorded clip exists for the canned line, play that |

`FallbackAIService` rules:

- `summarize`: keywords = up to 3 longest content words from the transcript (simple split, skipping stop words), detail = the transcript, color by rotation.
- Other actions: pick from `StreamingAssets/AI/fallback/<topic>/<action>.json`, keyed by the demo topic, then rewrite IDs to match current bubbles.
- Fallback output passes through the same apply path as real output, so it is tested every time.

Demo topics with full fallback sets: **DECIDE** (suggest 2, e.g. a weekend team activity, and a product feature brainstorm).

## 9. Latency budget

Target from "user releases pinch" to "bubble appears": **≤ 3 s typical, 8 s hard timeout**.

| Step | Target |
| --- | --- |
| ASR final result | ≤ 1.0 s |
| Network round trip to backend | ≤ 0.3 s |
| LLM | ≤ 1.5 s |
| Apply to scene | ≤ 0.1 s |
| TTS (runs after bubble appears) | ≤ 1.5 s, cached 0 s |

How to get there: pick a fast model tier for `summarize`; keep prompts and the bubble list short; cap output tokens; host the backend in a region near the demo location; cache TTS. Log per-step timings in `AIDebugPanel` from day one.

## 10. Backend

- Runtime: Cloudflare Workers. Setup, local run and deploy: `worker/README.md`.
- Env vars: `OPENAI_API_KEY`, `LLM_MODEL`, `TTS_MODEL`, `TTS_VOICE`, `TTS_INSTRUCTIONS`, `APP_KEY`, timeouts. No `OPENAI_API_KEY` = mock mode.
- Must stay online through judging (until about 2026-12-11).
- Routes: `POST /v1/ai`, `POST /v1/tts`, `GET /v1/health` (returns provider reachability; the app pings it at session start and shows an "offline mode" badge if it fails).
- Logs: action, latency, source, token counts. Never log full transcripts in production logs beyond the hackathon.
- Rate limit by `X-App-Key` and IP (generous; this is only abuse protection).

## 11. Testing

- **Backend:** schema tests for each action using fixtures; one live smoke test per provider behind a flag.
- **Unity EditMode:** parse every fixture response into DTOs; run `FallbackAIService` for each action; check that apply never fails on unknown IDs.
- **On device:** a debug menu button per action that runs it against a fixed bubble set, so latency and audio can be checked in the headset.

## 12. Build order

1. Backend `/v1/health` and `/v1/ai` with `summarize` only, returning a hardcoded valid response. Unity `AIService` calls it and logs the result.
2. Real LLM call for `summarize` with schema validation and retry.
3. `FallbackAIService` + `forceFallback` switch.
4. `/v1/tts` + `TTSService` + playback on the AI partner.
5. `connect`, then `challenge`, then `expand`. Then `next_steps`, `suggest_topics` if time allows.
6. Caching, debug panel timings, latency tuning.

Checkpoint (team plan): steps 1–4 running on the Quest by **2026-11-05**.

## Open decisions

| # | Decision | Notes |
| --- | --- | --- |
| 1 | LLM model | OpenAI; pick a small, fast model that supports structured outputs |
| 2 | TTS model and voice | OpenAI; check English voices by ear |
| 3 | Demo topics with full fallback sets | Suggest two |
| 4 | Whether ASR also goes through our backend | If yes, add `POST /v1/asr` here and agree on the audio format with the ASR owner |
