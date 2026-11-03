import { describe, expect, it } from "vitest";
import { buildFallback, extractKeywords } from "../src/fallback";
import { route, providersFromEnv, type Env, type Providers } from "../src/index";
import { OpenAILLM } from "../src/providers/llm";
import { MockTTS } from "../src/providers/tts";
import { sanitize } from "../src/sanitize";
import { ACTIONS, AIRequestSchema, ModelOutputSchema, type AIResponse } from "../src/schema";

const bubbles = [
  { id: "bub_1", keywords: ["Camping"], detail: ["One night"], color: "green" as const, groupId: null },
  { id: "bub_2", keywords: ["Board Games"], detail: [], color: "blue" as const, groupId: null },
  { id: "bub_3", keywords: ["Cooking Class"], detail: [], color: null, groupId: null },
];

function request(action: string, extra: Record<string, unknown> = {}) {
  return AIRequestSchema.parse({
    action,
    transcript: action === "summarize" ? "I want to plan a weekend game for dogs at the park" : "",
    targetBubbleIds: action === "challenge" || action === "expand" ? ["bub_1"] : [],
    bubbles,
    ...extra,
  });
}

const mockProviders: Providers = { llm: null, tts: new MockTTS() };

async function post(path: string, body: unknown, env: Env = {}, providers = mockProviders) {
  const res = await route(
    new Request(`https://x${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...(env.APP_KEY ? { "X-App-Key": env.APP_KEY } : {}) },
      body: JSON.stringify(body),
    }),
    env,
    providers,
  );
  return res;
}

// Fake OpenAI chat completions endpoint returning the given contents in order.
function fakeOpenAI(contents: string[]) {
  let calls = 0;
  const fetchImpl = (async () => {
    const content = contents[Math.min(calls, contents.length - 1)];
    calls++;
    return new Response(JSON.stringify({ choices: [{ message: { content } }] }), { status: 200 });
  }) as unknown as typeof fetch;
  return { fetchImpl, calls: () => calls };
}

describe("mock mode", () => {
  it("uses mock providers when no key is set", () => {
    const p = providersFromEnv({});
    expect(p.llm).toBeNull();
    expect(p.tts.name).toBe("mock");
  });

  it.each(ACTIONS)("returns a valid response for %s", async (action) => {
    const res = await post("/v1/ai", request(action));
    expect(res.status).toBe(200);
    const body = (await res.json()) as AIResponse;
    expect(body.source).toBe("mock");
    expect(body.action).toBe(action);
    expect(ModelOutputSchema.safeParse(body).success).toBe(true);
  });

  it("summarize extracts keywords from the transcript", async () => {
    const body = (await (await post("/v1/ai", request("summarize"))).json()) as AIResponse;
    expect(body.bubblesToCreate).toHaveLength(1);
    expect(body.bubblesToCreate[0].keywords.length).toBeGreaterThan(0);
    expect(body.speech).toMatch(/^Got it/);
  });

  it("fallback output always matches the model schema", () => {
    for (const a of ACTIONS) expect(ModelOutputSchema.safeParse(buildFallback(request(a))).success).toBe(true);
  });

  it("extractKeywords skips stop words and keeps spoken order", () => {
    expect(extractKeywords("I want to plan a weekend game for dogs at the park")).toEqual(["Weekend", "Game", "Dogs"]);
    expect(extractKeywords("um")).toEqual(["Idea"]);
  });
});

describe("request validation", () => {
  it("rejects summarize without a transcript", async () => {
    expect((await post("/v1/ai", { action: "summarize", transcript: " " })).status).toBe(400);
  });
  it("rejects challenge without exactly one target", async () => {
    expect((await post("/v1/ai", { action: "challenge", bubbles })).status).toBe(400);
  });
  it("rejects a target that is not in bubbles", async () => {
    expect((await post("/v1/ai", { action: "expand", targetBubbleIds: ["nope"], bubbles })).status).toBe(400);
  });
  it("checks X-App-Key when APP_KEY is set", async () => {
    const res = await route(
      new Request("https://x/v1/ai", { method: "POST", body: "{}" }),
      { APP_KEY: "secret" },
      mockProviders,
    );
    expect(res.status).toBe(401);
  });
});

describe("sanitize", () => {
  it("drops invented IDs and remaps temp IDs", () => {
    const req = request("summarize");
    const { output, warnings } = sanitize(req, {
      bubblesToCreate: [
        { tempId: "x", kind: "idea", keywords: ["Dog Park Game"], detail: [], color: "orange", relatedTo: "ghost" },
      ],
      bubblesToUpdate: [{ id: "ghost", color: "red", groupId: null }],
      links: [
        { from: "x", to: "bub_1", label: "both outdoors" },
        { from: "x", to: "ghost", label: null },
      ],
      groups: [{ groupId: "g", title: "Outdoor", bubbleIds: ["bub_1", "x", "ghost"] }],
      speech: "Got it.",
    });
    expect(output.bubblesToCreate[0].tempId).toBe("new_1");
    expect(output.bubblesToCreate[0].relatedTo).toBeNull();
    expect(output.bubblesToUpdate).toHaveLength(0);
    expect(output.links).toEqual([{ from: "new_1", to: "bub_1", label: "both outdoors" }]);
    expect(output.groups[0].bubbleIds).toEqual(["bub_1", "new_1"]);
    expect(warnings.length).toBeGreaterThanOrEqual(3);
  });

  it("caps new bubbles per action and clamps speech", () => {
    const req = request("challenge");
    const many = Array.from({ length: 4 }, (_, i) => ({
      tempId: `t${i}`, kind: "challenge" as const, keywords: ["A"], detail: [], color: "red" as const, relatedTo: "bub_1",
    }));
    const long = Array.from({ length: 50 }, () => "word").join(" ");
    const { output } = sanitize(req, { bubblesToCreate: many, bubblesToUpdate: [], links: [], groups: [], speech: long });
    expect(output.bubblesToCreate).toHaveLength(1);
    expect(output.speech.split(" ").length).toBeLessThanOrEqual(30);
  });

  it("empties speech when wantSpeech is false", () => {
    const req = request("summarize", { wantSpeech: false });
    expect(sanitize(req, buildFallback(req)).output.speech).toBe("");
  });
});

describe("OpenAI LLM provider", () => {
  const valid = JSON.stringify({
    bubblesToCreate: [{ tempId: "new_1", kind: "idea", keywords: ["Dog Park Game"], detail: ["Weekend"], color: "orange", relatedTo: null }],
    bubblesToUpdate: [],
    links: [],
    groups: [],
    speech: "Got it: a dog park game.",
  });

  it("returns source llm on valid output", async () => {
    const fake = fakeOpenAI([valid]);
    const llm = new OpenAILLM({ apiKey: "k", model: "m", timeoutMs: 1000, fetchImpl: fake.fetchImpl });
    const body = (await (await post("/v1/ai", request("summarize"), {}, { llm, tts: new MockTTS() })).json()) as AIResponse;
    expect(body.source).toBe("llm");
    expect(body.bubblesToCreate[0].keywords).toEqual(["Dog Park Game"]);
    expect(fake.calls()).toBe(1);
  });

  it("retries once after invalid output", async () => {
    const fake = fakeOpenAI(["not json", valid]);
    const llm = new OpenAILLM({ apiKey: "k", model: "m", timeoutMs: 1000, fetchImpl: fake.fetchImpl });
    const body = (await (await post("/v1/ai", request("summarize"), {}, { llm, tts: new MockTTS() })).json()) as AIResponse;
    expect(body.source).toBe("llm");
    expect(fake.calls()).toBe(2);
  });

  it("falls back after two invalid outputs", async () => {
    const fake = fakeOpenAI(["{}", "{}"]);
    const llm = new OpenAILLM({ apiKey: "k", model: "m", timeoutMs: 1000, fetchImpl: fake.fetchImpl });
    const body = (await (await post("/v1/ai", request("summarize"), {}, { llm, tts: new MockTTS() })).json()) as AIResponse;
    expect(body.source).toBe("fallback_backend");
    expect(body.warnings[0]).toMatch(/model failed/);
  });

  it("falls back on HTTP errors", async () => {
    const fetchImpl = (async () => new Response("rate limited", { status: 429 })) as unknown as typeof fetch;
    const llm = new OpenAILLM({ apiKey: "k", model: "m", timeoutMs: 1000, fetchImpl });
    const body = (await (await post("/v1/ai", request("connect"), {}, { llm, tts: new MockTTS() })).json()) as AIResponse;
    expect(body.source).toBe("fallback_backend");
  });
});

describe("TTS", () => {
  it("mock returns a playable 16-bit mono WAV", async () => {
    const res = await post("/v1/tts", { text: "Got it: game, dogs, park." });
    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toBe("audio/wav");
    const v = new DataView(await res.arrayBuffer());
    expect(String.fromCharCode(v.getUint8(0), v.getUint8(1), v.getUint8(2), v.getUint8(3))).toBe("RIFF");
    expect(v.getUint16(22, true)).toBe(1); // mono
    expect(v.getUint16(34, true)).toBe(16); // 16-bit
  });

  it("rejects empty and too-long text", async () => {
    expect((await post("/v1/tts", { text: "" })).status).toBe(400);
    expect((await post("/v1/tts", { text: "a".repeat(201) })).status).toBe(400);
  });

  it("returns 502 when the provider fails", async () => {
    const tts = { name: "broken", synthesize: async () => { throw new Error("down"); } };
    expect((await post("/v1/tts", { text: "hi" }, {}, { llm: null, tts })).status).toBe(502);
  });
});

describe("health", () => {
  it("reports mock mode", async () => {
    const res = await route(new Request("https://x/v1/health"), {}, mockProviders);
    expect(await res.json()).toEqual({ ok: true, llm: "mock", tts: "mock" });
  });
});
