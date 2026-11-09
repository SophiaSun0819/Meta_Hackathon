"""Offline providers for mock mode and local fallback ideas.

Mock mode (THINKING_SPACE_MOCK_MODE=1) swaps every external AI provider for the deterministic
providers below, so the Unity client can be developed and tested without OpenAI or ElevenLabs
credentials. build_fallback_idea is also used in live mode when the idea provider fails, so a
spoken idea is never lost.
"""

import itertools
import re

from conversation_orchestrator import ConversationModelOutput, ConversationRequest
from idea_manager import IdeaAction, IdeaModelOutput, IdeaProcessRequest, SUMMARY_HARD_MAX_WORDS
from intent_router import FollowUpModelOutput, IntentModelOutput, IntentRouteRequest
from voice_service import VoiceConfigurationError

MOCK_TRANSCRIPTS = (
    "I want to build an AR game that teaches chemistry by mixing virtual elements.",
    "What if a cooking app showed recipe steps floating above the stove?",
    "Can you expand that idea?",
    "Combine those two ideas.",
    "What are the risks of that idea?",
    "How do people usually brainstorm in teams?",
)

FALLBACK_CATEGORY = "General"
FALLBACK_TITLE = "New idea"
MAX_TITLE_WORDS = 5
STOP_WORDS = frozenset("""
a about after again all also am an and any are as at be because been but by can could did do does
doing for from get give had has have how i if in into is it its just like make me might more most my
need of on one or our out over please should so some something than that the their them then there
these they think this those to up us use using want was we were what when where which while who why
will with would you your idea ideas maybe really thing things kind sort lot
""".split())
WORD_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9'-]*")


class MockTranscriber:
    """Cycles through fixed transcripts so each mock recording produces a predictable result."""

    def __init__(self, transcripts=MOCK_TRANSCRIPTS):
        self._transcripts = itertools.cycle(transcripts)

    def next_transcript(self) -> str:
        return next(self._transcripts)


class MockConversationProvider:
    """Keyword-based stand-in for the OpenAI conversation provider."""

    async def respond(self, request: ConversationRequest) -> ConversationModelOutput:
        text = request.selected_transcript.strip()
        lowered = text.lower()
        idea_ids = [idea.idea_id for idea in request.available_ideas]
        recent = request.context.most_recent_idea_id or (idea_ids[-1] if idea_ids else None)

        if _has_any(lowered, ("combine", "merge", "mix ")) and len(idea_ids) >= 2:
            targets = _combination_targets(idea_ids, recent)
            return _output("idea_modification", "combine_ideas", targets,
                           "Combining those two could spark something new.",
                           "excited", relationship="combination")
        if _has_any(lowered, ("expand", "more detail", "build on", "develop")) and recent:
            return _output("idea_modification", "expand_idea", [recent],
                           "Let's build on that idea and take it further.",
                           "excited", relationship="derived_expansion")
        if _has_any(lowered, ("risk", "challenge", "problem", "weakness", "wrong with")) and recent:
            return _output("conversational_idea_challenge", "conversational_challenge", [recent],
                           "One risk to consider: who maintains it after launch, and what happens "
                           "if early users don't come back?",
                           "thinking", discourse_context="challenge")
        if lowered.endswith("?") or lowered.split(" ", 1)[0] in {
                "what", "how", "why", "who", "when", "where", "can", "could", "should", "is", "are"}:
            return _output("general_conversation", "converse", [],
                           "That's a good question. In mock mode I can only give short canned "
                           "answers, but the live service will answer this properly.",
                           "attentive")
        return _output("clear_new_idea", "create_idea", [],
                       "That sounds like a promising idea.", "happy",
                       relationship="standalone", create=True)


class MockIdeaProvider:
    """Builds ideas locally from the transcript and the referenced ideas."""

    async def generate(self, request: IdeaProcessRequest) -> IdeaModelOutput:
        return build_fallback_idea(request, mock=True)


class MockIntentProvider:
    """Minimal stand-in for the legacy intent-routing endpoint."""

    async def classify(self, request: IntentRouteRequest) -> IntentModelOutput:
        return IntentModelOutput(intent="converse", pending_action=None, target_idea_ids=[],
                                 scale=None, direction=None, question=None,
                                 response_text="Mock mode is on, so I'm only chatting for now.",
                                 emotion="neutral")

    async def resolve_follow_up(self, request: IntentRouteRequest, pending) -> FollowUpModelOutput:
        return FollowUpModelOutput(resolution="cancel", selected_idea_ids=[],
                                   response_text="Mock mode is on, so let's skip that for now.",
                                   emotion="neutral")


