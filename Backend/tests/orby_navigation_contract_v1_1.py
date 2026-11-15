"""Authoritative mock-only proposal for Orby companion navigation contract 1.1.

This module intentionally lives under tests. Production conversation contract 1.0 does not
import it, advertise it, or route any navigation action.
"""

from enum import Enum
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

IDENTIFIER_PATTERN = r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$"
NAVIGATION_CAPABILITY = "companion_navigation.v1"
COMMAND_MAX_AGE_MS = 30_000


class NavigationAction(str, Enum):
    FOLLOW_USER = "follow_user"
    APPROACH_IDEA = "approach_idea"
    RETURN_HOME = "return_companion_home"
    REPOSITION = "reposition_companion"
    STOP = "stop_companion_navigation"
    RESUME = "resume_companion_navigation"


class NavigationContext(BaseModel):
    model_config = ConfigDict(extra="forbid")
    platform: Literal["quest_3d", "web_3d", "web_2d"]
    navigation_state: Literal["idle", "planning", "moving", "following", "paused", "blocked"]
    active_operation_id: str | None = Field(default=None, pattern=IDENTIFIER_PATTERN)
    selected_idea_id: str | None = Field(default=None, pattern=IDENTIFIER_PATTERN)
    user_anchor_available: bool
    home_anchor_available: bool
    resumable: bool


class NavigationRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contract_version: Literal["1.1"]
    request_id: str = Field(pattern=IDENTIFIER_PATTERN)
    session_id: str = Field(min_length=1, max_length=128)
    client_capabilities: list[Literal["companion_navigation.v1"]]
    navigation_context: NavigationContext

    @model_validator(mode="after")
    def require_capability(self):
        if NAVIGATION_CAPABILITY not in self.client_capabilities:
            raise ValueError("Navigation capability is required.")
        return self


class NavigationParameters(BaseModel):
    model_config = ConfigDict(extra="forbid")
    destination_kind: Literal["user", "idea", "home", "viewport_anchor", "none"]
    relative_position: Literal[
        "front", "front_left", "front_right", "left", "right", "above", "below",
        "viewing_side", "default"
    ]
    distance_band: Literal["near", "standard", "far"]
    movement_mode: Literal["one_shot", "continuous", "immediate", "resume"]
    operation_scope: Literal["companion_only"]
    execution_disposition: Literal["authorized", "clarification_required", "blocked"]
    failure_code: Literal[
        "destination_unavailable", "operation_not_active", "operation_not_resumable"
    ] | None = None


class NavigationCommand(BaseModel):
    """Exact command accepted by the committed WEB-014 local adapter."""

    model_config = ConfigDict(extra="forbid")
    contract_version: Literal["1.1"]
    request_id: str = Field(pattern=IDENTIFIER_PATTERN)
    session_id: str = Field(min_length=1, max_length=128)
    intent_category: Literal["companion_navigation"]
    proposed_action: NavigationAction
    target_idea_ids: list[str] = Field(max_length=1)
    navigation: NavigationParameters
    spoken_response: str = Field(min_length=1, max_length=1200)
    requires_confirmation: bool
    clarification_id: str | None = Field(default=None, pattern=IDENTIFIER_PATTERN)
    clarification_question: str | None = Field(default=None, max_length=1200)
    candidate_idea_ids: list[str] = Field(max_length=8)
    should_execute_action: bool
    status: Literal["completed"]
    error: None = None

    @model_validator(mode="after")
    def validate_semantics(self):
        action, nav = self.proposed_action, self.navigation
        if action == NavigationAction.FOLLOW_USER and not (
                nav.destination_kind == "user" and nav.movement_mode == "continuous"
                and not self.target_idea_ids):
            raise ValueError("Follow-user semantics are invalid.")
        if action == NavigationAction.APPROACH_IDEA:
            if nav.destination_kind != "idea" or nav.movement_mode != "one_shot":
                raise ValueError("Approach-idea semantics are invalid.")
            if nav.execution_disposition == "authorized" and len(self.target_idea_ids) != 1:
                raise ValueError("Authorized approach requires one idea target.")
            if nav.execution_disposition == "clarification_required" and self.target_idea_ids:
                raise ValueError("Ambiguous approach cannot select a target.")
        if action == NavigationAction.RETURN_HOME and not (
                nav.destination_kind == "home" and nav.movement_mode == "one_shot"
                and not self.target_idea_ids):
            raise ValueError("Return-home semantics are invalid.")
        if action == NavigationAction.REPOSITION and not (
                nav.destination_kind in {"user", "viewport_anchor"}
                and nav.movement_mode == "one_shot" and not self.target_idea_ids):
            raise ValueError("Reposition semantics are invalid.")
        if action == NavigationAction.STOP and not (
                nav.destination_kind == "none" and nav.movement_mode == "immediate"
                and not self.target_idea_ids):
            raise ValueError("Stop semantics are invalid.")
        if action == NavigationAction.RESUME and not (
                nav.destination_kind == "none" and nav.movement_mode == "resume"
                and not self.target_idea_ids):
            raise ValueError("Resume semantics are invalid.")
        if nav.execution_disposition == "authorized":
            if self.requires_confirmation or not self.should_execute_action or nav.failure_code:
                raise ValueError("Authorized navigation flags conflict.")
        elif self.should_execute_action:
            raise ValueError("Non-authorized navigation cannot execute.")
        if nav.execution_disposition == "clarification_required" and not (
                self.requires_confirmation and self.clarification_id
                and self.clarification_question and self.candidate_idea_ids):
            raise ValueError("Clarification metadata is incomplete.")
        if nav.execution_disposition == "blocked" and not nav.failure_code:
            raise ValueError("Blocked navigation requires a stable failure code.")
        return self


