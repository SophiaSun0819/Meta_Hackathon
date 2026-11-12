using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using ThinkingSpace.Companion;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceAIWorkflowController : MonoBehaviour
    {
        [SerializeField, Min(15f)] private float clarificationLifetimeSeconds = 120f;
        private ThinkingSpaceAIBackendClient client;
        private AIResponsePanel responsePanel;
        private BrainstormMenuController menuController;
        private OrbyConversationStateController orby;
        private IdeaBubbleManager bubbleManager;
        private Coroutine activeOperation;
        private int operationGeneration;
        private bool sessionActive, awaitingDismissal;
        private bool automaticConversationMode;
        private string activeSessionId, pendingClarificationId, pendingOriginalTranscript, pendingAction;
        private string resolvedResponseLanguage, resolvedVoiceProfileId;
        private string[] pendingCandidateIds = Array.Empty<string>();
        private float clarificationExpiresAt;

        private static readonly HashSet<string> IdeaActions = new HashSet<string>
        { "create_idea", "expand_idea", "combine_ideas" };
        private static readonly HashSet<string> AllowedActions = new HashSet<string>
        { "converse", "brainstorm_suggestions", "create_idea", "expand_idea", "combine_ideas",
          "conversational_challenge", "start_session", "resume_session", "end_session", "open_menu",
          "close_menu", "show_saved_sessions", "change_recording_settings", "move_bubble",
          "resize_bubble", "show_bubble", "hide_bubble", "delete_idea", "delete_session",
          "clarification_required", "cancel", "none" };
        private static readonly HashSet<string> AllowedCategories = new HashSet<string>
        { "general_conversation", "brainstorming_suggestions", "clear_new_idea",
          "ambiguous_potential_idea", "idea_modification", "conversational_idea_challenge",
          "application_command", "spatial_command", "contextual_follow_up", "clarification_required" };
        private static readonly HashSet<string> AllowedEmotions = new HashSet<string>
        { "neutral", "attentive", "thinking", "happy", "excited", "concerned", "confused" };
        private static readonly Regex IdentifierPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant);

        public bool IsBlockingRecording => activeOperation != null || awaitingDismissal;
        public bool IsBlockingLocalBubbleInteraction => awaitingDismissal;
        public event Action<bool> AutomaticTurnCompleted;
        public void SetAutomaticConversationMode(bool enabled) => automaticConversationMode = enabled;
        public bool HasPendingClarification => !string.IsNullOrEmpty(pendingClarificationId);
        public string PendingClarificationId => pendingClarificationId;
        public string LastSubmissionRejection { get; private set; } = "None";
        public string CurrentStage { get; private set; } = "Idle";
        public string LastSuccessfulStage { get; private set; } = "None";
        public string LastRequestId { get; private set; } = "None";
        public string LastResponseValidation { get; private set; } = "Not evaluated";
        public string LastIntentCategory { get; private set; } = "None";
        public string LastProposedAction { get; private set; } = "None";
        public string LastClarificationState { get; private set; } = "None";
        public string LastAutomaticError { get; private set; } = string.Empty;

        public void Configure(SpeechTranscriptionService transcriptionService, Transform headset)
        {
            client = GetComponent<ThinkingSpaceAIBackendClient>();
            if (client == null) client = gameObject.AddComponent<ThinkingSpaceAIBackendClient>();
            client.Configure(transcriptionService);
            responsePanel = GetComponent<AIResponsePanel>();
            if (responsePanel == null) responsePanel = gameObject.AddComponent<AIResponsePanel>();
            responsePanel.SetHeadset(headset);
            ResolveDependencies();
            bubbleManager = GetComponent<IdeaBubbleManager>();
            if (bubbleManager == null) bubbleManager = gameObject.AddComponent<IdeaBubbleManager>();
            bubbleManager.Configure(() => Store, headset, HandleIdeaDeleted,
                StartIdeaSummaryPlayback, StopIdeaSummaryPlayback);
        }

        public void SetSessionActive(bool active)
        {
            sessionActive = active;
            ResolveDependencies();
            string nextSession = active && Store?.ActiveSession != null
                ? Store.ActiveSession.SessionId : string.Empty;
            if (!active || (!string.IsNullOrEmpty(activeSessionId) && activeSessionId != nextSession))
            {
                CancelAndClear();
                responsePanel?.Hide();
            }
            activeSessionId = nextSession;
            bubbleManager?.SetSessionActive(active, nextSession);
            if (!active) SetOrby(OrbyConversationState.Idle);
        }

        private void Start()
        {
            // All scene Awake calls have completed here, so a restored menu session can be
            // resolved even when component initialization order differed on device.
            if (!sessionActive) return;
            ResolveDependencies();
            BrainstormSession restored = Store?.ActiveSession;
            if (restored == null) return;
            activeSessionId = restored.SessionId;
            bubbleManager?.SetSessionActive(true, activeSessionId);
        }

        public bool SubmitApprovedTranscript(string rawTranscript)
        {
            ResolveDependencies();
            BrainstormSessionStore store = Store;
            if (!sessionActive)
            {
                LastSubmissionRejection = "The AI workflow does not have an active brainstorming session.";
                return false;
            }
            if (store?.ActiveSession == null)
            {
                LastSubmissionRejection = "The active session could not be resolved from the session store.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(rawTranscript))
            {
                LastSubmissionRejection = "The approved transcript is empty.";
                return false;
            }
            if (activeOperation != null)
            {
                LastSubmissionRejection = "Another AI request is already in progress.";
                return false;
            }
            LastSubmissionRejection = "None";
            LastAutomaticError = string.Empty;
            activeSessionId = store.ActiveSession.SessionId;
            bubbleManager?.SetSessionActive(true, activeSessionId);
            if (HasPendingClarification && Time.unscaledTime >= clarificationExpiresAt)
            {
                ClearClarification();
                CurrentStage = "Expired clarification cleared; routing as a new request";
            }
            string transcript = rawTranscript;
            if(!HasPendingClarification&&TryGetDirectGreetingResponse(transcript,out string greeting))
                return HandleDirectGreeting(transcript,greeting);
            string requestId = NewRequestId("route");
            LastRequestId = requestId;
            CurrentStage = "Routing intent";
            LastResponseValidation = "Awaiting response";
            store.AppendConversation(activeSessionId, "user", transcript,
                HasPendingClarification ? "clarification_answer" : "approved_transcript", requestId);
            activeOperation = StartCoroutine(Route(transcript, requestId, ++operationGeneration));
            awaitingDismissal = false;
            responsePanel.Hide();
            SetOrby(OrbyConversationState.Thinking);
            return true;
        }

        private bool HandleDirectGreeting(string transcript,string response)
        {
            string requestId=NewRequestId("greeting");LastRequestId=requestId;
            Store.AppendConversation(activeSessionId,"user",transcript,"approved_transcript",requestId);
            Store.AppendConversation(activeSessionId,"orby",response,"direct_greeting",requestId);
            CurrentStage="Direct greeting acknowledged";LastSuccessfulStage=CurrentStage;
            LastIntentCategory="general_conversation";LastProposedAction="converse";
            responsePanel.Hide();awaitingDismissal=false;
            OrbyVoiceDirector director=OrbyVoiceDirector.Instance;
            if(automaticConversationMode)
            {
                string voiceRequest=director?.SpeakDynamicTracked(response,activeSessionId,
                    OrbyVoicePriority.Acknowledgement,
                    success=>CompleteAutomaticSpeech(success,response),"direct_greeting","happy");
                if(string.IsNullOrEmpty(voiceRequest))CompleteAutomaticSpeech(false,response);
            }
            else
            {
                ShowMessage("Orby",response,"Close");
                director?.SpeakDynamic(response,activeSessionId,OrbyVoicePriority.Acknowledgement,
                    null,"direct_greeting","happy");
            }
            return true;
        }

        public static bool TryGetDirectGreetingResponse(string transcript,out string response)
        {
            string normalized=Regex.Replace((transcript??string.Empty).Trim().ToLowerInvariant(),
                "[^\\p{L}\\p{N}]+"," ").Trim();
            bool nameOnly=normalized=="orby";
            bool greeting=normalized=="hey orby"||normalized=="hi orby"
                ||normalized=="hello orby"||normalized=="hey there orby"
                ||normalized=="good morning orby"||normalized=="good afternoon orby"
                ||normalized=="good evening orby";
            if(!nameOnly&&!greeting){response=null;return false;}
            response=nameOnly?"I'm here. What's on your mind?":"Hey! What's on your mind?";
            return true;
        }

        public void NotifyListening() => SetOrby(OrbyConversationState.Listening);
        public void NotifyReviewing() => SetOrby(OrbyConversationState.Reviewing);
        public void NotifyThinking() => SetOrby(OrbyConversationState.Thinking);
        public void NotifyIdle() => SetOrby(OrbyConversationState.Idle);
        public void NotifyError(string message)
        {
            ResolveDependencies();
            if (orby != null) orby.SetError(message);
        }

        private IEnumerator Route(string transcript, string requestId, int generation)
        {
            BrainstormSession session = Store.ActiveSession;
            bool followUp = HasPendingClarification;
            ConversationProcessResponse response = null;
            AIBackendResult<ConversationProcessResponse> result = null;
            AIIdeaContext[] availableIdeas = BuildIdeaContexts(session);
            ConversationContextPayload conversationContext = BuildConversationContext(session,
                availableIdeas, requestId, menuController?.PreferencesStore?.Data?.PreferredName);
            ApplyLanguagePreferences(conversationContext, menuController?.PreferencesStore?.Data);
            var request = new ConversationProcessRequest
            {
                request_id = requestId,
                session_id = activeSessionId,
                raw_transcript = transcript,
                cleaned_transcript = null,
                transcript_source = "raw",
                available_ideas = availableIdeas,
                context = conversationContext,
                clarification_id = followUp ? pendingClarificationId : null
            };
            yield return client.ProcessConversation(request, value => result = value);
            activeOperation = null;
            if (!IsCurrent(generation, activeSessionId)) yield break;
            if (result == null || !result.Success)
            {
                if (followUp) ClearClarification();
                CurrentStage = "Intent request failed";
                ShowError(L("ai.request_failed"), result != null ? result.Error : "Intent routing returned no result.",
                    () => SubmitApprovedTranscript(transcript));
                SpeakFallbackIfAvailable(result?.FallbackSpokenResponse,"conversation_error_fallback");
                yield break;
            }
            response = result.Response;
            if (followUp && response != null && response.status == "error")
            {
                string message = string.IsNullOrWhiteSpace(response.error)
                    ? "That clarification is no longer available. Please repeat the request."
                    : response.error;
                ClearClarification();
                CurrentStage = "Clarification unavailable";
                ShowError(L("ai.clarification_ended"), message, null);
                yield break;
            }
            string validation = ValidateConversationResponse(response, requestId, activeSessionId,
                session, followUp ? pendingClarificationId : null);
            LastResponseValidation = string.IsNullOrEmpty(validation) ? "Valid" : validation;
            if (!string.IsNullOrEmpty(validation))
            {
                if (followUp) ClearClarification();
                CurrentStage = "Intent validation failed";
                ShowError(L("ai.invalid_response"), validation, null);
                yield break;
            }
            Store.AppendConversation(activeSessionId, "orby", response.spoken_response,
                "conversation_response", requestId, response.target_idea_ids);
            LastIntentCategory = response.intent_category;
            LastProposedAction = response.proposed_action;
            LastClarificationState = response.requires_confirmation
                ? "Pending " + response.clarification_id : "None";
            LastSuccessfulStage = "Intent response validated";
            resolvedResponseLanguage = NormalizeResolvedLanguage(response.response_language);
            resolvedVoiceProfileId = NormalizeResolvedVoiceProfile(response.voice_profile_id);

            if (response.requires_confirmation || response.intent_category == "clarification_required")
            {
                CurrentStage = "Awaiting clarification";
                pendingClarificationId = response.clarification_id;
                pendingCandidateIds = response.candidate_idea_ids ?? Array.Empty<string>();
                pendingAction = response.proposed_action;
                if (!followUp) pendingOriginalTranscript = transcript;
                clarificationExpiresAt = Time.unscaledTime + clarificationLifetimeSeconds;
                ShowClarification(response);
                yield break;
            }

            if (response.proposed_action == "cancel")
            {
                CurrentStage = "Cancelled";
                ClearClarification();
                ShowMessage("Request Cancelled", response.spoken_response, L("common.close"));
                ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.PlayPrerecorded("action_cancelled",activeSessionId);
                CompleteAutomaticTurn(true);
                yield break;
            }

            string ideaTranscript = followUp && !string.IsNullOrEmpty(pendingOriginalTranscript)
                ? pendingOriginalTranscript : transcript;
            ClearClarification();
            if (IdeaActions.Contains(response.proposed_action))
            {
                CurrentStage = "Processing idea";
                activeOperation = StartCoroutine(ProcessIdea(response, ideaTranscript,
                    NewRequestId("idea"), generation));
                yield break;
            }
            ShowSemanticAction(response);
            CurrentStage = "Semantic action ready";
        }

        private IEnumerator ProcessIdea(ConversationProcessResponse route, string rawTranscript,
            string requestId, int generation)
        {
            BrainstormSession session = Store.ActiveSession;
            string[] targets = route.target_idea_ids ?? Array.Empty<string>();
            var request = new IdeaProcessRequest
            {
                request_id = requestId,
                session_id = activeSessionId,
                action = route.proposed_action,
                raw_transcript = rawTranscript,
                cleaned_transcript = null,
                transcript_source = "raw",
                idea_id = route.proposed_action == "expand_idea"
                    && targets.Length == 1 ? targets[0] : null,
                related_idea_ids = route.proposed_action == "combine_ideas" ? targets : Array.Empty<string>(),
                existing_ideas = BuildIdeaContexts(session)
            };
            AIBackendResult<IdeaProcessResponse> result = null;
            LastRequestId = requestId;
            LastResponseValidation = "Awaiting response";
            yield return client.ProcessIdea(request, value => result = value);
            activeOperation = null;
            if (!IsCurrent(generation, activeSessionId)) yield break;
            if (result == null || !result.Success)
            {
                CurrentStage = "Idea request failed";
                ShowError(L("ai.idea_failed"), result != null ? result.Error : "Idea processing returned no result.", null);
                SpeakFallbackIfAvailable(result?.FallbackSpokenResponse,"idea_error_fallback");
                yield break;
            }
            IdeaProcessResponse response = result.Response;
            string validation = ValidateIdeaResponse(response, requestId, activeSessionId, session);
            LastResponseValidation = string.IsNullOrEmpty(validation) ? "Valid" : validation;
            if (!string.IsNullOrEmpty(validation))
            {
                CurrentStage = "Idea validation failed";
                ShowError(L("ai.invalid_response"), validation, null);
                yield break;
            }
            var record = new BrainstormIdeaRecord
            {
                IdeaId = response.idea_id,
                Title = response.title,
                Summary = response.summary,
                Category = response.category,
                SourceTranscript = rawTranscript,
                RelatedIdeaIds = new List<string>(response.related_idea_ids ?? Array.Empty<string>()),
                CreatedUtc = DateTime.UtcNow.ToString("O")
            };
            if (!Store.TryAddIdea(activeSessionId, record))
            {
                ShowError(L("ai.idea_save_failed"), "The idea was returned but could not be saved to the active session.", null);
                yield break;
            }
            Store.AppendConversation(activeSessionId, "orby", response.spoken_response,
                response.source=="fallback"?"idea_fallback_result":"idea_result", requestId);
            LastSuccessfulStage = "Idea saved";
            CurrentStage = "Idea ready";
            ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.ReportEvent("idea_saved_ack_suppressed",false,false,
                ThinkingSpace.Companion.OrbyVoicePriority.Acknowledgement,activeSessionId);
            bool spawned = bubbleManager != null && bubbleManager.ShowSavedIdea(record.IdeaId);
            LastSuccessfulStage = spawned ? "Idea saved and bubble spawned" : "Idea saved; bubble spawn failed";
            if (!spawned)
                ShowError(L("ai.visual_unavailable"),
                    "The idea was saved, but its bubble could not be displayed. Reopening the session will retry restoration.", null);
            string ideaSpeech = BuildIdeaSpeechText(record);
            string voiceRequest = StartIdeaSummaryPlayback(record, success =>
            {
                CurrentStage = success ? "Idle" : "Idea saved; voice unavailable";
                if (ThinkingSpace.Companion.OrbyVoiceDirector.Instance == null) SetOrby(OrbyConversationState.Idle);
                CompleteAutomaticSpeech(success, ideaSpeech);
            });
            if (string.IsNullOrEmpty(voiceRequest)) CompleteAutomaticSpeech(false, ideaSpeech);
        }

        private string StartIdeaSummaryPlayback(BrainstormIdeaRecord idea, Action<bool> completed = null)
        {
            if(idea==null||string.IsNullOrEmpty(activeSessionId))return null;
            string text=BuildIdeaSpeechText(idea);
            return ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.SpeakDynamicTracked(
                text.Trim(),activeSessionId,ThinkingSpace.Companion.OrbyVoicePriority.AIResponse,
                completed,"idea_summary","happy",resolvedResponseLanguage,resolvedVoiceProfileId);
        }
        private void StopIdeaSummaryPlayback(string requestId)
            => ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.CancelSpeech(requestId);
        public static string BuildIdeaSpeechText(BrainstormIdeaRecord idea)=>idea==null?string.Empty:((idea.Title??string.Empty).Trim()+". "+(idea.Summary??string.Empty).Trim()).Trim();
        public static bool ShouldSpeakSemanticAction(string action)
            => action == "converse" || action == "brainstorm_suggestions"
                || action == "conversational_challenge";

        private void ShowClarification(ConversationProcessResponse response)
        {
            string question = response.spoken_response ?? response.clarification_question;
            if (automaticConversationMode)
            {
                awaitingDismissal = false;
                responsePanel.Hide();
                string request = ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.SpeakDynamicTracked(
                    question, activeSessionId, ThinkingSpace.Companion.OrbyVoicePriority.AIResponse,
                    success => CompleteAutomaticSpeech(success, question), "clarification_question",
                    response.suggested_emotion,resolvedResponseLanguage,resolvedVoiceProfileId);
                if (string.IsNullOrEmpty(request)) CompleteAutomaticSpeech(false, question);
                return;
            }
            awaitingDismissal = true;
            SetOrby(OrbyConversationState.Reviewing);
            responsePanel.Show(L("ai.clarification"),
                response.clarification_question ?? response.spoken_response,
                L("ai.record_answer"), DismissForClarification, L("common.cancel"), CancelClarification);
            ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.SpeakDynamic(question,activeSessionId,
                ThinkingSpace.Companion.OrbyVoicePriority.AIResponse,null,"clarification_question",
                response.suggested_emotion,resolvedResponseLanguage,resolvedVoiceProfileId);
        }

        private void DismissForClarification()
        {
            awaitingDismissal = false;
            responsePanel.Hide();
            SetOrby(OrbyConversationState.Idle);
        }

        private void CancelClarification()
        {
            ClearClarification();
            DismissResponse();
        }

        private void ShowSemanticAction(ConversationProcessResponse response)
        {
            string parameters = response.parameters == null ? string.Empty
                : (!string.IsNullOrEmpty(response.parameters.scale) ? "\nScale: " + response.parameters.scale : string.Empty)
                + (!string.IsNullOrEmpty(response.parameters.direction) ? "\nDirection: " + response.parameters.direction : string.Empty);
            ThinkingSpace.Companion.OrbyVoiceDirector director=ThinkingSpace.Companion.OrbyVoiceDirector.Instance;
            if (automaticConversationMode)
            {
                responsePanel.Hide(); awaitingDismissal = false;
                if (ShouldSpeakSemanticAction(response.proposed_action))
                {
                    string request = director?.SpeakDynamicTracked(response.spoken_response,
                        activeSessionId,ThinkingSpace.Companion.OrbyVoicePriority.AIResponse,
                        success => CompleteAutomaticSpeech(success, response.spoken_response),"conversation_response",
                        response.suggested_emotion,resolvedResponseLanguage,resolvedVoiceProfileId);
                    if (string.IsNullOrEmpty(request)) CompleteAutomaticSpeech(false, response.spoken_response);
                }
                else
                {
                    director?.ReportEvent("spatial_confirmation_suppressed_unexecuted",false,false,
                        ThinkingSpace.Companion.OrbyVoicePriority.Acknowledgement,activeSessionId);
                    CompleteAutomaticTurn(true);
                }
                return;
            }
            ShowMessage(L("ai.response"), response.spoken_response + parameters, L("common.close"));
            if(ShouldSpeakSemanticAction(response.proposed_action))director?.SpeakDynamic(response.spoken_response,
                activeSessionId,ThinkingSpace.Companion.OrbyVoicePriority.AIResponse,null,"conversation_response",
                response.suggested_emotion,resolvedResponseLanguage,resolvedVoiceProfileId);
            else director?.ReportEvent("spatial_confirmation_suppressed_unexecuted",false,false,
                ThinkingSpace.Companion.OrbyVoicePriority.Acknowledgement,activeSessionId);
        }

        private void ShowMessage(string title, string content, string closeLabel)
        {
            awaitingDismissal = true;
            SetOrby(OrbyConversationState.Reviewing);
            responsePanel.Show(title, content, closeLabel, DismissResponse, null, null);
        }

        private void ShowError(string title, string message, Action retry)
        {
            ResolveDependencies();
            if (orby != null) orby.SetError(message);
            if (automaticConversationMode)
            {
                LastAutomaticError = string.IsNullOrWhiteSpace(message)
                    ? "Orby could not complete that request." : message;
                awaitingDismissal = false;
                responsePanel.Hide();
                ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.PlayPrerecorded(
                    title.IndexOf("network",StringComparison.OrdinalIgnoreCase)>=0
                        ?"error_network":"error_general",activeSessionId);
                CompleteAutomaticTurn(false);
                return;
            }
            awaitingDismissal = true;
            responsePanel.Show(title, message + DevelopmentDiagnosticSuffix(), retry != null ? L("common.retry") : L("common.close"),
                retry != null ? retry : DismissResponse, retry != null ? L("common.close") : null,
                retry != null ? DismissResponse : null);
            ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.PlayPrerecorded(
                title.IndexOf("network",StringComparison.OrdinalIgnoreCase)>=0?"error_network":"error_general",activeSessionId);
        }

        private void CompleteAutomaticTurn(bool success)
        {
            if (!automaticConversationMode) return;
            AutomaticTurnCompleted?.Invoke(success);
        }

        private void CompleteAutomaticSpeech(bool playbackSucceeded, string fallbackText)
        {
            if (!automaticConversationMode) return;
            if (!playbackSucceeded)
            {
                CurrentStage = "Response completed; voice unavailable";
                responsePanel.Show(L("ai.voice_unavailable"),
                    "Orby's response is available as text:\n\n" + (fallbackText ?? string.Empty),
                    L("common.close"), responsePanel.Hide, null, null);
                awaitingDismissal = false;
            }
            CompleteAutomaticTurn(true);
        }

        private void DismissResponse()
        {
            awaitingDismissal = false;
            responsePanel.Hide();
            CurrentStage = "Idle";
            ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.ReportEvent("ai_response_panel_closed",false,false,
                ThinkingSpace.Companion.OrbyVoicePriority.Acknowledgement,activeSessionId);
            if(ThinkingSpace.Companion.OrbyVoiceDirector.Instance==null||!ThinkingSpace.Companion.OrbyVoiceDirector.Instance.IsSpeaking)
                SetOrby(OrbyConversationState.Idle);
        }

        private string DevelopmentDiagnosticSuffix()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return "\n\nDiagnostics"
                + "\nStage: " + CurrentStage
                + "\nEndpoint: " + (client != null ? client.LastEndpointPath : "Unavailable")
                + "\nHTTP: " + (client != null ? client.LastHttpStatus.ToString() : "0")
                + "\nRequest: " + LastRequestId
                + "\nSession: " + (string.IsNullOrEmpty(activeSessionId) ? "None" : activeSessionId)
                + "\nValidation: " + LastResponseValidation
                + "\nValidation field: " + (client != null ? client.LastValidationFieldPath : "Unavailable")
                + "\nValidation category: " + (client != null ? client.LastValidationCategory : "Unavailable")
                + "\nFailure stage: " + (client != null ? client.LastFailureStage : "Unavailable")
                + "\nFailure field/category: " + (client != null
                    ? client.LastFailureFieldPath + " / " + client.LastFailureCategory : "Unavailable")
                + "\nCompletion/incomplete: " + (client != null
                    ? client.LastCompletionStatus + " / " + client.LastIncompleteReason : "Unavailable")
                + "\nOutput/reasoning tokens: " + (client != null
                    ? client.LastOutputTokenCount + " / " + client.LastReasoningTokenCount : "Unavailable")
                + "\nIntent: " + LastIntentCategory
                + "\nAction: " + LastProposedAction
                + "\nClarification: " + LastClarificationState
                + "\nCancelled: " + (client != null && client.LastRequestWasCancelled ? "Yes" : "No")
                + "\nTimed out: " + (client != null && client.LastRequestTimedOut ? "Yes" : "No")
                + "\nLast success: " + LastSuccessfulStage;
#else
            return string.Empty;
#endif
        }

        private void CancelAndClear()
        {
            operationGeneration++;
            if (activeOperation != null) StopCoroutine(activeOperation);
            activeOperation = null;
            client?.Cancel();
            awaitingDismissal = false;
            automaticConversationMode = false;
            ClearClarification();
        }

        private void ClearClarification()
        {
            pendingClarificationId = pendingOriginalTranscript = pendingAction = null;
            pendingCandidateIds = Array.Empty<string>();
            clarificationExpiresAt = 0f;
            LastClarificationState = "None";
        }

        private void HandleIdeaDeleted(string ideaId)
        {
            if (string.IsNullOrEmpty(ideaId) || pendingCandidateIds == null) return;
            if (Array.IndexOf(pendingCandidateIds, ideaId) < 0) return;
            ClearClarification();
            awaitingDismissal = false;
            responsePanel?.Hide();
            CurrentStage = "Clarification cancelled because a referenced idea was deleted";
            SetOrby(OrbyConversationState.Idle);
        }

        private bool IsCurrent(int generation, string sessionId)
        { return generation == operationGeneration && sessionActive && sessionId == activeSessionId
            && Store?.ActiveSession != null && Store.ActiveSession.SessionId == sessionId; }

        private BrainstormSessionStore Store => menuController != null ? menuController.SessionStore : null;
        private void ResolveDependencies()
        {
            if (menuController == null) menuController = FindFirstObjectByType<BrainstormMenuController>();
            if (orby == null) orby = FindFirstObjectByType<OrbyConversationStateController>();
        }
        private void SetOrby(OrbyConversationState state) { ResolveDependencies(); orby?.SetState(state); }

        public static AIIdeaContext[] BuildIdeaContexts(BrainstormSession session)
        {
            if (session == null) return Array.Empty<AIIdeaContext>();
            var ordered = new List<BrainstormIdeaRecord>(session.Ideas);
            ordered.Sort(CompareIdeasByCreation);
            int first = Math.Max(0, ordered.Count - 32);
            var values = new AIIdeaContext[ordered.Count - first];
            for (int i = 0; i < values.Length; i++)
            {
                BrainstormIdeaRecord idea = ordered[first + i];
                string context = i == values.Length - 1 ? "Most recently created idea. " : string.Empty;
                if (idea != null && idea.RelatedIdeaIds != null && idea.RelatedIdeaIds.Count >= 2)
                    context += "Combined idea derived from multiple ideas. ";
                values[i] = new AIIdeaContext
                { idea_id = idea != null ? idea.IdeaId : string.Empty,
                  title = idea != null ? idea.Title : string.Empty,
                  summary = context + (idea != null ? idea.Summary : string.Empty) };
            }
            return values;
        }

        private static int CompareIdeasByCreation(BrainstormIdeaRecord left, BrainstormIdeaRecord right)
        {
            DateTimeOffset leftTime = ParseIdeaCreation(left != null ? left.CreatedUtc : null);
            DateTimeOffset rightTime = ParseIdeaCreation(right != null ? right.CreatedUtc : null);
            int time = leftTime.CompareTo(rightTime);
            if (time != 0) return time;
            return string.Compare(left != null ? left.IdeaId : string.Empty,
                right != null ? right.IdeaId : string.Empty, StringComparison.Ordinal);
        }

        private static DateTimeOffset ParseIdeaCreation(string value)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;
        }

        public static ConversationContextPayload BuildConversationContext(BrainstormSession session,
            string excludeRequestId = null)
            => BuildConversationContext(session, BuildIdeaContexts(session), excludeRequestId);

        public static ConversationContextPayload BuildConversationContext(BrainstormSession session,
            AIIdeaContext[] availableIdeas, string excludeRequestId = null, string preferredName = null)
        {
            var context = new ConversationContextPayload { current_mode = "conversational",
                preferred_name = SanitizePreferredName(preferredName) };
            if (session == null) return context;
            var availableIds = new HashSet<string>();
            AIIdeaContext[] available = availableIdeas ?? Array.Empty<AIIdeaContext>();
            for (int i = 0; i < available.Length; i++) availableIds.Add(available[i].idea_id);
            for (int i = available.Length - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(available[i].idea_id)
                    && IdentifierPattern.IsMatch(available[i].idea_id))
                { context.most_recent_idea_id = available[i].idea_id; break; }
            int count = Math.Min(12, session.Conversation.Count);
            var turns = new List<ConversationTurnPayload>(count);
            for (int i = session.Conversation.Count - count; i < session.Conversation.Count; i++)
            {
                ConversationRecord record = session.Conversation[i];
                if (record == null || string.IsNullOrWhiteSpace(record.Text)) continue;
                if (!string.IsNullOrEmpty(excludeRequestId)
                    && record.RelatedRequestId == excludeRequestId) continue;
                string role = string.Equals(record.Speaker, "orby", StringComparison.OrdinalIgnoreCase)
                    ? "orby" : string.Equals(record.Speaker, "user", StringComparison.OrdinalIgnoreCase)
                        ? "user" : null;
                if (role == null) continue;
                var related = new List<string>();
                if (record.RelatedIdeaIds != null)
                    for (int j = 0; j < record.RelatedIdeaIds.Count; j++)
                        if (availableIds.Contains(record.RelatedIdeaIds[j]))
                            related.Add(record.RelatedIdeaIds[j]);
                string boundedText = record.Text.Length <= 2000 ? record.Text : record.Text.Substring(0, 2000);
                turns.Add(new ConversationTurnPayload { role = role, text = boundedText,
                    related_idea_ids = related.ToArray() });
            }
            context.recent_turns = turns.ToArray();
            return context;
        }

        public static void ApplyLanguagePreferences(ConversationContextPayload context,
            ThinkingSpacePreferencesData preferences)
        {
            if (context == null || preferences == null) return;
            string mode = (preferences.ConversationLanguageMode ?? string.Empty).Trim().ToLowerInvariant();
            string language = ThinkingSpacePreferencesStore.NormalizeConversationLanguage(
                preferences.ConversationLanguageCode);
            string profile = ThinkingSpacePreferencesStore.NormalizeVoiceProfile(preferences.VoiceProfileId);

            // No reliable STT language metadata exists in the current final-WAV transcription path.
            // Keep auto/automatic requests wire-compatible with the older backend by omitting every
            // language field. Explicit mode is only sent after the multilingual backend is deployed.
            if (mode != "explicit" || string.IsNullOrEmpty(language)) return;
            context.response_language_mode = "explicit";
            context.response_language = language;
            context.voice_profile_id = string.IsNullOrEmpty(profile) ? "orby-auto" : profile;
        }

        public static string NormalizeResolvedLanguage(string value)
            => ThinkingSpacePreferencesStore.NormalizeConversationLanguage(value);

        public static string NormalizeResolvedVoiceProfile(string value)
            => ThinkingSpacePreferencesStore.NormalizeVoiceProfile(value);

        private static string SanitizePreferredName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 32) return null;
            for (int i = 0; i < name.Length; i++)
                if (!(char.IsLetterOrDigit(name[i]) || name[i] == ' ' || name[i] == '-'
                    || name[i] == '\'')) return null;
            return name;
        }

        public static string ValidateConversationResponse(ConversationProcessResponse response,
            string requestId, string sessionId, BrainstormSession session,
            string expectedClarificationId = null)
        {
            if (response == null || response.contract_version != "1.0") return "Unsupported conversation response contract.";
            if (response.request_id != requestId || response.session_id != sessionId) return "Conversation response identity mismatch.";
            if (response.status != "completed" || !string.IsNullOrEmpty(response.error)) return response.error ?? "Conversation request failed.";
            if (!AllowedCategories.Contains(response.intent_category)
                || !AllowedActions.Contains(response.proposed_action)) return "Backend returned an unsupported semantic result.";
            if (!AllowedEmotions.Contains(response.suggested_emotion)) return "Backend returned an unsupported companion emotion.";
            if (!string.IsNullOrEmpty(response.clarification_id)
                && !IdentifierPattern.IsMatch(response.clarification_id)) return "Clarification identifier is invalid.";
            if ((response.intent_category == "general_conversation" && response.proposed_action != "converse")
                || (response.intent_category == "brainstorming_suggestions"
                    && response.proposed_action != "brainstorm_suggestions")
                || (response.intent_category == "clear_new_idea" && response.proposed_action != "create_idea")
                || (response.intent_category == "idea_modification"
                    && response.proposed_action != "expand_idea"
                    && response.proposed_action != "combine_ideas")
                || ((response.intent_category == "conversational_idea_challenge"
                    || response.intent_category == "contextual_follow_up")
                    && response.proposed_action != "conversational_challenge"))
                return "Conversation action does not match its category.";
            var known = new HashSet<string>();
            if (session != null) for (int i = 0; i < session.Ideas.Count; i++) known.Add(session.Ideas[i].IdeaId);
            string[] targets = response.target_idea_ids ?? Array.Empty<string>();
            var uniqueTargets = new HashSet<string>();
            for (int i = 0; i < targets.Length; i++)
                if (!uniqueTargets.Add(targets[i]) || !known.Contains(targets[i]))
                    return "Backend referenced an invalid or duplicate idea outside the active session.";
            string[] candidates = response.candidate_idea_ids ?? Array.Empty<string>();
            var uniqueCandidates = new HashSet<string>();
            for (int i = 0; i < candidates.Length; i++)
                if (!uniqueCandidates.Add(candidates[i]) || !known.Contains(candidates[i]))
                    return "Clarification referenced an invalid or duplicate idea outside the active session.";
            if (response.requires_confirmation && (string.IsNullOrEmpty(response.clarification_id)
                || string.IsNullOrWhiteSpace(response.clarification_question))) return "Clarification metadata is incomplete.";
            if (!string.IsNullOrEmpty(expectedClarificationId) && response.requires_confirmation
                && response.clarification_id != expectedClarificationId) return "Clarification response identity mismatch.";
            if (response.should_create_idea && (response.intent_category != "clear_new_idea"
                || response.proposed_action != "create_idea" || response.should_execute_action
                || response.requires_confirmation)) return "Conversation response has conflicting creation flags.";
            if (response.should_execute_action && response.requires_confirmation)
                return "Conversation response cannot execute while confirmation is required.";
            if (!response.requires_confirmation
                && ((response.proposed_action == "expand_idea" && targets.Length != 1)
                    || (response.proposed_action == "combine_ideas" && targets.Length < 2)))
                return "Idea modification targets are incomplete.";
            if (response.proposed_action == "create_idea" && !response.should_create_idea
                && !response.requires_confirmation)
                return "Idea creation was not authorized by the conversation response.";
            if ((response.intent_category == "general_conversation"
                || response.intent_category == "brainstorming_suggestions"
                || response.intent_category == "conversational_idea_challenge"
                || response.intent_category == "contextual_follow_up")
                && (response.should_create_idea || response.should_execute_action))
                return "Conversational response contains an unsafe action flag.";
            if (response.parameters == null) return "Conversation parameters are missing.";
            bool hasLanguage = !string.IsNullOrWhiteSpace(response.response_language);
            bool hasProfile = !string.IsNullOrWhiteSpace(response.voice_profile_id);
            if (hasLanguage != hasProfile) return "Conversation language metadata is incomplete.";
            if (hasLanguage)
            {
                string language = NormalizeResolvedLanguage(response.response_language);
                string profile = NormalizeResolvedVoiceProfile(response.voice_profile_id);
                if (language == null || profile == null) return "Conversation language metadata is unsupported.";
                if (!IsVoiceProfileCompatible(language, profile))
                    return "Conversation language and voice profile do not match.";
            }
            if ((!string.IsNullOrEmpty(response.parameters.scale)
                    && response.parameters.scale != "smaller" && response.parameters.scale != "larger"
                    && response.parameters.scale != "small" && response.parameters.scale != "medium"
                    && response.parameters.scale != "large" && response.parameters.scale != "reset")
                || (!string.IsNullOrEmpty(response.parameters.direction)
                    && response.parameters.direction != "left" && response.parameters.direction != "right"
                    && response.parameters.direction != "up" && response.parameters.direction != "down"
                    && response.parameters.direction != "closer" && response.parameters.direction != "farther"
                    && response.parameters.direction != "reset_layout")
                || (!string.IsNullOrEmpty(response.parameters.settings_section)
                    && response.parameters.settings_section != "recording_mode"
                    && response.parameters.settings_section != "silence_duration"
                    && response.parameters.settings_section != "recording_hand"
                    && response.parameters.settings_section != "subtitles"))
                return "Conversation parameters contain an unsupported semantic value.";
            if (response.subtitle_text != response.spoken_response)
                return "Voice and subtitle text do not match.";
            return string.Empty;
        }

        public static bool IsVoiceProfileCompatible(string language,string profile)
        {
            if (profile == "orby-auto") return true;
            return (language == "en" && profile == "orby-en")
                || (language == "es" && profile == "orby-es")
                || (language == "fr" && profile == "orby-fr")
                || (language == "zh-CN" && profile == "orby-zh")
                || (language == "hi-IN" && profile == "orby-hi");
        }

        private static string ValidateIdeaResponse(IdeaProcessResponse response, string requestId,
            string sessionId, BrainstormSession session)
        {
            if (response == null || response.contract_version != "1.0") return "Unsupported idea response contract.";
            if (response.request_id != requestId || response.session_id != sessionId) return "Idea response identity mismatch.";
            if (response.status != "completed" || !string.IsNullOrEmpty(response.error)) return response.error ?? "Idea request failed.";
            if (string.IsNullOrWhiteSpace(response.idea_id) || string.IsNullOrWhiteSpace(response.title)
                || string.IsNullOrWhiteSpace(response.summary) || string.IsNullOrWhiteSpace(response.category)) return "Idea response is incomplete.";
            if (session != null && session.Ideas.Exists(item => item.IdeaId == response.idea_id)) return "Idea identifier is duplicated.";
            var known = new HashSet<string>();
            if (session != null) for (int i = 0; i < session.Ideas.Count; i++) known.Add(session.Ideas[i].IdeaId);
            string[] related = response.related_idea_ids ?? Array.Empty<string>();
            for (int i = 0; i < related.Length; i++) if (!known.Contains(related[i])) return "Idea response contains an unknown relationship.";
            return string.Empty;
        }

        private static string NewRequestId(string prefix)
        { return prefix + "-" + Guid.NewGuid().ToString("N"); }
        private static string L(string key,params object[] arguments)
            =>ThinkingSpaceLocalizationController.Get(key,arguments);
        private void SpeakFallbackIfAvailable(string text,string eventName)
        {
            if(string.IsNullOrWhiteSpace(text)||string.IsNullOrEmpty(activeSessionId))return;
            OrbyVoiceDirector.Instance?.SpeakDynamic(text,activeSessionId,
                OrbyVoicePriority.Acknowledgement,null,eventName,"concerned",
                resolvedResponseLanguage,resolvedVoiceProfileId);
        }
        private void OnDisable() => CancelAndClear();
    }
}
