"""Deterministic mock-only Orby navigation router used by tests.

This module is deliberately located under Backend/tests. It is not imported by app.py, does not
change production prompts or schemas, and never performs provider or network calls.
"""

import re
from dataclasses import dataclass

from orby_navigation_contract_v1_1 import (
    NavigationAction,
    NavigationCommand,
    NavigationRequest,
)


@dataclass(frozen=True)
class MockIdea:
    idea_id: str
    title: str


class MockNavigationRouter:
    capability = "companion_navigation.v1"

    def route(self, request_payload: dict, transcript: str,
              ideas: list[MockIdea]) -> NavigationCommand | None:
        """Return a proposed 1.1 response, or None to preserve legacy routing."""
        if request_payload.get("contract_version") != "1.1" \
                or self.capability not in request_payload.get("client_capabilities", []):
            return None
        request = NavigationRequest.model_validate(request_payload)
        text = " ".join(transcript.lower().split())
        context = request.navigation_context

        if self._contains(text, r"\b(?:stop|halt|freeze)\b"):
            if not context.active_operation_id or context.navigation_state == "idle":
                return self._blocked(request, NavigationAction.STOP, "none", "immediate",
                                     "operation_not_active",
                                     "There isn't an active navigation operation to stop.")
            return self._response(request, NavigationAction.STOP, "none", "default", "immediate",
                                  "authorized", True, "I'll stop moving.")
        if self._contains(text, r"\b(?:resume|continue)\b"):
            if not context.resumable or not context.active_operation_id \
                    or context.navigation_state != "paused":
                return self._blocked(request, NavigationAction.RESUME, "none", "resume",
                                     "operation_not_resumable",
                                     "There isn't a paused navigation operation I can resume.")
            return self._response(request, NavigationAction.RESUME, "none", "default", "resume",
                                  "authorized", True, "I can resume the paused movement.")
        if self._contains(text, r"\b(?:return|go|back)\b.*\bhome\b|\bhome\b"):
            if not context.home_anchor_available:
                return self._blocked(request, NavigationAction.RETURN_HOME, "home", "one_shot",
                                     "destination_unavailable",
                                     "My home position is unavailable, so I'll stay here.")
            return self._response(request, NavigationAction.RETURN_HOME, "home", "default",
                                  "one_shot", "authorized", True,
                                  "I can return to my home position.")
        if self._contains(text, r"\bfollow\b"):
            if not context.user_anchor_available:
                return self._blocked(request, NavigationAction.FOLLOW_USER, "user", "continuous",
                                     "destination_unavailable",
                                     "Your position is unavailable, so I'll stay here.")
            return self._response(request, NavigationAction.FOLLOW_USER, "user", "front_right",
                                  "continuous", "authorized", True, "I can follow you.")
        if text in {"come closer", "move closer", "get closer"}:
            return self._clarification(request, NavigationAction.REPOSITION,
                "Should I move Orby closer to you, or move beside an idea?", ideas,
                destination="user", relative="front")
        if self._contains(text, r"\b(?:approach|beside|next to)\b"):
            selected = context.selected_idea_id if "selected idea" in text else None
            matches = [idea for idea in ideas if idea.idea_id == selected] if selected else [
                idea for idea in ideas if idea.idea_id.lower() in text or idea.title.lower() in text]
            if len(matches) == 1:
                return self._response(request, NavigationAction.APPROACH_IDEA, "idea",
                    "viewing_side", "one_shot", "authorized", True,
                    f"I can move beside {matches[0].title}.", [matches[0].idea_id])
            if ideas:
                return self._clarification(request, NavigationAction.APPROACH_IDEA,
                    "Which idea should I move beside?", ideas, destination="idea",
                    relative="viewing_side")
            return self._blocked(request, NavigationAction.APPROACH_IDEA, "idea", "one_shot",
                                 "destination_unavailable",
                                 "I couldn't find that idea, so I'll stay here.")

        position = next((value for value in ("front_left", "front_right", "left", "right",
            "above", "below", "front") if value.replace("_", " ") in text), None)
        if position and self._contains(text, r"\b(?:move|stand|reposition)\b"):
            destination = "viewport_anchor" if context.platform == "web_2d" else "user"
            return self._response(request, NavigationAction.REPOSITION, destination, position,
                                  "one_shot", "authorized", True,
                                  f"I can move to the {position.replace('_', ' ')}.")
        return None

    @staticmethod
    def _contains(value: str, pattern: str) -> bool:
        return re.search(pattern, value, flags=re.IGNORECASE) is not None

    def _blocked(self, request, action, destination, mode, failure, speech):
        return self._response(request, action, destination, "default", mode, "blocked", False,
                              speech, failure=failure)

    def _clarification(self, request, action, speech, ideas, destination, relative):
        candidates = sorted(idea.idea_id for idea in ideas)
        return self._response(request, action, destination, relative, "one_shot",
            "clarification_required", False, speech, candidates=candidates,
            clarification_id=f"clarify-{request.request_id}")

    @staticmethod
    def _response(request, action, destination, relative, mode, disposition, execute, speech,
                  targets=None, candidates=None, clarification_id=None, failure=None):
        candidates = candidates or []
        return NavigationCommand.model_validate({
            "contract_version": "1.1", "request_id": request.request_id,
            "session_id": request.session_id, "intent_category": "companion_navigation",
            "proposed_action": action.value, "target_idea_ids": targets or [],
            "navigation": {"destination_kind": destination, "relative_position": relative,
                "distance_band": "standard", "movement_mode": mode,
                "operation_scope": "companion_only", "execution_disposition": disposition,
                "failure_code": failure},
            "spoken_response": speech,
            "requires_confirmation": disposition == "clarification_required",
            "clarification_id": clarification_id,
            "clarification_question": speech if clarification_id else None,
            "candidate_idea_ids": candidates, "should_execute_action": execute,
            "status": "completed", "error": None,
        })