class CapabilityOffer(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contract_versions: list[Literal["1.0", "1.1"]]
    capabilities: list[Literal["companion_navigation.v1"]] = Field(default_factory=list)
    platform: Literal["quest_3d", "web_3d", "web_2d"]


class CapabilitySelection(BaseModel):
    model_config = ConfigDict(extra="forbid")
    active_contract_version: Literal["1.0", "1.1"]
    enabled_capabilities: list[Literal["companion_navigation.v1"]] = Field(default_factory=list)
    command_max_age_ms: Literal[30000] = COMMAND_MAX_AGE_MS
    acknowledgement_required: bool

    @model_validator(mode="after")
    def validate_selection(self):
        navigation_enabled = NAVIGATION_CAPABILITY in self.enabled_capabilities
        if navigation_enabled != (self.active_contract_version == "1.1"):
            raise ValueError("Navigation requires contract 1.1 and explicit capability selection.")
        if navigation_enabled and not self.acknowledgement_required:
            raise ValueError("Navigation commands require acknowledgement.")
        return self


class AuthenticatedSessionBinding(BaseModel):
    """Trusted transport identity; never supplied inside an AI command payload."""

    model_config = ConfigDict(extra="forbid")
    session_id: str = Field(min_length=1, max_length=128)
    subject_id: str = Field(pattern=IDENTIFIER_PATTERN)
    client_instance_id: str = Field(pattern=IDENTIFIER_PATTERN)
    authenticated_at_ms: int = Field(ge=0)
    expires_at_ms: int = Field(gt=0)

    @model_validator(mode="after")
    def validate_window(self):
        if self.expires_at_ms <= self.authenticated_at_ms:
            raise ValueError("Session binding expiry must follow authentication.")
        return self


class NavigationAcknowledgement(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contract_version: Literal["1.1"]
    request_id: str = Field(pattern=IDENTIFIER_PATTERN)
    session_id: str = Field(min_length=1, max_length=128)
    operation_id: str | None = Field(default=None, pattern=IDENTIFIER_PATTERN)
    status: Literal["accepted", "rejected"]
    failure_code: Literal[
        "feature_disabled", "invalid_command", "not_authorized", "operation_not_resumable",
        "replayed_command", "session_binding_expired", "stale_command", "unknown_idea",
        "wrong_session"
    ] | None = None

    @model_validator(mode="after")
    def validate_status(self):
        if self.status == "accepted" and (not self.operation_id or self.failure_code):
            raise ValueError("Accepted acknowledgement requires an operation and no failure.")
        if self.status == "rejected" and (self.operation_id or not self.failure_code):
            raise ValueError("Rejected acknowledgement requires a failure and no operation.")
        return self


class NavigationExecutionResult(BaseModel):
    model_config = ConfigDict(extra="forbid")
    contract_version: Literal["1.1"]
    request_id: str = Field(pattern=IDENTIFIER_PATTERN)
    session_id: str = Field(min_length=1, max_length=128)
    operation_id: str = Field(pattern=IDENTIFIER_PATTERN)
    action: NavigationAction
    target_idea_ids: list[str] = Field(max_length=1)
    status: Literal["completed", "failed", "interrupted", "cancelled"]
    failure_code: Literal[
        "destination_unavailable", "interrupted_by_new_command", "movement_failed",
        "session_ended"
    ] | None = None

    @model_validator(mode="after")
    def validate_result(self):
        if self.status == "completed" and self.failure_code:
            raise ValueError("Completed results cannot include a failure.")
        if self.status != "completed" and not self.failure_code:
            raise ValueError("Non-completed results require a stable failure.")
        if self.action == NavigationAction.APPROACH_IDEA and len(self.target_idea_ids) != 1:
            raise ValueError("Approach results require the bound idea target.")
        if self.action != NavigationAction.APPROACH_IDEA and self.target_idea_ids:
            raise ValueError("Only approach results may bind an idea target.")
        return self


def negotiate_capability(offer: CapabilityOffer, server_navigation_enabled: bool) -> CapabilitySelection:
    enabled = (server_navigation_enabled and "1.1" in offer.contract_versions
               and NAVIGATION_CAPABILITY in offer.capabilities)
    return CapabilitySelection(
        active_contract_version="1.1" if enabled else "1.0",
        enabled_capabilities=[NAVIGATION_CAPABILITY] if enabled else [],
        acknowledgement_required=enabled,
    )
