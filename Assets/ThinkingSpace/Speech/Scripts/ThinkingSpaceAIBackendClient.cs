using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace ThinkingSpace.Speech
{
    [Serializable] public sealed class AIIdeaContext
    { public string idea_id, title, summary; }

    [Serializable] public sealed class IntentRouteRequest
    {
        public string request_id, session_id, raw_transcript, cleaned_transcript, transcript_source = "raw";
        public AIIdeaContext[] available_ideas = Array.Empty<AIIdeaContext>();
        public string clarification_id;
    }

    [Serializable] public sealed class AICommandParameters
    { public string scale, direction, question; }

    [Serializable] public sealed class IntentRouteResponse
    {
        public string contract_version, request_id, session_id, intent, action;
        public string[] target_idea_ids;
        public AICommandParameters parameters;
        public string spoken_response, subtitle_text, suggested_emotion;
        public string clarification_id, clarification_question;
        public string[] candidate_idea_ids;
        public bool requires_confirmation;
        public string pending_action, status, error;
    }

    [Serializable] public sealed class IdeaProcessRequest
    {
        public string request_id, session_id, action, raw_transcript, cleaned_transcript, transcript_source = "raw";
        public string idea_id;
        public string[] related_idea_ids = Array.Empty<string>();
        public AIIdeaContext[] existing_ideas = Array.Empty<AIIdeaContext>();
    }

    [Serializable] public sealed class IdeaProcessResponse
    {
        public string contract_version, request_id, session_id, action, idea_id;
        public string title, summary, category;
        public string[] related_idea_ids;
        public string spoken_response, status, error;
    }

    public sealed class AIBackendResult<T>
    {
        public bool Success;
        public T Response;
        public string Error;
        public long HttpStatus;
        public float DurationSeconds;
    }

    public static class AIRequestJsonSerializer
    {
        [Serializable] private sealed class InitialIntentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
        }

        [Serializable] private sealed class FollowUpIntentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source, clarification_id;
            public AIIdeaContext[] available_ideas;
        }

        [Serializable] private sealed class UntargetedIdeaWireRequest
        {
            public string request_id, session_id, action, raw_transcript, transcript_source;
            public string[] related_idea_ids;
            public AIIdeaContext[] existing_ideas;
        }

        [Serializable] private sealed class TargetedIdeaWireRequest
        {
            public string request_id, session_id, action, raw_transcript, transcript_source, idea_id;
            public string[] related_idea_ids;
            public AIIdeaContext[] existing_ideas;
        }

        public static string Serialize(IntentRouteRequest value)
        {
            AIIdeaContext[] ideas = value.available_ideas ?? Array.Empty<AIIdeaContext>();
            if (string.IsNullOrEmpty(value.clarification_id))
                return JsonUtility.ToJson(new InitialIntentWireRequest
                {
                    request_id = value.request_id,
                    session_id = value.session_id,
                    raw_transcript = value.raw_transcript,
                    transcript_source = value.transcript_source,
                    available_ideas = ideas
                });
            return JsonUtility.ToJson(new FollowUpIntentWireRequest
            {
                request_id = value.request_id,
                session_id = value.session_id,
                raw_transcript = value.raw_transcript,
                transcript_source = value.transcript_source,
                available_ideas = ideas,
                clarification_id = value.clarification_id
            });
        }

        public static string Serialize(IdeaProcessRequest value)
        {
            string[] related = value.related_idea_ids ?? Array.Empty<string>();
            AIIdeaContext[] ideas = value.existing_ideas ?? Array.Empty<AIIdeaContext>();
            if (string.IsNullOrEmpty(value.idea_id))
                return JsonUtility.ToJson(new UntargetedIdeaWireRequest
                {
                    request_id = value.request_id,
                    session_id = value.session_id,
                    action = value.action,
                    raw_transcript = value.raw_transcript,
                    transcript_source = value.transcript_source,
                    related_idea_ids = related,
                    existing_ideas = ideas
                });
            return JsonUtility.ToJson(new TargetedIdeaWireRequest
            {
                request_id = value.request_id,
                session_id = value.session_id,
                action = value.action,
                raw_transcript = value.raw_transcript,
                transcript_source = value.transcript_source,
                idea_id = value.idea_id,
                related_idea_ids = related,
                existing_ideas = ideas
            });
        }
    }

    public interface IThinkingSpaceAIBackendClient
    {
        bool IsBusy { get; }
        IEnumerator RouteIntent(IntentRouteRequest request, Action<AIBackendResult<IntentRouteResponse>> completed);
        IEnumerator ProcessIdea(IdeaProcessRequest request, Action<AIBackendResult<IdeaProcessResponse>> completed);
        void Cancel();
    }

    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceAIBackendClient : MonoBehaviour, IThinkingSpaceAIBackendClient
    {
        [SerializeField, Range(5, 120)] private int requestTimeoutSeconds = 45;
        private SpeechTranscriptionService transcriptionService;
        private UnityWebRequest activeRequest;
        private int requestGeneration;
        public bool IsBusy => activeRequest != null;
        public string LastEndpointPath { get; private set; } = "None";
        public long LastHttpStatus { get; private set; }
        public string LastError { get; private set; } = "None";
        public bool LastRequestWasCancelled { get; private set; }
        public bool LastRequestTimedOut { get; private set; }
        public string LastValidationFieldPath { get; private set; } = "None";
        public string LastValidationCategory { get; private set; } = "None";
        private static readonly Regex IdentifierPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant);
        private static readonly Regex SessionPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9 ._:-]{0,127}$", RegexOptions.CultureInvariant);

        public void Configure(SpeechTranscriptionService service) => transcriptionService = service;

        public IEnumerator RouteIntent(IntentRouteRequest request,
            Action<AIBackendResult<IntentRouteResponse>> completed)
        {
            yield return PostJson("api/intents/route", request, completed);
        }

        public IEnumerator ProcessIdea(IdeaProcessRequest request,
            Action<AIBackendResult<IdeaProcessResponse>> completed)
        {
            yield return PostJson("api/ideas/process", request, completed);
        }

        private IEnumerator PostJson<TRequest, TResponse>(string endpoint, TRequest payload,
            Action<AIBackendResult<TResponse>> completed) where TResponse : class
        {
            LastEndpointPath = "/" + endpoint;
            LastHttpStatus = 0;
            LastError = "None";
            LastRequestWasCancelled = false;
            LastRequestTimedOut = false;
            LastValidationFieldPath = "None";
            LastValidationCategory = "None";
            if (IsBusy)
            {
                LastError = "Another AI request is already active.";
                completed?.Invoke(Failure<TResponse>("Another AI request is already active.", 0, 0f));
                yield break;
            }
            if (transcriptionService == null)
            {
                LastError = "AI backend configuration is unavailable.";
                completed?.Invoke(Failure<TResponse>("AI backend configuration is unavailable.", 0, 0f));
                yield break;
            }
            string baseUrl = transcriptionService.BackendBaseUrl;
            string token = transcriptionService.GetDevelopmentTokenForCurrentRun();
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            {
                LastError = "AI backend URL or device authentication is not configured.";
                completed?.Invoke(Failure<TResponse>("AI backend URL or device authentication is not configured.", 0, 0f));
                yield break;
            }

            string validationError = ValidatePayload(payload, out string fieldPath, out string category);
            if (!string.IsNullOrEmpty(validationError))
            {
                LastValidationFieldPath = fieldPath;
                LastValidationCategory = category;
                LastError = validationError;
                completed?.Invoke(Failure<TResponse>(validationError, 0, 0f));
                yield break;
            }
            string json = payload is IntentRouteRequest intent
                ? AIRequestJsonSerializer.Serialize(intent)
                : payload is IdeaProcessRequest idea
                    ? AIRequestJsonSerializer.Serialize(idea)
                    : JsonUtility.ToJson(payload);
            byte[] body = Encoding.UTF8.GetBytes(json);
            int generation = ++requestGeneration;
            float started = Time.realtimeSinceStartup;
            UnityWebRequest request = new UnityWebRequest(baseUrl + endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = requestTimeoutSeconds
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-ThinkingSpace-Token", token);
            activeRequest = request;
            yield return request.SendWebRequest();
            float elapsed = Time.realtimeSinceStartup - started;
            if (generation != requestGeneration)
            {
                request.Dispose();
                yield break;
            }
            activeRequest = null;
            LastHttpStatus = request.responseCode;
            if (request.result == UnityWebRequest.Result.Success)
            {
                TResponse response = default;
                try { response = JsonUtility.FromJson<TResponse>(request.downloadHandler.text); }
                catch (Exception) { }
                if (response == null)
                {
                    LastError = "Backend returned an invalid response.";
                    completed?.Invoke(Failure<TResponse>("Backend returned an invalid response.", request.responseCode, elapsed));
                }
                else
                    completed?.Invoke(new AIBackendResult<TResponse> { Success = true, Response = response,
                        HttpStatus = request.responseCode, DurationSeconds = elapsed });
            }
            else
            {
                string message = ExtractSafeError(request.downloadHandler != null
                    ? request.downloadHandler.text : null, request.error);
                LastRequestTimedOut = request.error != null
                    && request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
                LastError = message;
                if (request.responseCode == 422)
                {
                    LastValidationFieldPath = "Backend response did not include a field path";
                    LastValidationCategory = "remote_schema_validation";
                }
                completed?.Invoke(Failure<TResponse>(message, request.responseCode, elapsed));
            }
            request.Dispose();
        }

        public void Cancel()
        {
            requestGeneration++;
            if (activeRequest == null) return;
            LastRequestWasCancelled = true;
            LastError = "Request cancelled locally.";
            activeRequest.Abort();
            activeRequest = null;
        }

        private static AIBackendResult<T> Failure<T>(string error, long status, float duration)
        {
            return new AIBackendResult<T> { Success = false,
                Error = string.IsNullOrEmpty(error) ? "AI backend request failed." : error,
                HttpStatus = status, DurationSeconds = duration };
        }

        private static string ExtractSafeError(string json, string fallback)
        {
            if (!string.IsNullOrEmpty(json))
            {
                BackendError parsed = null;
                try { parsed = JsonUtility.FromJson<BackendError>(json); } catch (Exception) { }
                if (parsed != null)
                {
                    if (!string.IsNullOrEmpty(parsed.error)) return parsed.error;
                    if (!string.IsNullOrEmpty(parsed.detail)) return parsed.detail;
                }
            }
            return string.IsNullOrEmpty(fallback) ? "AI backend request failed." : fallback;
        }

        [Serializable] private sealed class BackendError { public string error, detail; }

        private static string ValidatePayload<TRequest>(TRequest payload, out string fieldPath,
            out string category)
        {
            fieldPath = "None";
            category = "None";
            if (payload is IntentRouteRequest intent)
            {
                string common = ValidateCommon(intent.request_id, intent.session_id,
                    intent.raw_transcript, out fieldPath, out category);
                if (!string.IsNullOrEmpty(common)) return common;
                if (!string.IsNullOrEmpty(intent.clarification_id)
                    && !IdentifierPattern.IsMatch(intent.clarification_id))
                { fieldPath = "clarification_id"; category = "invalid_identifier"; return "Clarification identifier is invalid."; }
                AIIdeaContext[] ideas = intent.available_ideas ?? Array.Empty<AIIdeaContext>();
                if (ideas.Length > 32)
                { fieldPath = "available_ideas"; category = "too_many_items"; return "Too many available ideas were supplied."; }
                return ValidateIdeaContexts(ideas, "available_ideas", out fieldPath, out category);
            }
            if (payload is IdeaProcessRequest idea)
            {
                string common = ValidateCommon(idea.request_id, idea.session_id,
                    idea.raw_transcript, out fieldPath, out category);
                if (!string.IsNullOrEmpty(common)) return common;
                if (!string.IsNullOrEmpty(idea.idea_id) && !IdentifierPattern.IsMatch(idea.idea_id))
                { fieldPath = "idea_id"; category = "invalid_identifier"; return "Target idea identifier is invalid."; }
                return ValidateIdeaContexts(idea.existing_ideas ?? Array.Empty<AIIdeaContext>(),
                    "existing_ideas", out fieldPath, out category);
            }
            return string.Empty;
        }

        private static string ValidateCommon(string requestId, string sessionId, string transcript,
            out string fieldPath, out string category)
        {
            fieldPath = "None"; category = "None";
            if (string.IsNullOrEmpty(requestId) || !IdentifierPattern.IsMatch(requestId))
            { fieldPath = "request_id"; category = "invalid_identifier"; return "Request identifier is invalid."; }
            if (string.IsNullOrEmpty(sessionId) || !SessionPattern.IsMatch(sessionId))
            { fieldPath = "session_id"; category = "invalid_identifier"; return "Session identifier is invalid."; }
            if (string.IsNullOrWhiteSpace(transcript) || transcript.Length > 8000)
            { fieldPath = "raw_transcript"; category = "invalid_length"; return "Transcript length is outside the supported range."; }
            return string.Empty;
        }

        private static string ValidateIdeaContexts(AIIdeaContext[] ideas, string prefix,
            out string fieldPath, out string category)
        {
            fieldPath = "None"; category = "None";
            for (int i = 0; i < ideas.Length; i++)
            {
                AIIdeaContext item = ideas[i];
                if (item == null || string.IsNullOrEmpty(item.idea_id) || !IdentifierPattern.IsMatch(item.idea_id))
                { fieldPath = prefix + "." + i + ".idea_id"; category = "invalid_identifier"; return "An idea identifier is invalid."; }
                if (string.IsNullOrWhiteSpace(item.title) || item.title.Length > 120)
                { fieldPath = prefix + "." + i + ".title"; category = "invalid_length"; return "An idea title is invalid."; }
                if (string.IsNullOrWhiteSpace(item.summary) || item.summary.Length > 2000)
                { fieldPath = prefix + "." + i + ".summary"; category = "invalid_length"; return "An idea summary is invalid."; }
            }
            return string.Empty;
        }
        private void OnDisable() => Cancel();
    }
}
