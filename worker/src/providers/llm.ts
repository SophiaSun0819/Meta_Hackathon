// LLM providers. The router only sees `LLMProvider`, so switching vendors means
// adding a class here, nothing else.
import { buildMessages, TEMPERATURE } from "../prompts";
import { MODEL_OUTPUT_JSON_SCHEMA, ModelOutputSchema, type AIRequest, type ModelOutput } from "../schema";

export interface LLMProvider {
  readonly name: string;
  generate(req: AIRequest): Promise<ModelOutput>;
}

export class LLMError extends Error {}

type ChatMessage = { role: "system" | "user" | "assistant"; content: string };

export interface OpenAILLMOptions {
  apiKey: string;
  model: string;
  timeoutMs: number;
  maxOutputTokens?: number;
  sendTemperature?: boolean; // some model families reject `temperature`
  baseUrl?: string;
  fetchImpl?: typeof fetch; // for tests
}

export class OpenAILLM implements LLMProvider {
  readonly name = "openai";
  constructor(private readonly opts: OpenAILLMOptions) {}

  async generate(req: AIRequest): Promise<ModelOutput> {
    const { system, user } = buildMessages(req);
    const messages: ChatMessage[] = [
      { role: "system", content: system },
      { role: "user", content: user },
    ];

    // One retry: on invalid output, show the model its own reply and the error.
    let lastError = "";
    for (let attempt = 0; attempt < 2; attempt++) {
      const raw = await this.call(messages, req);
      const parsed = parse(raw);
      if (parsed.ok) return parsed.value;
      lastError = parsed.error;
      messages.push(
        { role: "assistant", content: raw },
        { role: "user", content: `That reply was invalid: ${parsed.error}. Reply again with valid JSON only.` },
      );
    }
    throw new LLMError(`invalid model output after retry: ${lastError}`);
  }

  private async call(messages: ChatMessage[], req: AIRequest): Promise<string> {
    const f = this.opts.fetchImpl ?? fetch;
    const body: Record<string, unknown> = {
      model: this.opts.model,
      messages,
      max_completion_tokens: this.opts.maxOutputTokens ?? 900,
      response_format: {
        type: "json_schema",
        json_schema: { name: "bubble_operations", strict: true, schema: MODEL_OUTPUT_JSON_SCHEMA },
      },
    };
    if (this.opts.sendTemperature !== false) body.temperature = TEMPERATURE[req.action];

    let res: Response;
    try {
      res = await f(`${this.opts.baseUrl ?? "https://api.openai.com"}/v1/chat/completions`, {
        method: "POST",
        headers: { Authorization: `Bearer ${this.opts.apiKey}`, "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(this.opts.timeoutMs),
      });
    } catch (e) {
      throw new LLMError(`request failed: ${(e as Error).message}`);
    }
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new LLMError(`HTTP ${res.status}: ${text.slice(0, 300)}`);
    }
    const data = (await res.json()) as {
      choices?: { message?: { content?: string | null; refusal?: string | null } }[];
    };
    const msg = data.choices?.[0]?.message;
    if (msg?.refusal) throw new LLMError(`model refused: ${msg.refusal}`);
    if (!msg?.content) throw new LLMError("empty model response");
    return msg.content;
  }
}

function parse(raw: string): { ok: true; value: ModelOutput } | { ok: false; error: string } {
  let json: unknown;
  try {
    json = JSON.parse(raw);
  } catch {
    return { ok: false, error: "not valid JSON" };
  }
  const result = ModelOutputSchema.safeParse(json);
  if (result.success) return { ok: true, value: result.data };
  return { ok: false, error: result.error.issues.map((i) => `${i.path.join(".")}: ${i.message}`).join("; ") };
}
