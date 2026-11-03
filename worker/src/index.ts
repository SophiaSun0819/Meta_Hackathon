// Thinking Space AI backend (Cloudflare Worker).
// Routes: GET /v1/health, POST /v1/ai, POST /v1/tts. See docs/ai-integration.md.
// With no OPENAI_API_KEY set, everything runs in mock mode.
import { buildFallback } from "./fallback";
import { OpenAILLM, type LLMProvider } from "./providers/llm";
import { MockTTS, OpenAITTS, type TTSProvider } from "./providers/tts";
import { sanitize } from "./sanitize";
import { AIRequestSchema, TTSRequestSchema, type AIRequest, type AIResponse, type Source } from "./schema";

export interface Env {
  OPENAI_API_KEY?: string;
  LLM_MODEL?: string;
  LLM_TIMEOUT_MS?: string;
  LLM_SEND_TEMPERATURE?: string; // "false" for models that reject temperature
  TTS_MODEL?: string;
  TTS_VOICE?: string;
  TTS_INSTRUCTIONS?: string;
  TTS_TIMEOUT_MS?: string;
  APP_KEY?: string; // shared secret the app sends as X-App-Key; unset = no check (local dev only)
}

export interface Providers {
  llm: LLMProvider | null; // null = mock mode
  tts: TTSProvider;
}

export function providersFromEnv(env: Env): Providers {
  const key = env.OPENAI_API_KEY?.trim();
  if (!key) return { llm: null, tts: new MockTTS() };
  if (!env.LLM_MODEL || !env.TTS_MODEL) {
    throw new Error("OPENAI_API_KEY is set but LLM_MODEL or TTS_MODEL is missing");
  }
  return {
    llm: new OpenAILLM({
      apiKey: key,
      model: env.LLM_MODEL,
      timeoutMs: Number(env.LLM_TIMEOUT_MS ?? 7000),
      sendTemperature: env.LLM_SEND_TEMPERATURE !== "false",
    }),
    tts: new OpenAITTS({
      apiKey: key,
      model: env.TTS_MODEL,
      voice: env.TTS_VOICE ?? "alloy",
      instructions: env.TTS_INSTRUCTIONS,
      timeoutMs: Number(env.TTS_TIMEOUT_MS ?? 6000),
    }),
  };
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const log = (fields: Record<string, unknown>) => console.log(JSON.stringify(fields));

export async function handleAI(req: AIRequest, providers: Providers): Promise<AIResponse> {
  let output;
  let source: Source;
  const warnings: string[] = [];

  if (providers.llm === null) {
    output = buildFallback(req);
    source = "mock";
  } else {
    try {
      output = await providers.llm.generate(req);
      source = "llm";
    } catch (e) {
      warnings.push(`model failed, using fallback: ${(e as Error).message}`);
      output = buildFallback(req);
      source = "fallback_backend";
    }
  }

  const clean = sanitize(req, output);
  return { action: req.action, source, ...clean.output, warnings: [...warnings, ...clean.warnings] };
}

async function cacheKey(body: object): Promise<Request> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(JSON.stringify(body)));
  const hex = [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
  return new Request(`https://tts-cache.internal/${hex}`);
}

export async function route(request: Request, env: Env, providers: Providers): Promise<Response> {
  const url = new URL(request.url);
  const started = Date.now();

  if (url.pathname === "/v1/health" && request.method === "GET") {
    return json({ ok: true, llm: providers.llm?.name ?? "mock", tts: providers.tts.name });
  }

  if (env.APP_KEY && request.headers.get("X-App-Key") !== env.APP_KEY) {
    return json({ error: "unauthorized" }, 401);
  }

  if (url.pathname === "/v1/ai" && request.method === "POST") {
    const parsed = AIRequestSchema.safeParse(await request.json().catch(() => null));
    if (!parsed.success) {
      return json({ error: "bad request", issues: parsed.error.issues.map((i) => i.message) }, 400);
    }
    const result = await handleAI(parsed.data, providers);
    log({ route: "ai", action: result.action, source: result.source, ms: Date.now() - started, warnings: result.warnings.length });
    return json(result);
  }

  if (url.pathname === "/v1/tts" && request.method === "POST") {
    const parsed = TTSRequestSchema.safeParse(await request.json().catch(() => null));
    if (!parsed.success) {
      return json({ error: "bad request", issues: parsed.error.issues.map((i) => i.message) }, 400);
    }
    const tts = parsed.data;
    const cache = typeof caches !== "undefined" ? (caches as unknown as { default: Cache }).default : null;
    const key = await cacheKey({ ...tts, provider: providers.tts.name });

    const hit = cache ? await cache.match(key) : undefined;
    if (hit) {
      log({ route: "tts", cache: "hit", ms: Date.now() - started });
      return hit;
    }
    try {
      const audio = await providers.tts.synthesize(tts);
      const res = new Response(audio, {
        headers: { "Content-Type": "audio/wav", "Cache-Control": "public, max-age=604800" },
      });
      if (cache) await cache.put(key, res.clone());
      log({ route: "tts", cache: "miss", provider: providers.tts.name, ms: Date.now() - started });
      return res;
    } catch (e) {
      log({ route: "tts", error: (e as Error).message, ms: Date.now() - started });
      return json({ error: "tts failed" }, 502);
    }
  }

  return json({ error: "not found" }, 404);
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    let providers: Providers;
    try {
      providers = providersFromEnv(env);
    } catch (e) {
      return json({ error: (e as Error).message }, 500);
    }
    return route(request, env, providers);
  },
};
