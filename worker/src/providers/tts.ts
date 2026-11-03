// TTS providers. Output is always 16-bit PCM WAV so Unity can decode it on Quest.
import type { TTSRequest } from "../schema";

export interface TTSProvider {
  readonly name: string;
  synthesize(req: TTSRequest): Promise<ArrayBuffer>;
}

export class TTSError extends Error {}

export interface OpenAITTSOptions {
  apiKey: string;
  model: string;
  voice: string; // used when the request asks for "default"
  timeoutMs: number;
  instructions?: string; // speaking style, for models that support it
  baseUrl?: string;
  fetchImpl?: typeof fetch;
}

export class OpenAITTS implements TTSProvider {
  readonly name = "openai";
  constructor(private readonly opts: OpenAITTSOptions) {}

  async synthesize(req: TTSRequest): Promise<ArrayBuffer> {
    const f = this.opts.fetchImpl ?? fetch;
    const body: Record<string, unknown> = {
      model: this.opts.model,
      voice: req.voice === "default" ? this.opts.voice : req.voice,
      input: req.text,
      response_format: "wav",
    };
    if (this.opts.instructions) body.instructions = this.opts.instructions;

    let res: Response;
    try {
      res = await f(`${this.opts.baseUrl ?? "https://api.openai.com"}/v1/audio/speech`, {
        method: "POST",
        headers: { Authorization: `Bearer ${this.opts.apiKey}`, "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(this.opts.timeoutMs),
      });
    } catch (e) {
      throw new TTSError(`request failed: ${(e as Error).message}`);
    }
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      throw new TTSError(`HTTP ${res.status}: ${text.slice(0, 300)}`);
    }
    return res.arrayBuffer();
  }
}

// Mock mode: a short two-note chime whose length grows with the text, so the
// Unity side can test download, decode and playback without a key.
export class MockTTS implements TTSProvider {
  readonly name = "mock";

  async synthesize(req: TTSRequest): Promise<ArrayBuffer> {
    const seconds = Math.min(2, 0.3 + req.text.length * 0.01);
    return makeChimeWav(seconds);
  }
}

export function makeChimeWav(seconds: number, sampleRate = 24000): ArrayBuffer {
  const n = Math.round(seconds * sampleRate);
  const buf = new ArrayBuffer(44 + n * 2);
  const v = new DataView(buf);
  const ascii = (o: number, s: string) => [...s].forEach((c, i) => v.setUint8(o + i, c.charCodeAt(0)));
  ascii(0, "RIFF");
  v.setUint32(4, 36 + n * 2, true);
  ascii(8, "WAVE");
  ascii(12, "fmt ");
  v.setUint32(16, 16, true); // PCM chunk size
  v.setUint16(20, 1, true); // PCM
  v.setUint16(22, 1, true); // mono
  v.setUint32(24, sampleRate, true);
  v.setUint32(28, sampleRate * 2, true); // byte rate
  v.setUint16(32, 2, true); // block align
  v.setUint16(34, 16, true); // bits per sample
  ascii(36, "data");
  v.setUint32(40, n * 2, true);
  for (let i = 0; i < n; i++) {
    const t = i / sampleRate;
    const freq = t < seconds / 2 ? 660 : 880;
    const fade = Math.min(1, t / 0.02, (seconds - t) / 0.05);
    v.setInt16(44 + i * 2, Math.round(Math.sin(2 * Math.PI * freq * t) * 0.3 * fade * 32767), true);
  }
  return buf;
}
