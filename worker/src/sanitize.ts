// Makes model output safe for the client to apply: drops references to IDs the
// model made up, clamps lengths, and caps counts per action. Every change is
// reported in `warnings`.
import type { Action, AIRequest, ModelOutput } from "./schema";

const MAX_KEYWORDS = 3;
const MAX_KEYWORD_WORDS = 3;
const MAX_DETAIL = 4;
const MAX_DETAIL_WORDS = 15;
const MAX_SPEECH_WORDS = 30;

// How many new bubbles each action may create.
export const MAX_NEW_BUBBLES: Record<Action, number> = {
  summarize: 2,
  connect: 0,
  challenge: 1,
  expand: 3,
  next_steps: 1,
  suggest_topics: 4,
};

function clampWords(text: string, maxWords: number): string {
  const words = text.trim().split(/\s+/).filter(Boolean);
  return words.length <= maxWords ? words.join(" ") : words.slice(0, maxWords).join(" ");
}

// Cuts speech to the word limit, preferring to end at a sentence boundary.
export function clampSpeech(text: string, maxWords = MAX_SPEECH_WORDS): string {
  const clean = text.replace(/[*_`#>]/g, "").replace(/\s+/g, " ").trim();
  const words = clean.split(" ").filter(Boolean);
  if (words.length <= maxWords) return clean;
  const cut = words.slice(0, maxWords).join(" ");
  const lastEnd = Math.max(cut.lastIndexOf("."), cut.lastIndexOf("!"), cut.lastIndexOf("?"));
  return lastEnd > 0 ? cut.slice(0, lastEnd + 1) : cut + ".";
}

export function sanitize(req: AIRequest, out: ModelOutput): { output: ModelOutput; warnings: string[] } {
  const warnings: string[] = [];
  const existing = new Set(req.bubbles.map((b) => b.id));

  // New bubbles: cap count, give unique temp IDs, clamp text.
  const limit = MAX_NEW_BUBBLES[req.action];
  if (out.bubblesToCreate.length > limit) {
    warnings.push(`dropped ${out.bubblesToCreate.length - limit} extra new bubble(s) for ${req.action}`);
  }
  const tempIdMap = new Map<string, string>();
  const bubblesToCreate = out.bubblesToCreate.slice(0, limit).map((b, i) => {
    const tempId = `new_${i + 1}`;
    tempIdMap.set(b.tempId, tempId);
    let relatedTo = b.relatedTo;
    if (relatedTo !== null && !existing.has(relatedTo)) {
      warnings.push(`dropped unknown relatedTo ${relatedTo}`);
      relatedTo = null;
    }
    const keywords = b.keywords
      .map((k) => clampWords(k, MAX_KEYWORD_WORDS))
      .filter(Boolean)
      .slice(0, MAX_KEYWORDS);
    return {
      ...b,
      tempId,
      relatedTo,
      keywords: keywords.length > 0 ? keywords : ["Idea"],
      detail: b.detail.map((d) => clampWords(d, MAX_DETAIL_WORDS)).filter(Boolean).slice(0, MAX_DETAIL),
    };
  });

  const known = new Set([...existing, ...bubblesToCreate.map((b) => b.tempId)]);
  const resolve = (id: string): string | null => {
    const mapped = tempIdMap.get(id) ?? id;
    return known.has(mapped) ? mapped : null;
  };

  const bubblesToUpdate = out.bubblesToUpdate.filter((u) => {
    if (existing.has(u.id)) return true;
    warnings.push(`dropped update for unknown bubble ${u.id}`);
    return false;
  });

  const seenLinks = new Set<string>();
  const links = out.links.flatMap((l) => {
    const from = resolve(l.from);
    const to = resolve(l.to);
    if (from === null || to === null || from === to) {
      warnings.push(`dropped link ${l.from} -> ${l.to}`);
      return [];
    }
    const key = [from, to].sort().join("|");
    if (seenLinks.has(key)) return [];
    seenLinks.add(key);
    return [{ from, to, label: l.label === null ? null : clampWords(l.label, 4) }];
  });

  const groups = out.groups.flatMap((g) => {
    const bubbleIds = [...new Set(g.bubbleIds.map(resolve).filter((id): id is string => id !== null))];
    if (bubbleIds.length === 0) {
      warnings.push(`dropped empty group ${g.groupId}`);
      return [];
    }
    return [{ groupId: g.groupId, title: clampWords(g.title, 4) || "Group", bubbleIds }];
  });

  const speech = req.wantSpeech ? clampSpeech(out.speech) : "";

  return { output: { bubblesToCreate, bubblesToUpdate, links, groups, speech }, warnings };
}
