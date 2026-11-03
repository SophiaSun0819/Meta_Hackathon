# Thinking Space backend

Cloudflare Worker that sits between the Quest app and OpenAI. It holds the API key, turns app requests into LLM and TTS calls, validates what comes back, and falls back to canned responses when anything fails. The full contract is in [`../docs/ai-integration.md`](../docs/ai-integration.md).

## Mock mode

With no `OPENAI_API_KEY`, the backend runs in **mock mode**: `/v1/ai` returns deterministic responses built from the request (`source: "mock"`), and `/v1/tts` returns a short chime WAV. Use it to build and test the Unity side before keys exist. Adding the key switches to real calls with no client changes.

## Run locally

Requires Node 20+.

```bash
cd worker
npm install
cp .dev.vars.example .dev.vars   # leave OPENAI_API_KEY empty for mock mode
npm run dev                      # http://localhost:8787
```

Try it:

```bash
curl localhost:8787/v1/health

curl -X POST localhost:8787/v1/ai \
  -H "Content-Type: application/json" -H "X-App-Key: dev-key" \
  -d '{"action":"summarize","transcript":"Let us host a cooking class for the team on Friday","bubbles":[]}'

curl -X POST localhost:8787/v1/tts \
  -H "Content-Type: application/json" -H "X-App-Key: dev-key" \
  -d '{"text":"Got it: cooking class, team, Friday."}' -o test.wav
```

The Quest can't reach `localhost` on your computer. To test from the headset, deploy (below) or run `npx wrangler dev --ip 0.0.0.0` and use your computer's LAN IP; note that Android blocks plain HTTP by default, so deploying is usually easier.

## Use real OpenAI calls

1. Create an API key in the OpenAI dashboard and set a monthly spending limit.
2. Put `OPENAI_API_KEY`, `LLM_MODEL` and `TTS_MODEL` in `.dev.vars`. Pick current model names from OpenAI's model list: a small, fast model for the LLM, and a TTS model that supports WAV output.
3. If the chosen LLM rejects `temperature`, set `LLM_SEND_TEMPERATURE=false`.

## Deploy

```bash
npx wrangler login
npx wrangler secret put OPENAI_API_KEY
npx wrangler secret put APP_KEY          # any long random string; the app sends it as X-App-Key
npx wrangler secret put LLM_MODEL        # or add it under [vars] in wrangler.toml
npx wrangler secret put TTS_MODEL
npm run deploy
```

The backend must stay online through judging (until about 2026-12-11).

## Test

```bash
npm test          # unit tests, no network
npm run typecheck
```

## Layout

| File | Purpose |
| --- | --- |
| `src/index.ts` | Routes, auth, TTS cache, provider selection |
| `src/schema.ts` | Request/response schemas (zod) and the JSON Schema sent to OpenAI |
| `src/prompts.ts` | System prompt per action, temperatures |
| `src/sanitize.ts` | Drops invented IDs, clamps lengths, caps bubble counts |
| `src/fallback.ts` | Mock and fallback responses |
| `src/providers/llm.ts` | OpenAI chat completions with structured output and one retry |
| `src/providers/tts.ts` | OpenAI speech (WAV) and the mock chime |
| `test/backend.test.ts` | Tests for all of the above |