class MockVoiceProvider:
    """Mock mode has no generated audio; Unity falls back to subtitles on a 503."""

    async def synthesize(self, text: str) -> bytes:
        raise VoiceConfigurationError("Voice is disabled in mock mode.")


def build_fallback_idea(request: IdeaProcessRequest, mock: bool = False) -> IdeaModelOutput:
    """Turn a request into a saved idea without any AI provider.

    Live fallback only uses this for create_idea, where the transcript itself is the idea.
    Mock mode also uses it for expansion, challenge and combination.
    """
    transcript = _one_paragraph(request.selected_transcript)
    ideas = {idea.idea_id: idea for idea in request.existing_ideas}
    if request.action == IdeaAction.COMBINE:
        sources = [ideas[idea_id] for idea_id in request.related_idea_ids if idea_id in ideas]
        source_titles = [idea.title for idea in sources] or ["the selected ideas"]
        title = _limit_title(" + ".join(source_titles))
        summary = _limit_words("Combines " + " and ".join(source_titles) + ". "
                               + " ".join(_first_sentence(idea.summary) for idea in sources))
        spoken = "Here's a combined idea from " + " and ".join(source_titles) + "."
    elif request.action in (IdeaAction.EXPAND, IdeaAction.CHALLENGE):
        source = ideas.get(request.idea_id or "")
        base_title = source.title if source else FALLBACK_TITLE
        if request.action == IdeaAction.EXPAND:
            title = _limit_title(base_title + " extended")
            summary = _limit_words("Builds on " + base_title + ". " + transcript)
            spoken = "Here's an expansion of " + base_title + "."
        else:
            title = _limit_title(base_title + " risks")
            summary = _limit_words("Open questions for " + base_title
                                   + ": who it serves first, what it costs to run, and how it is tested.")
            spoken = "Here are some questions to test " + base_title + "."
    else:
        title = keyword_title(transcript)
        summary = _limit_words(transcript)
        spoken = ("Here's that idea." if mock
                  else "My full thinking isn't available right now, so I kept your idea in your own words.")
    return IdeaModelOutput(title=title, summary=summary or transcript or title,
                           category=FALLBACK_CATEGORY, spoken_response=spoken)


def keyword_title(text: str) -> str:
    """Pick up to five content words, in order, as a readable title."""
    seen: set[str] = set()
    words = []
    for word in WORD_PATTERN.findall(text):
        key = word.lower().strip("'-")
        is_acronym = len(word) >= 2 and word.isupper()
        if (len(key) < 3 and not is_acronym) or key in STOP_WORDS or key in seen:
            continue
        seen.add(key)
        words.append(word if word.isupper() else word[0].upper() + word[1:].lower())
        if len(words) == MAX_TITLE_WORDS:
            break
    return " ".join(words) if words else FALLBACK_TITLE


def _output(category, action, targets, response, emotion, relationship="none",
            discourse_context="general", create=False) -> ConversationModelOutput:
    return ConversationModelOutput(
        intent_category=category, proposed_action=action, target_idea_ids=targets,
        authorization="authorized", discourse_context=discourse_context,
        response_mode="action_proposal" if create or relationship != "none" else "conversational_answer",
        response_style="brief", idea_relationship=relationship, spoken_response=response,
        suggested_emotion=emotion, should_create_idea=create)


def _combination_targets(idea_ids: list[str], recent: str | None) -> list[str]:
    if recent in idea_ids:
        others = [idea_id for idea_id in idea_ids if idea_id != recent]
        return [others[-1], recent]
    return idea_ids[-2:]


def _has_any(text: str, keywords) -> bool:
    return any(keyword in text for keyword in keywords)


def _one_paragraph(text: str) -> str:
    cleaned = "".join(character for character in text if ord(character) >= 32 or character in "\n\t")
    return re.sub(r"\s+", " ", cleaned).strip()


def _first_sentence(text: str) -> str:
    sentence = re.split(r"(?<=[.!?])\s", _one_paragraph(text), maxsplit=1)[0]
    return sentence if sentence.endswith((".", "!", "?")) else sentence + "."


def _limit_words(text: str, maximum: int = SUMMARY_HARD_MAX_WORDS - 5) -> str:
    words = _one_paragraph(text).replace("*", "").replace("`", "").split(" ")
    if len(words) <= maximum:
        return " ".join(words).lstrip("#>-+ ")
    return " ".join(words[:maximum]).rstrip(",;:").lstrip("#>-+ ") + "..."


def _limit_title(text: str) -> str:
    return text if len(text) <= 120 else text[:117].rstrip() + "..."
