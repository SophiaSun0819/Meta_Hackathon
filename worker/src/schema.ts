// Request/response contract for /v1/ai and /v1/tts.
// Keep in sync with docs/ai-integration.md, sections 5 and 6.
import { z } from "zod";

export const ACTIONS = [
  "summarize",
  "connect",
  "challenge",
  "expand",
  "next_steps",
  "suggest_topics",
] as const;
export const KINDS = ["idea", "challenge", "expansion", "steps", "topic"] as const;
export const COLORS = ["red", "orange", "yellow", "green", "blue", "purple"] as const;

export type Action = (typeof ACTIONS)[number];
export type Color = (typeof COLORS)[number];

export const MAX_BUBBLES_IN_REQUEST = 40;

// ---------- Request ----------

export const BubbleSchema = z.object({
  id: z.string().min(1).max(64),
  keywords: z.array(z.string()).max(5).default([]),
  detail: z.array(z.string()).max(8).default([]),
  color: z.enum(COLORS).nullable().optional(),
  groupId: z.string().max(64).nullable().optional(),
});
export type Bubble = z.infer<typeof BubbleSchema>;

export const AIRequestSchema = z
  .object({
    action: z.enum(ACTIONS),
    sessionId: z.string().max(64).default(""),
    language: z.string().max(16).default("en-US"),
    transcript: z.string().max(4000).default(""),
    targetBubbleIds: z.array(z.string()).max(10).default([]),
    bubbles: z.array(BubbleSchema).max(MAX_BUBBLES_IN_REQUEST).default([]),
    wantSpeech: z.boolean().default(true),
  })
  .superRefine((r, ctx) => {
    if (r.action === "summarize" && r.transcript.trim() === "") {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: "summarize needs a transcript" });
    }
    if ((r.action === "challenge" || r.action === "expand") && r.targetBubbleIds.length !== 1) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: `${r.action} needs exactly one targetBubbleId` });
    }
    const ids = new Set(r.bubbles.map((b) => b.id));
    for (const t of r.targetBubbleIds) {
      if (!ids.has(t)) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, message: `targetBubbleId ${t} is not in bubbles` });
      }
    }
  });
export type AIRequest = z.infer<typeof AIRequestSchema>;

// ---------- Response ----------

export const NewBubbleSchema = z.object({
  tempId: z.string(),
  kind: z.enum(KINDS),
  keywords: z.array(z.string()),
  detail: z.array(z.string()),
  color: z.enum(COLORS),
  relatedTo: z.string().nullable(),
});

export const BubbleUpdateSchema = z.object({
  id: z.string(),
  color: z.enum(COLORS).nullable(),
  groupId: z.string().nullable(),
});

export const LinkSchema = z.object({
  from: z.string(),
  to: z.string(),
  label: z.string().nullable(),
});

export const GroupSchema = z.object({
  groupId: z.string(),
  title: z.string(),
  bubbleIds: z.array(z.string()),
});

// What the model must return (no `action`, `source`, `warnings`: the backend adds those).
export const ModelOutputSchema = z.object({
  bubblesToCreate: z.array(NewBubbleSchema),
  bubblesToUpdate: z.array(BubbleUpdateSchema),
  links: z.array(LinkSchema),
  groups: z.array(GroupSchema),
  speech: z.string(),
});
export type ModelOutput = z.infer<typeof ModelOutputSchema>;

export type Source = "llm" | "fallback_backend" | "mock";

export type AIResponse = ModelOutput & {
  action: Action;
  source: Source;
  warnings: string[];
};

// JSON Schema for OpenAI Structured Outputs (strict mode: every property required,
// no additional properties, nullable via a type union). Mirrors ModelOutputSchema.
const str = { type: "string" } as const;
const nullableStr = { type: ["string", "null"] } as const;
const strArray = { type: "array", items: str } as const;
const colorEnum = { type: "string", enum: [...COLORS] } as const;

export const MODEL_OUTPUT_JSON_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["bubblesToCreate", "bubblesToUpdate", "links", "groups", "speech"],
  properties: {
    bubblesToCreate: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["tempId", "kind", "keywords", "detail", "color", "relatedTo"],
        properties: {
          tempId: str,
          kind: { type: "string", enum: [...KINDS] },
          keywords: strArray,
          detail: strArray,
          color: colorEnum,
          relatedTo: nullableStr,
        },
      },
    },
    bubblesToUpdate: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["id", "color", "groupId"],
        properties: {
          id: str,
          color: { type: ["string", "null"], enum: [...COLORS, null] },
          groupId: nullableStr,
        },
      },
    },
    links: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["from", "to", "label"],
        properties: { from: str, to: str, label: nullableStr },
      },
    },
    groups: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        required: ["groupId", "title", "bubbleIds"],
        properties: { groupId: str, title: str, bubbleIds: strArray },
      },
    },
    speech: str,
  },
} as const;

// ---------- TTS ----------

export const TTS_MAX_CHARS = 200;

export const TTSRequestSchema = z.object({
  text: z.string().trim().min(1).max(TTS_MAX_CHARS),
  language: z.string().max(16).default("en-US"),
  voice: z.string().max(32).default("default"),
  format: z.enum(["wav"]).default("wav"),
});
export type TTSRequest = z.infer<typeof TTSRequestSchema>;
