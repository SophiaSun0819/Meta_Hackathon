// Deterministic responses built without a model. Used in two places:
// - mock mode (no OPENAI_API_KEY), so the Unity side can be built before keys exist;
// - backend fallback, when the model fails twice.
// Output always passes the same sanitize step as model output.
import { COLORS, type AIRequest, type Bubble, type Color, type ModelOutput } from "./schema";

const STOP_WORDS = new Set(
  "a an and are as at be but by for from have i if in into is it its i'm let's like maybe me my of on or our so some that the their then there this to up we what when with would you your want think about could should really just also plan make need try get going gonna kind sort thing things".split(
    " ",
  ),
);

function titleCase(w: string): string {
  return w.charAt(0).toUpperCase() + w.slice(1).toLowerCase();
}

// Up to 3 keywords: the longest distinct non-stop words, kept in spoken order.
export function extractKeywords(text: string): string[] {
  const words = text
    .toLowerCase()
    .replace(/[^a-z0-9'\s-]/g, " ")
    .split(/\s+/)
    .filter((w) => w.length > 2 && !STOP_WORDS.has(w));
  const unique = [...new Set(words)];
  const top = new Set([...unique].sort((a, b) => b.length - a.length).slice(0, 3));
  const picked = unique.filter((w) => top.has(w)).map(titleCase);
  return picked.length > 0 ? picked : ["Idea"];
}

function colorAt(i: number): Color {
  return COLORS[i % COLORS.length];
}

function label(b: Bubble): string {
  return b.keywords[0] ?? "this idea";
}

const empty = (): ModelOutput => ({ bubblesToCreate: [], bubblesToUpdate: [], links: [], groups: [], speech: "" });

export function buildFallback(req: AIRequest): ModelOutput {
  const out = empty();
  const target = req.bubbles.find((b) => b.id === req.targetBubbleIds[0]);

  switch (req.action) {
    case "summarize": {
      const keywords = extractKeywords(req.transcript);
      out.bubblesToCreate.push({
        tempId: "new_1",
        kind: "idea",
        keywords,
        detail: [req.transcript.trim()],
        color: colorAt(req.bubbles.length),
        relatedTo: null,
      });
      out.speech = `Got it: ${keywords.join(", ")}.`;
      break;
    }
    case "connect": {
      // Split bubbles into up to 3 groups in order; good enough to exercise the client.
      const n = Math.min(3, Math.max(1, Math.ceil(req.bubbles.length / 3)));
      for (let g = 0; g < n; g++) {
        const members = req.bubbles.filter((_, i) => i % n === g);
        if (members.length === 0) continue;
        const groupId = `grp_${g + 1}`;
        const color = colorAt(g);
        out.groups.push({ groupId, title: `Group ${g + 1}`, bubbleIds: members.map((b) => b.id) });
        for (const b of members) out.bubblesToUpdate.push({ id: b.id, color, groupId });
        for (let i = 1; i < members.length; i++) {
          out.links.push({ from: members[0].id, to: members[i].id, label: null });
        }
      }
      out.speech = `I sorted your ideas into ${out.groups.length} group${out.groups.length === 1 ? "" : "s"}.`;
      break;
    }
    case "challenge": {
      if (!target) break;
      out.bubblesToCreate.push({
        tempId: "new_1",
        kind: "challenge",
        keywords: ["Who Is It For?"],
        detail: [`Who exactly benefits from ${label(target)}?`, "What would make someone skip it?"],
        color: "red",
        relatedTo: target.id,
      });
      out.links.push({ from: "new_1", to: target.id, label: "challenge" });
      out.speech = `Let me push back on ${label(target)}. Who is it really for?`;
      break;
    }
    case "expand": {
      if (!target) break;
      const color = target.color ?? "blue";
      const angles: [string, string][] = [
        ["Smaller Version", `A quick, low-effort take on ${label(target)}`],
        ["Bolder Version", `What ${label(target)} looks like with no limits`],
        ["Different Audience", `${label(target)} for a completely different group`],
      ];
      angles.forEach(([kw, d], i) => {
        const tempId = `new_${i + 1}`;
        out.bubblesToCreate.push({ tempId, kind: "expansion", keywords: [kw], detail: [d], color, relatedTo: target.id });
        out.links.push({ from: tempId, to: target.id, label: null });
      });
      out.speech = `Here are three directions for ${label(target)}. The bolder one could be fun.`;
      break;
    }
    case "next_steps": {
      const focus = target ?? req.bubbles[0];
      out.bubblesToCreate.push({
        tempId: "new_1",
        kind: "steps",
        keywords: ["Next Steps"],
        detail: [
          `Pick one version of ${focus ? label(focus) : "the idea"}`,
          "List what you need to try it",
          "Set a date for a first test",
          "Share it with one person for feedback",
        ],
        color: "blue",
        relatedTo: focus?.id ?? null,
      });
      if (focus) out.links.push({ from: "new_1", to: focus.id, label: "plan" });
      out.speech = "Start by picking one version to try this week.";
      break;
    }
    case "suggest_topics": {
      const topics: [string, string][] = [
        ["Weekend Team Activity", "Plan something fun the whole team would join"],
        ["New App Feature", "One feature that would make your favorite app better"],
        ["Better Mornings", "Small changes that make mornings less rushed"],
      ];
      topics.forEach(([kw, d], i) => {
        out.bubblesToCreate.push({
          tempId: `new_${i + 1}`,
          kind: "topic",
          keywords: [kw],
          detail: [d],
          color: colorAt(i),
          relatedTo: null,
        });
      });
      out.speech = "Here are a few places to start. Which one sounds good?";
      break;
    }
  }
  return out;
}
