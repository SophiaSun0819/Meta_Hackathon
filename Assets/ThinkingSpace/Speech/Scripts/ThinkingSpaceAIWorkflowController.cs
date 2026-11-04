using System;
using System.Collections;
using System.Collections.Generic;
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
        private string activeSessionId, pendingClarificationId, pendingOriginalTranscript, pendingAction;
        private string[] pendingCandidateIds = Array.Empty<string>();
        private float clarificationExpiresAt;

        private static readonly HashSet<string> IdeaActions = new HashSet<string>
        { "create_idea", "expand_idea", "challenge_idea", "combine_ideas" };
        private static readonly HashSet<string> AllowedActions = new HashSet<string>
        { "create_idea", "expand_idea", "challenge_idea", "combine_ideas", "show_ideas",
          "resize_bubble", "move_bubble", "hide_bubble", "converse", "clarification_required", "cancel" };

        public bool IsBlockingRecording => activeOperation != null || awaitingDismissal;
        public bool HasPendingClarification => !string.IsNullOrEmpty(pendingClarificationId);
        public string PendingClarificationId => pendingClarificationId;
        public string LastSubmissionRejection { get; private set; } = "None";
        public string CurrentStage { get; private set; } = "Idle";
        public string LastSuccessfulStage { get; private set; } = "None";
        public string LastRequestId { get; private set; } = "None";
        public string LastResponseValidation { get; private set; } = "Not evaluated";

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
            bubbleManager.Configure(() => Store, headset, HandleIdeaDeleted);
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
            activeSessionId = store.ActiveSession.SessionId;
            bubbleManager?.SetSessionActive(true, activeSessionId);
            if (HasPendingClarification && Time.unscaledTime >= clarificationExpiresAt)
            {
                ClearClarification();
                ShowError("Clarification Expired",
                    "That clarification expired. Please repeat the original request.", null);
                return true;
            }
            string transcript = rawTranscript;
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
            IntentRouteResponse response = null;
            AIBackendResult<IntentRouteResponse> result = null;
            var request = new IntentRouteRequest
            {
                request_id = requestId,
                session_id = activeSessionId,
                raw_transcript = transcript,
                cleaned_transcript = null,
                transcript_source = "raw",
                available_ideas = BuildIdeaContexts(session),
                clarification_id = followUp ? pendingClarificationId : null
            };
            yield return client.RouteIntent(request, value => result = value);
            activeOperation = null;
            if (!IsCurrent(generation, activeSessionId)) yield break;
            if (result == null || !result.Success)
            {
                CurrentStage = "Intent request failed";
                ShowError("AI Request Failed", result != null ? result.Error : "Intent routing returned no result.",
                    () => SubmitApprovedTranscript(transcript));
                yield break;
            }
            response = result.Response;
            string validation = ValidateIntentResponse(response, requestId, activeSessionId, session);
            LastResponseValidation = string.IsNullOrEmpty(validation) ? "Valid" : validation;
            if (!string.IsNullOrEmpty(validation))
            {
                CurrentStage = "Intent validation failed";
                ShowError("Invalid AI Response", validation, null);
                yield break;
            }
            Store.AppendConversation(activeSessionId, "orby", response.spoken_response,
                "intent_response", requestId);
            LastSuccessfulStage = "Intent response validated";

            if (response.intent == "clarification_required")
            {
                CurrentStage = "Awaiting clarification";
                pendingClarificationId = response.clarification_id;
                pendingCandidateIds = response.candidate_idea_ids ?? Array.Empty<string>();
                pendingAction = response.pending_action;
                if (!followUp) pendingOriginalTranscript = transcript;
                clarificationExpiresAt = Time.unscaledTime + clarificationLifetimeSeconds;
                ShowClarification(response);
                yield break;
            }

            if (response.action == "cancel")
            {
                CurrentStage = "Cancelled";
                ClearClarification();
                ShowMessage("Request Cancelled", response.spoken_response, "Close");
                yield break;
            }

            string ideaTranscript = followUp && !string.IsNullOrEmpty(pendingOriginalTranscript)
                ? pendingOriginalTranscript : transcript;
            ClearClarification();
            if (IdeaActions.Contains(response.action))
            {
                CurrentStage = "Processing idea";
                activeOperation = StartCoroutine(ProcessIdea(response, ideaTranscript,
                    NewRequestId("idea"), generation));
                yield break;
            }
            ShowSemanticAction(response);
            CurrentStage = "Semantic action ready";
        }

        private IEnumerator ProcessIdea(IntentRouteResponse route, string rawTranscript,
            string requestId, int generation)
        {
            BrainstormSession session = Store.ActiveSession;
            string[] targets = route.target_idea_ids ?? Array.Empty<string>();
            var request = new IdeaProcessRequest
            {
                request_id = requestId,
                session_id = activeSessionId,
                action = route.action,
                raw_transcript = rawTranscript,
                cleaned_transcript = null,
                transcript_source = "raw",
                idea_id = (route.action == "expand_idea" || route.action == "challenge_idea")
                    && targets.Length == 1 ? targets[0] : null,
                related_idea_ids = route.action == "combine_ideas" ? targets : Array.Empty<string>(),
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
                ShowError("Idea Processing Failed", result != null ? result.Error : "Idea processing returned no result.", null);
                yield break;
            }
            IdeaProcessResponse response = result.Response;
            string validation = ValidateIdeaResponse(response, requestId, activeSessionId, session);
            LastResponseValidation = string.IsNullOrEmpty(validation) ? "Valid" : validation;
            if (!string.IsNullOrEmpty(validation))
            {
                CurrentStage = "Idea validation failed";
                ShowError("Invalid Idea Response", validation, null);
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
                ShowError("Idea Save Failed", "The idea was returned but could not be saved to the active session.", null);
                yield break;
            }
            Store.AppendConversation(activeSessionId, "orby", response.spoken_response,
                "idea_result", requestId);
            LastSuccessfulStage = "Idea saved";
            CurrentStage = "Idea ready";
            string content = response.spoken_response + "\n\nTitle\n" + response.title
                + "\n\nSummary\n" + response.summary + "\n\nCategory\n" + response.category;
            ShowIdeaResult(record.IdeaId, content);
        }

        private void ShowIdeaResult(string ideaId, string content)
        {
            awaitingDismissal = true;
            SetOrby(OrbyConversationState.Reviewing);
            responsePanel.Show("Idea Ready", content, "Close", () =>
            {
                bubbleManager?.ShowSavedIdea(ideaId);
                DismissResponse();
            }, null, null);
        }

        private void ShowClarification(IntentRouteResponse response)
        {
            awaitingDismissal = true;
            SetOrby(OrbyConversationState.Reviewing);
            responsePanel.Show("Orby Needs Clarification",
                response.clarification_question ?? response.spoken_response,
                "Record Answer", DismissForClarification, "Cancel", CancelClarification);
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

        private void ShowSemanticAction(IntentRouteResponse response)
        {
            string parameters = response.parameters == null ? string.Empty
                : (!string.IsNullOrEmpty(response.parameters.scale) ? "\nScale: " + response.parameters.scale : string.Empty)
                + (!string.IsNullOrEmpty(response.parameters.direction) ? "\nDirection: " + response.parameters.direction : string.Empty);
            ShowMessage("AI Response", response.spoken_response + parameters, "Close");
        }

        private void ShowMessage(string title, string content, string closeLabel)
        {
            awaitingDismissal = true;
            SetOrby(OrbyConversationState.Reviewing);
            responsePanel.Show(title, content, closeLabel, DismissResponse, null, null);
        }

        private void ShowError(string title, string message, Action retry)
        {
            awaitingDismissal = true;
            ResolveDependencies();
            if (orby != null) orby.SetError(message);
            responsePanel.Show(title, message + DevelopmentDiagnosticSuffix(), retry != null ? "Retry" : "Close",
                retry != null ? retry : DismissResponse, retry != null ? "Close" : null,
                retry != null ? DismissResponse : null);
        }

        private void DismissResponse()
        {
            awaitingDismissal = false;
            responsePanel.Hide();
            CurrentStage = "Idle";
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
            ClearClarification();
        }

        private void ClearClarification()
        {
            pendingClarificationId = pendingOriginalTranscript = pendingAction = null;
            pendingCandidateIds = Array.Empty<string>();
            clarificationExpiresAt = 0f;
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

        private static AIIdeaContext[] BuildIdeaContexts(BrainstormSession session)
        {
            if (session == null) return Array.Empty<AIIdeaContext>();
            var values = new AIIdeaContext[session.Ideas.Count];
            for (int i = 0; i < values.Length; i++) values[i] = new AIIdeaContext
            { idea_id = session.Ideas[i].IdeaId, title = session.Ideas[i].Title, summary = session.Ideas[i].Summary };
            return values;
        }

        private static string ValidateIntentResponse(IntentRouteResponse response, string requestId,
            string sessionId, BrainstormSession session)
        {
            if (response == null || response.contract_version != "1.0") return "Unsupported intent response contract.";
            if (response.request_id != requestId || response.session_id != sessionId) return "Intent response identity mismatch.";
            if (response.status != "completed" || !string.IsNullOrEmpty(response.error)) return response.error ?? "Intent request failed.";
            if (!AllowedActions.Contains(response.action) || !AllowedActions.Contains(response.intent)) return "Backend returned an unsupported action.";
            var known = new HashSet<string>();
            if (session != null) for (int i = 0; i < session.Ideas.Count; i++) known.Add(session.Ideas[i].IdeaId);
            string[] targets = response.target_idea_ids ?? Array.Empty<string>();
            for (int i = 0; i < targets.Length; i++) if (!known.Contains(targets[i])) return "Backend referenced an idea outside the active session.";
            string[] candidates = response.candidate_idea_ids ?? Array.Empty<string>();
            for (int i = 0; i < candidates.Length; i++) if (!known.Contains(candidates[i])) return "Clarification referenced an idea outside the active session.";
            if (response.intent == "clarification_required" && string.IsNullOrEmpty(response.clarification_id)) return "Clarification identifier is missing.";
            return string.Empty;
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
        private void OnDisable() => CancelAndClear();
    }
}
