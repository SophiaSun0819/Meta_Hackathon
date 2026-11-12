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
    { public string scale, direction, settings_section; }

    [Serializable] public sealed class ConversationTurnPayload
    { public string role, text; public string[] related_idea_ids = Array.Empty<string>(); }

    [Serializable] public sealed class ConversationContextPayload
    {
        public ConversationTurnPayload[] recent_turns = Array.Empty<ConversationTurnPayload>();
        public string most_recent_idea_id;
        public string preferred_name;
        public string current_mode = "conversational";
        public string input_language, response_language, response_language_mode, voice_profile_id;
    }

    [Serializable] public sealed class ConversationProcessRequest
    {
        public string request_id, session_id, raw_transcript, cleaned_transcript;
        public string transcript_source = "raw";
        public AIIdeaContext[] available_ideas = Array.Empty<AIIdeaContext>();
        public ConversationContextPayload context = new ConversationContextPayload();
        public string clarification_id;
    }

    [Serializable] public sealed class ConversationProcessResponse
    {
        public string contract_version, request_id, session_id, intent_category, proposed_action;
        public string[] target_idea_ids;
        public AICommandParameters parameters;
        public string spoken_response, subtitle_text, suggested_emotion;
        public bool requires_confirmation;
        public string clarification_id, clarification_question;
        public string[] candidate_idea_ids;
        public bool should_create_idea, should_execute_action;
        public string status, error;
        public string response_language, voice_profile_id;
    }

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
        public string spoken_response, status, error, source, fallback_spoken_response;
    }

    public sealed class AIBackendResult<T>
    {
        public bool Success;
        public T Response;
        public string Error;
        public long HttpStatus;
        public float DurationSeconds;
        public string FallbackSpokenResponse;
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

        [Serializable] private sealed class ConversationContextWithoutRecentWire
        {
            public ConversationTurnPayload[] recent_turns;
            public string preferred_name;
            public string current_mode;
        }

        [Serializable] private sealed class ConversationContextWithRecentWire
        {
            public ConversationTurnPayload[] recent_turns;
            public string most_recent_idea_id, preferred_name, current_mode;
        }

        [Serializable] private sealed class ConversationContextWithoutRecentLanguageWire
        {
            public ConversationTurnPayload[] recent_turns;
            public string preferred_name, current_mode, input_language, response_language,
                response_language_mode, voice_profile_id;
        }

        [Serializable] private sealed class ConversationContextWithRecentLanguageWire
        {
            public ConversationTurnPayload[] recent_turns;
            public string most_recent_idea_id, preferred_name, current_mode, input_language,
                response_language, response_language_mode, voice_profile_id;
        }

        [Serializable] private sealed class ConversationLanguageWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithoutRecentLanguageWire context;
            public string clarification_id;
        }

        [Serializable] private sealed class ConversationRecentLanguageWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithRecentLanguageWire context;
            public string clarification_id;
        }

        [Serializable] private sealed class InitialConversationWithoutRecentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithoutRecentWire context;
        }

        [Serializable] private sealed class InitialConversationWithRecentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithRecentWire context;
        }

        [Serializable] private sealed class FollowUpConversationWithoutRecentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithoutRecentWire context;
            public string clarification_id;
        }

        [Serializable] private sealed class FollowUpConversationWithRecentWireRequest
        {
            public string request_id, session_id, raw_transcript, transcript_source;
            public AIIdeaContext[] available_ideas;
            public ConversationContextWithRecentWire context;
            public string clarification_id;
        }

        public static string Serialize(ConversationProcessRequest value)
        {
            AIIdeaContext[] ideas = value.available_ideas ?? Array.Empty<AIIdeaContext>();
            ConversationContextPayload context = value.context ?? new ConversationContextPayload();
            context.recent_turns = context.recent_turns ?? Array.Empty<ConversationTurnPayload>();
            bool hasRecent = !string.IsNullOrWhiteSpace(context.most_recent_idea_id);
            bool hasLanguage = !string.IsNullOrWhiteSpace(context.input_language)
                || !string.IsNullOrWhiteSpace(context.response_language)
                || !string.IsNullOrWhiteSpace(context.response_language_mode)
                || !string.IsNullOrWhiteSpace(context.voice_profile_id);
            var withoutRecent = new ConversationContextWithoutRecentWire
            { recent_turns = context.recent_turns, preferred_name = context.preferred_name,
              current_mode = context.current_mode };
            var withRecent = new ConversationContextWithRecentWire
            { recent_turns = context.recent_turns, most_recent_idea_id = context.most_recent_idea_id,
              preferred_name = context.preferred_name, current_mode = context.current_mode };
            if (hasLanguage)
            {
                var language = new ConversationContextWithoutRecentLanguageWire
                { recent_turns=context.recent_turns, preferred_name=context.preferred_name,
                  current_mode=context.current_mode, input_language=context.input_language,
                  response_language=context.response_language,
                  response_language_mode=context.response_language_mode,
                  voice_profile_id=context.voice_profile_id };
                var recentLanguage = new ConversationContextWithRecentLanguageWire
                { recent_turns=context.recent_turns, most_recent_idea_id=context.most_recent_idea_id,
                  preferred_name=context.preferred_name, current_mode=context.current_mode,
                  input_language=context.input_language, response_language=context.response_language,
                  response_language_mode=context.response_language_mode,
                  voice_profile_id=context.voice_profile_id };
                return hasRecent
                    ? JsonUtility.ToJson(new ConversationRecentLanguageWireRequest
                    { request_id=value.request_id, session_id=value.session_id,
                      raw_transcript=value.raw_transcript, transcript_source=value.transcript_source,
                      available_ideas=ideas, context=recentLanguage,
                      clarification_id=string.IsNullOrEmpty(value.clarification_id)?null:value.clarification_id })
                    : JsonUtility.ToJson(new ConversationLanguageWireRequest
                    { request_id=value.request_id, session_id=value.session_id,
                      raw_transcript=value.raw_transcript, transcript_source=value.transcript_source,
                      available_ideas=ideas, context=language,
                      clarification_id=string.IsNullOrEmpty(value.clarification_id)?null:value.clarification_id });
            }
            if (string.IsNullOrEmpty(value.clarification_id))
                return hasRecent
                    ? JsonUtility.ToJson(new InitialConversationWithRecentWireRequest
                    { request_id = value.request_id, session_id = value.session_id,
                      raw_transcript = value.raw_transcript, transcript_source = value.transcript_source,
                      available_ideas = ideas, context = withRecent })
                    : JsonUtility.ToJson(new InitialConversationWithoutRecentWireRequest
                    { request_id = value.request_id, session_id = value.session_id,
                      raw_transcript = value.raw_transcript, transcript_source = value.transcript_source,
                      available_ideas = ideas, context = withoutRecent });
            return hasRecent
                ? JsonUtility.ToJson(new FollowUpConversationWithRecentWireRequest
                { request_id = value.request_id, session_id = value.session_id,
                  raw_transcript = value.raw_transcript, transcript_source = value.transcript_source,
                  available_ideas = ideas, context = withRecent, clarification_id = value.clarification_id })
                : JsonUtility.ToJson(new FollowUpConversationWithoutRecentWireRequest
                { request_id = value.request_id, session_id = value.session_id,
                  raw_transcript = value.raw_transcript, transcript_source = value.transcript_source,
                  available_ideas = ideas, context = withoutRecent, clarification_id = value.clarification_id });
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
        IEnumerator ProcessConversation(ConversationProcessRequest request,
            Action<AIBackendResult<ConversationProcessResponse>> completed);
        IEnumerator ProcessIdea(IdeaProcessRequest request, Action<AIBackendResult<IdeaProcessResponse>> completed);
        void Cancel();
    }

    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceAIBackendClient : MonoBehaviour, IThinkingSpaceAIBackendClient
    {
        public const string ConversationEndpointPath = "/api/conversations/process";
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
        public string LastFailureStage { get; private set; } = "None";
        public string LastFailureFieldPath { get; private set; } = "None";
        public string LastFailureCategory { get; private set; } = "None";
        public string LastCompletionStatus { get; private set; } = "None";
        public string LastIncompleteReason { get; private set; } = "None";
        public int LastOutputTokenCount { get; private set; } = -1;
        public int LastReasoningTokenCount { get; private set; } = -1;
        private static readonly Regex IdentifierPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant);
        private static readonly Regex SessionPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9 ._:-]{0,127}$", RegexOptions.CultureInvariant);

        public void Configure(SpeechTranscriptionService service) => transcriptionService = service;

        public static string ValidateConversationRequest(ConversationProcessRequest request,
            out string fieldPath, out string category)
            => ValidatePayload(request, out fieldPath, out category);

        public IEnumerator RouteIntent(IntentRouteRequest request,
            Action<AIBackendResult<IntentRouteResponse>> completed)
        {
            yield return PostJson("api/intents/route", request, completed);
        }

        public IEnumerator ProcessConversation(ConversationProcessRequest request,
            Action<AIBackendResult<ConversationProcessResponse>> completed)
        {
            yield return PostJson(ConversationEndpointPath.TrimStart('/'), request, completed);
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
            LastFailureStage = LastFailureFieldPath = LastFailureCategory = "None";
            LastCompletionStatus = LastIncompleteReason = "None";
            LastOutputTokenCount = LastReasoningTokenCount = -1;
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
            string json = payload is ConversationProcessRequest conversation
                ? AIRequestJsonSerializer.Serialize(conversation)
                : payload is IntentRouteRequest intent
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
            ReadSafeDiagnosticHeaders(request);
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
                AIBackendResult<TResponse> failure=Failure<TResponse>(message,request.responseCode,elapsed);
                failure.FallbackSpokenResponse=ExtractFallbackSpeech(request.downloadHandler!=null
                    ?request.downloadHandler.text:null);
                completed?.Invoke(failure);
            }
            request.Dispose();
        }

        private void ReadSafeDiagnosticHeaders(UnityWebRequest request)
        {
            string Header(string name)
            {
                string value = request.GetResponseHeader(name);
                return string.IsNullOrWhiteSpace(value) ? "None" : value;
            }
            LastFailureStage = Header("X-ThinkingSpace-Failure-Stage");
            LastFailureFieldPath = Header("X-ThinkingSpace-Failure-Field");
            LastFailureCategory = Header("X-ThinkingSpace-Failure-Category");
            LastCompletionStatus = Header("X-ThinkingSpace-Completion-Status");
            LastIncompleteReason = Header("X-ThinkingSpace-Incomplete-Reason");
            LastOutputTokenCount = int.TryParse(request.GetResponseHeader(
                "X-ThinkingSpace-Output-Tokens"), out int outputTokens) ? outputTokens : -1;
            LastReasoningTokenCount = int.TryParse(request.GetResponseHeader(
                "X-ThinkingSpace-Reasoning-Tokens"), out int reasoningTokens) ? reasoningTokens : -1;
            string validationField = request.GetResponseHeader("X-ThinkingSpace-Validation-Field");
            string validationCategory = request.GetResponseHeader("X-ThinkingSpace-Validation-Category");
            if (!string.IsNullOrWhiteSpace(validationField)) LastValidationFieldPath = validationField;
            if (!string.IsNullOrWhiteSpace(validationCategory)) LastValidationCategory = validationCategory;
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

        private static string ExtractFallbackSpeech(string json)
        {
            if(string.IsNullOrEmpty(json))return null;
            try{return JsonUtility.FromJson<BackendError>(json)?.fallback_spoken_response;}
            catch(Exception){return null;}
        }

        [Serializable] private sealed class BackendError { public string error, detail, fallback_spoken_response; }

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
            if (payload is ConversationProcessRequest conversation)
            {
                string common = ValidateCommon(conversation.request_id, conversation.session_id,
                    conversation.raw_transcript, out fieldPath, out category);
                if (!string.IsNullOrEmpty(common)) return common;
                if (conversation.transcript_source != "raw" && conversation.transcript_source != "cleaned")
                { fieldPath = "transcript_source"; category = "invalid_enum"; return "Transcript source is invalid."; }
                if (!string.IsNullOrEmpty(conversation.clarification_id)
                    && !IdentifierPattern.IsMatch(conversation.clarification_id))
                { fieldPath = "clarification_id"; category = "invalid_identifier"; return "Clarification identifier is invalid."; }
                AIIdeaContext[] ideas = conversation.available_ideas ?? Array.Empty<AIIdeaContext>();
                if (ideas.Length > 32)
                { fieldPath = "available_ideas"; category = "too_many_items"; return "Too many available ideas were supplied."; }
                string ideaError = ValidateIdeaContexts(ideas, "available_ideas", out fieldPath, out category);
                if (!string.IsNullOrEmpty(ideaError)) return ideaError;
                return ValidateConversationContext(conversation.context, ideas, out fieldPath, out category);
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

        private static string ValidateConversationContext(ConversationContextPayload context,
            AIIdeaContext[] ideas, out string fieldPath, out string category)
        {
            fieldPath = "None"; category = "None";
            if (context == null)
            { fieldPath = "context"; category = "missing"; return "Conversation context is missing."; }
            if (context.current_mode != "conversational")
            { fieldPath = "context.current_mode"; category = "invalid_enum"; return "Conversation mode is invalid."; }
            if (!string.IsNullOrEmpty(context.preferred_name))
            {
                string name = context.preferred_name;
                if (name.Length > 32 || name != name.Trim())
                { fieldPath = "context.preferred_name"; category = "invalid_length"; return "Preferred name is invalid."; }
                for (int i = 0; i < name.Length; i++)
                    if (!(char.IsLetterOrDigit(name[i]) || name[i] == ' ' || name[i] == '-'
                        || name[i] == '\''))
                    { fieldPath = "context.preferred_name"; category = "invalid_character"; return "Preferred name is invalid."; }
            }
            ConversationTurnPayload[] turns = context.recent_turns ?? Array.Empty<ConversationTurnPayload>();
            if (turns.Length > 12)
            { fieldPath = "context.recent_turns"; category = "too_many_items"; return "Conversation history is too long."; }
            var known = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < ideas.Length; i++)
                if (!known.Add(ideas[i].idea_id))
                { fieldPath = "available_ideas." + i + ".idea_id"; category = "duplicate_identifier"; return "Available idea identifiers must be unique."; }
            if (known.Count > 0 && string.IsNullOrEmpty(context.most_recent_idea_id))
            { fieldPath = "context.most_recent_idea_id"; category = "missing"; return "Most-recent idea is required when ideas are available."; }
            if (!string.IsNullOrEmpty(context.most_recent_idea_id)
                && (!IdentifierPattern.IsMatch(context.most_recent_idea_id)
                    || !known.Contains(context.most_recent_idea_id)))
            { fieldPath = "context.most_recent_idea_id"; category = "unknown_idea"; return "Most-recent idea is unavailable."; }
            for (int i = 0; i < turns.Length; i++)
            {
                ConversationTurnPayload turn = turns[i];
                if (turn == null || (turn.role != "user" && turn.role != "orby"))
                { fieldPath = "context.recent_turns." + i + ".role"; category = "invalid_enum"; return "Conversation role is invalid."; }
                if (string.IsNullOrWhiteSpace(turn.text) || turn.text.Length > 2000)
                { fieldPath = "context.recent_turns." + i + ".text"; category = "invalid_length"; return "Conversation text is invalid."; }
                string[] related = turn.related_idea_ids ?? Array.Empty<string>();
                if (related.Length > 8)
                { fieldPath = "context.recent_turns." + i + ".related_idea_ids"; category = "too_many_items"; return "Conversation references too many ideas."; }
                var turnIds = new System.Collections.Generic.HashSet<string>();
                for (int j = 0; j < related.Length; j++)
                {
                    if (!turnIds.Add(related[j]))
                    { fieldPath = "context.recent_turns." + i + ".related_idea_ids." + j; category = "duplicate_identifier"; return "Conversation idea references must be unique."; }
                    if (!known.Contains(related[j]))
                    { fieldPath = "context.recent_turns." + i + ".related_idea_ids." + j; category = "unknown_idea"; return "Conversation references an unavailable idea."; }
                }
            }
            return string.Empty;
        }
        private void OnDisable() => Cancel();
    }
}
