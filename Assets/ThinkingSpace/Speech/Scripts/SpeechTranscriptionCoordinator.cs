using System;
using System.Collections;
using System.IO;
using UnityEngine;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    public enum RecordingWorkflowState { Ready, Recording, Reviewing, Playing, Transcribing, TranscriptionFailed, TranscriptReady }

    [DisallowMultipleComponent]
    public sealed class SpeechTranscriptionCoordinator : MonoBehaviour
    {
        [Header("Existing References")]
        [SerializeField] private QuestMicrophoneRecorder microphoneRecorder;
        [SerializeField] private ThumbRecordingGestureController gestureController;
        [SerializeField] private SpeechTranscriptionService transcriptionService;
        [SerializeField] private TranscriptPanel transcriptPanel;
        [SerializeField] private RecordingReviewPanel reviewPanel;
        [SerializeField] private ThinkingSpaceAIWorkflowController aiWorkflow;
        [SerializeField] private ConversationalListeningController conversationalListening;
        [Header("Development Test")]
        [SerializeField] private AudioClip developmentTestClip;
        [Tooltip("Routes a generated 440 Hz tone through the review AudioSource instead of the retained clip.")]
        [SerializeField] private bool developmentUseOutputTestToneForPlay;
        [Tooltip("Shows an explicit WAV export button in development builds. Never available in production builds.")]
        [SerializeField] private bool enableDevelopmentWavExport;

        private CompletedMicrophoneRecording? pendingRecording;
        private byte[] pendingWav;
        private string pendingRecordingIdentifier;
        private string pendingTranscript;
        private AudioSource playbackSource;
        private Coroutine activeRequest;
        private int lastErrorVoiceGeneration = -1;
        private int requestGeneration, recordingSequence;
        private bool sessionActive;
        private bool voicePlaybackBlocked, tutorialRecordingBlocked;
        private bool automaticConversationTurn;
        private float playbackCommandTime;
        private bool playbackStartedObserved;
        private string playbackError = "None";
        private string lastExportPath = "None";
        private string lastExportValidation = "Not exported";
        private string lastTranscriptionMode = "None";
        private string lastWavValidation = "Not validated";
        private string lastRequestCompletion = "No request completed";
        private int lastStartedRequestGeneration;
        private AudioClip outputTestTone;
        private const float PlaybackStartupGrace = 0.75f;

        public RecordingWorkflowState State { get; private set; } = RecordingWorkflowState.Ready;
        public bool CanBeginRecording => sessionActive && State == RecordingWorkflowState.Ready
            && !voicePlaybackBlocked && !tutorialRecordingBlocked
            && (aiWorkflow == null || !aiWorkflow.IsBlockingRecording);
        public bool IsSessionActive => sessionActive;
        public void SetVoicePlaybackBlocked(bool value) => voicePlaybackBlocked = value;
        public void SetTutorialRecordingBlocked(bool value) => tutorialRecordingBlocked = value;
        public string DiagnosticText { get; private set; } = "WORKFLOW: Ready";

        private void Awake()
        {
            if (microphoneRecorder == null) microphoneRecorder = GetComponent<QuestMicrophoneRecorder>();
            if (gestureController == null) gestureController = GetComponent<ThumbRecordingGestureController>();
            if (transcriptionService == null) transcriptionService = GetComponent<SpeechTranscriptionService>();
            if (transcriptionService == null) transcriptionService = gameObject.AddComponent<SpeechTranscriptionService>();
            if (transcriptPanel == null) transcriptPanel = GetComponent<TranscriptPanel>();
            if (transcriptPanel == null) transcriptPanel = gameObject.AddComponent<TranscriptPanel>();
            if (reviewPanel == null) reviewPanel = GetComponent<RecordingReviewPanel>();
            if (reviewPanel == null) reviewPanel = gameObject.AddComponent<RecordingReviewPanel>();
            if (aiWorkflow == null) aiWorkflow = GetComponent<ThinkingSpaceAIWorkflowController>();
            if (aiWorkflow == null) aiWorkflow = gameObject.AddComponent<ThinkingSpaceAIWorkflowController>();
            if (conversationalListening == null) conversationalListening = GetComponent<ConversationalListeningController>();
            if (conversationalListening == null) conversationalListening = gameObject.AddComponent<ConversationalListeningController>();
            Transform headset = gestureController != null ? gestureController.CenterEyeAnchor : null;
            transcriptPanel.SetHeadset(headset);
            reviewPanel.SetHeadset(headset);
            aiWorkflow.Configure(transcriptionService, headset);
            aiWorkflow.AutomaticTurnCompleted -= HandleAutomaticTurnCompleted;
            aiWorkflow.AutomaticTurnCompleted += HandleAutomaticTurnCompleted;
            // BrainstormMenuController can restore a session before this component's Awake
            // creates the runtime AI workflow. Replay the coordinator's current gate so the
            // dynamically added workflow cannot remain incorrectly inactive.
            aiWorkflow.SetSessionActive(sessionActive);
            conversationalListening.Configure(microphoneRecorder, this);
            conversationalListening.SetSessionActive(sessionActive);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            reviewPanel.ConfigureDevelopmentExport(enableDevelopmentWavExport, ExportPendingRecordingForDevelopment);
#else
            reviewPanel.ConfigureDevelopmentExport(false, null);
#endif
            playbackSource = gameObject.AddComponent<AudioSource>();
            playbackSource.playOnAwake = false;
            playbackSource.loop = false;
            playbackSource.spatialBlend = 0f;
            playbackSource.volume = 1f;
            playbackSource.mute = false;
            playbackSource.pitch = 1f;
            playbackSource.panStereo = 0f;
            playbackSource.ignoreListenerPause = true;
            playbackSource.priority = 0;
            playbackSource.bypassEffects = true;
            playbackSource.bypassListenerEffects = true;
            playbackSource.bypassReverbZones = true;
            playbackSource.outputAudioMixerGroup = null;
        }

        private void OnEnable()
        {
            if (microphoneRecorder == null) return;
            microphoneRecorder.RecordingStarted += HandleRecordingStarted;
            microphoneRecorder.RecordingCompleted += HandleRecordingCompleted;
            microphoneRecorder.RecordingFailed += HandleRecordingFailed;
        }

        private void OnDisable()
        {
            if (microphoneRecorder != null)
            {
                microphoneRecorder.RecordingStarted -= HandleRecordingStarted;
                microphoneRecorder.RecordingCompleted -= HandleRecordingCompleted;
                microphoneRecorder.RecordingFailed -= HandleRecordingFailed;
            }
            if (aiWorkflow != null) aiWorkflow.AutomaticTurnCompleted -= HandleAutomaticTurnCompleted;
            CancelActiveRequest();
            StopPlayback();
            if (outputTestTone != null) Destroy(outputTestTone);
        }

        private void Update()
        {
            if (State == RecordingWorkflowState.Playing)
            {
                if (playbackSource != null && playbackSource.isPlaying)
                {
                    playbackStartedObserved = true;
                    reviewPanel.SetPlaybackProgress(playbackSource.time, pendingRecording.HasValue
                        ? pendingRecording.Value.Clip.length : playbackSource.clip.length);
                    UpdateDiagnostic("Playback active");
                }
                else if (playbackStartedObserved || Time.unscaledTime - playbackCommandTime >= PlaybackStartupGrace)
                {
                    playbackError = playbackStartedObserved ? "None" : "AudioSource did not enter isPlaying state";
                    State = RecordingWorkflowState.Reviewing;
                    reviewPanel.SetPlaying(false);
                    reviewPanel.SetPlaybackProgress(0f, pendingRecording.HasValue
                        ? pendingRecording.Value.Clip.length : 0f);
                    UpdateDiagnostic(playbackStartedObserved ? "Playback finished" : "Playback failed to start");
                }
            }
        }

        public void SetSessionActive(bool active)
        {
            sessionActive = active;
            aiWorkflow?.SetSessionActive(active);
            conversationalListening?.SetSessionActive(active);
            if (!active)
            {
                CancelActiveRequest(); StopPlayback(); ClearPendingRecording();
                reviewPanel.Hide(); transcriptPanel.Hide();
                State = RecordingWorkflowState.Ready;
                UpdateDiagnostic("Session ended; pending recording cleared");
            }
            else if (!pendingRecording.HasValue && State != RecordingWorkflowState.Recording)
            {
                State = RecordingWorkflowState.Ready;
                UpdateDiagnostic("Ready");
            }
        }

        private void HandleRecordingStarted()
        {
            if (!sessionActive) return;
            if (conversationalListening != null && conversationalListening.OwnsCapture)
            {
                conversationalListening.NotifyRecordingStarted();
                aiWorkflow?.NotifyListening();
                UpdateDiagnostic("Conversational microphone monitoring");
                return;
            }
            State = RecordingWorkflowState.Recording;
            aiWorkflow?.NotifyListening();
            UpdateDiagnostic("Recording");
        }

        private void HandleRecordingCompleted(CompletedMicrophoneRecording recording)
        {
            if (!sessionActive || recording.Clip == null) return;
            bool conversationalOwner = conversationalListening != null && conversationalListening.OwnsCapture;
            bool automatic = ShouldAutomaticallyProcess(conversationalOwner,
                gestureController != null ? gestureController.CurrentRecordingMode
                    : ThumbRecordingGestureController.RecordingMode.Manual);
            conversationalListening?.NotifyRecordingCompleted();
            StopPlayback();
            pendingRecording = recording;
            pendingWav = null;
            pendingTranscript = null;
            pendingRecordingIdentifier = CreateRecordingIdentifier();
            automaticConversationTurn = automatic;
            if (automatic)
            {
                reviewPanel.Hide();
                transcriptPanel.Hide();
                State = RecordingWorkflowState.Transcribing;
                aiWorkflow?.SetAutomaticConversationMode(true);
                BeginRequest(false, true);
                UpdateDiagnostic("Conversational recording finalized; automatic transcription started");
                return;
            }
            State = RecordingWorkflowState.Reviewing;
            aiWorkflow?.NotifyReviewing();
            transcriptPanel.Hide();
            reviewPanel.Show(recording.Clip.length, TogglePlayback, SendToAI, Discard, Rerecord);
            BrainstormMenuController menu=FindFirstObjectByType<BrainstormMenuController>();
            ThinkingSpacePreferencesStore preferences=menu?.PreferencesStore;
            bool firstReview=preferences!=null&&!preferences.Data.FirstReviewOptionsCompleted;
            bool accepted=ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.PlayPrerecorded(SelectReviewVoiceKey(firstReview))??false;
            if(firstReview&&accepted)preferences.SetFirstReviewOptionsCompleted(true);
            UpdateDiagnostic("Reviewing completed recording");
        }
        public static bool ShouldAutomaticallyProcess(bool conversationalCaptureOwner,
            ThumbRecordingGestureController.RecordingMode mode)
            => conversationalCaptureOwner
                && mode == ThumbRecordingGestureController.RecordingMode.Conversational;
        public static string SelectReviewVoiceKey(bool firstReview)=>firstReview?"first_review_options":"review_options";

        private void HandleRecordingFailed(string message)
        {
            conversationalListening?.NotifyRecordingFailed(message);
            if (State != RecordingWorkflowState.Recording) return;
            State = RecordingWorkflowState.Ready;
            aiWorkflow?.NotifyError(message);
            UpdateDiagnostic("Microphone failed: " + message);
            transcriptPanel.ShowNotice("Recording Failed", message, transcriptPanel.Hide);
        }

        public void NotifyConversationalSpeechStarted()
        {
            if (!sessionActive || State != RecordingWorkflowState.Ready) return;
            State = RecordingWorkflowState.Recording;
            aiWorkflow?.NotifyListening();
            UpdateDiagnostic("Conversational speech detected");
        }

        public void NotifyConversationalCaptureCancelled()
        {
            if (pendingRecording.HasValue || State != RecordingWorkflowState.Ready) return;
            UpdateDiagnostic("Silent conversational monitor recycled or stopped");
        }

        private void TogglePlayback()
        {
            if (!pendingRecording.HasValue || microphoneRecorder.IsCaptureActive) return;
            if (State == RecordingWorkflowState.Playing)
            {
                StopPlayback(); State = RecordingWorkflowState.Reviewing;
                reviewPanel.SetPlaying(false); UpdateDiagnostic("Playback stopped"); return;
            }
            if (State != RecordingWorkflowState.Reviewing) return;
            AudioClip clip = developmentUseOutputTestToneForPlay
                ? GetOrCreateOutputTestTone() : pendingRecording.Value.Clip;
            if (clip == null || clip.samples <= 0 || clip.frequency <= 0 || clip.channels <= 0)
            {
                playbackError = "Completed AudioClip is missing or invalid";
                UpdateDiagnostic("Playback rejected");
                return;
            }
            playbackSource.gameObject.SetActive(true);
            playbackSource.enabled = true;
            playbackSource.volume = 1f;
            playbackSource.mute = false;
            playbackSource.spatialBlend = 0f;
            playbackSource.clip = clip;
            playbackSource.time = 0f;
            playbackError = developmentUseOutputTestToneForPlay
                ? "Development 440 Hz output test tone selected" : "None";
            playbackStartedObserved = false;
            playbackCommandTime = Time.unscaledTime;
            playbackSource.Play();
            State = RecordingWorkflowState.Playing;
            reviewPanel.SetPlaying(true);
            reviewPanel.SetPlaybackProgress(0f, pendingRecording.Value.Clip.length);
            UpdateDiagnostic("Playing retained recording");
        }

        private void SendToAI()
        {
            if (!pendingRecording.HasValue || activeRequest != null
                || (State != RecordingWorkflowState.Reviewing && State != RecordingWorkflowState.Playing)) return;
            StopPlayback();
            reviewPanel.SetButtonsInteractable(false);
            reviewPanel.Hide();
            if (!string.IsNullOrEmpty(pendingTranscript))
            {
                State = RecordingWorkflowState.TranscriptReady;
                transcriptPanel.ShowTranscript(pendingTranscript, ConfirmTranscript, BackToRecording);
                UpdateDiagnostic("Showing retained transcript");
                return;
            }
            BeginRequest(pendingWav != null);
        }

        public void RetryLastFailed()
        {
            if (State != RecordingWorkflowState.TranscriptionFailed || !pendingRecording.HasValue
                || activeRequest != null || transcriptionService.IsBusy) return;
            BeginRequest(true);
        }

        private void BeginRequest(bool reuseWav, bool automatic = false)
        {
            if (!reuseWav) pendingWav = null;
            State = RecordingWorkflowState.Transcribing;
            aiWorkflow?.NotifyThinking();
            if (!automatic) transcriptPanel.ShowTranscribing(pendingRecordingIdentifier, BackToRecording);
            int generation = ++requestGeneration;
            lastStartedRequestGeneration = generation;
            lastTranscriptionMode = automatic ? "Automatic Conversational" : "Manual Review";
            lastRequestCompletion = "Request started";
            activeRequest = StartCoroutine(EncodeAndTranscribe(generation));
            UpdateDiagnostic(reuseWav ? "Retrying transcription" : "Encoding recording");
        }

        private IEnumerator EncodeAndTranscribe(int generation)
        {
            yield return null;
            if (!pendingRecording.HasValue)
            {
                activeRequest = null;
                lastRequestCompletion = "Cancelled because pending recording ownership was lost";
                yield break;
            }
            CompletedMicrophoneRecording recording = pendingRecording.Value;
            try { if (pendingWav == null) pendingWav = WavEncoder.Encode16BitPcm(recording); }
            catch (Exception ex)
            {
                activeRequest = null;
                HandleRequestFailure(generation, "WAV encoding failed: " + ex.Message, 0, 0f);
                yield break;
            }
            WavValidationResult wavValidation = WavEncoder.Validate16BitPcm(pendingWav, recording);
            lastWavValidation = wavValidation.ToDiagnosticString();
            if (!wavValidation.IsValid || !wavValidation.MetadataMatchesRecording)
            {
                activeRequest = null;
                lastRequestCompletion = "Rejected before upload by local WAV validation";
                HandleRequestFailure(generation,
                    "WAV validation failed before upload: " + wavValidation.Error, 0, 0f);
                yield break;
            }
            UpdateDiagnostic("Uploading WAV");
            SpeechTranscriptionResult result = null;
            SpeechTranscriptionRequest request = CreateTranscriptionRequest(recording, pendingWav,
                pendingRecordingIdentifier);
            yield return transcriptionService.Transcribe(request, value => result = value);
            activeRequest = null;
            lastRequestCompletion = transcriptionService.LastCompletionReason;
            if (!IsCurrentRequest(generation, requestGeneration, sessionActive)) yield break;
            if (result == null || !result.Success)
            {
                HandleRequestFailure(generation, result != null ? result.Error : "Transcription ended without a result.",
                    result != null ? result.HttpStatus : 0, result != null ? result.RequestDurationSeconds : 0f);
                yield break;
            }
            State = RecordingWorkflowState.TranscriptReady;
            pendingTranscript = result.Transcript;
            DiagnosticText = BuildDiagnostic(recording, pendingWav.Length, "Transcript ready", result.HttpStatus,
                result.RequestDurationSeconds, result.Transcript != null ? result.Transcript.Length : 0, null);
            if (automaticConversationTurn)
            {
                State = RecordingWorkflowState.Transcribing;
                aiWorkflow?.NotifyThinking();
                if (aiWorkflow == null || !aiWorkflow.SubmitApprovedTranscript(result.Transcript))
                {
                    string reason = aiWorkflow != null ? aiWorkflow.LastSubmissionRejection
                        : "The AI workflow component is unavailable.";
                    HandleAutomaticSubmissionFailure(reason);
                }
                else UpdateDiagnostic("Transcript validated; conversational request submitted automatically");
                yield break;
            }
            aiWorkflow?.NotifyReviewing();
            transcriptPanel.ShowTranscript(result.Transcript, ConfirmTranscript, BackToRecording);
        }

        public static SpeechTranscriptionRequest CreateTranscriptionRequest(
            CompletedMicrophoneRecording recording, byte[] wav, string recordingIdentifier)
            => new SpeechTranscriptionRequest
            {
                WavData = wav,
                RecordingIdentifier = recordingIdentifier,
                SessionIdentifier = recording.SessionIdentifier,
                RecordingDuration = recording.DurationSeconds,
                SampleRate = recording.SampleRate
            };

        public static bool IsCurrentRequest(int generation, int currentGeneration, bool sessionActive)
            => sessionActive && generation == currentGeneration;

        private void HandleRequestFailure(int generation, string error, long status, float seconds)
        {
            if (!IsCurrentRequest(generation, requestGeneration, sessionActive)) return;
            bool wasAutomatic = automaticConversationTurn;
            if (wasAutomatic)
            {
                automaticConversationTurn = false;
                aiWorkflow?.SetAutomaticConversationMode(false);
            }
            State = RecordingWorkflowState.TranscriptionFailed;
            aiWorkflow?.NotifyError(error);
            CompletedMicrophoneRecording recording = pendingRecording.Value;
            DiagnosticText = BuildDiagnostic(recording, pendingWav != null ? pendingWav.Length : 0,
                "Transcription failed", status, seconds, 0, error)
                + BuildSafeTranscriptionTransportDiagnostic();
            transcriptPanel.ShowError(error + BuildDevelopmentTranscriptionDiagnostic(),
                wasAutomatic ? RetryAutomaticTurn : RetryLastFailed,
                wasAutomatic ? Discard : BackToRecording);
            RequestTranscriptionErrorVoice(generation, error, status);
        }

        private void HandleAutomaticSubmissionFailure(string error)
        {
            automaticConversationTurn = false;
            aiWorkflow?.SetAutomaticConversationMode(false);
            State = RecordingWorkflowState.TranscriptionFailed;
            aiWorkflow?.NotifyError(error);
            transcriptPanel.ShowWorkflowError("Conversation Submission Failed", error,
                RetryAutomaticTurn, Discard);
            UpdateDiagnostic("Automatic conversation submission failed; completed recording retained");
        }

        private void RetryAutomaticTurn()
        {
            if (!pendingRecording.HasValue || activeRequest != null || transcriptionService.IsBusy) return;
            automaticConversationTurn = true;
            aiWorkflow?.SetAutomaticConversationMode(true);
            transcriptPanel.Hide();
            BeginRequest(pendingWav != null, true);
        }

        private void HandleAutomaticTurnCompleted(bool workflowSucceeded)
        {
            if (!automaticConversationTurn) return;
            automaticConversationTurn = false;
            aiWorkflow?.SetAutomaticConversationMode(false);
            if (!sessionActive) return;
            if (!workflowSucceeded)
            {
                State = RecordingWorkflowState.TranscriptionFailed;
                string error = aiWorkflow != null && !string.IsNullOrWhiteSpace(aiWorkflow.LastAutomaticError)
                    ? aiWorkflow.LastAutomaticError : "Orby could not complete that request.";
                transcriptPanel.ShowWorkflowError("Conversation Processing Failed", error,
                    RetryAutomaticConversationRequest, Discard);
                UpdateDiagnostic("Automatic conversational processing failed; recording retained");
                return;
            }
            reviewPanel.Hide();
            transcriptPanel.Hide();
            ClearPendingRecording();
            State = RecordingWorkflowState.Ready;
            gestureController?.RequireFreshRecordingGesture();
            if (conversationalListening != null && conversationalListening.ListeningEnabled)
                aiWorkflow?.NotifyListening();
            else aiWorkflow?.NotifyIdle();
            UpdateDiagnostic("Automatic conversational turn completed; ready to listen");
        }

        private void RetryAutomaticConversationRequest()
        {
            if (!sessionActive || string.IsNullOrWhiteSpace(pendingTranscript)
                || aiWorkflow == null || aiWorkflow.IsBlockingRecording) return;
            automaticConversationTurn = true;
            aiWorkflow.SetAutomaticConversationMode(true);
            transcriptPanel.Hide();
            State = RecordingWorkflowState.Transcribing;
            if (!aiWorkflow.SubmitApprovedTranscript(pendingTranscript))
                HandleAutomaticSubmissionFailure(aiWorkflow.LastSubmissionRejection);
            else UpdateDiagnostic("Retrying retained transcript through conversational processing");
        }

        private string BuildSafeTranscriptionTransportDiagnostic()
        {
            if (transcriptionService == null) return "\nTransport diagnostics: service unavailable";
            return "\nEndpoint: " + transcriptionService.BackendUrl
                + "\nRequest stage/category: " + transcriptionService.LastRequestStage
                + " / " + transcriptionService.LastErrorCategory
                + "\nHTTP: " + transcriptionService.LastHttpStatus
                + "   Valid completed recording submitted: "
                + (transcriptionService.LastSubmittedValidRecording ? "YES" : "no")
                + "\nCompletion: " + transcriptionService.LastCompletionReason;
        }

        private string BuildDevelopmentTranscriptionDiagnostic()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CompletedMicrophoneRecording recording = pendingRecording.HasValue
                ? pendingRecording.Value : default;
            return "\n\nDiagnostics"
                + "\nMode: " + lastTranscriptionMode
                + "\nEndpoint: " + (transcriptionService != null
                    ? transcriptionService.BackendUrl : "Unavailable")
                + "\nStage: " + (transcriptionService != null
                    ? transcriptionService.LastRequestStage : "Unavailable")
                + "\nHTTP/category: " + (transcriptionService != null
                    ? transcriptionService.LastHttpStatus + " / " + transcriptionService.LastErrorCategory
                    : "0 / unavailable")
                + "\nWAV bytes/duration: " + (pendingWav != null ? pendingWav.Length : 0)
                + " / " + recording.DurationSeconds.ToString("0.00") + "s"
                + "\nRequest generation: " + lastStartedRequestGeneration + " / " + requestGeneration
                + "\nCompletion: " + lastRequestCompletion
                + "\nWAV validation: " + lastWavValidation;
#else
            return string.Empty;
#endif
        }

        private void RequestTranscriptionErrorVoice(int generation, string error, long status)
        {
            if (lastErrorVoiceGeneration == generation) return;
            lastErrorVoiceGeneration = generation;
            BrainstormSession active = FindFirstObjectByType<BrainstormMenuController>()?.SessionStore?.ActiveSession;
            string sessionId = active != null ? active.SessionId
                : pendingRecording.HasValue ? pendingRecording.Value.SessionIdentifier : null;
            string key = SelectTranscriptionErrorVoiceKey(error, status);
            bool accepted = ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.PlayPrerecorded(
                key, sessionId, null, false) ?? false;
            UpdateDiagnostic("Transcription error voice " + key + (accepted ? " accepted" : " rejected"));
        }

        public static string SelectTranscriptionErrorVoiceKey(string error, long status)
        {
            bool network = status >= 500 || (!string.IsNullOrEmpty(error)
                && (error.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0
                    || error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                    || error.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0));
            return network ? "error_network" : "error_general";
        }

        private void ConfirmTranscript()
        {
            if (State != RecordingWorkflowState.TranscriptReady) return;
            if (aiWorkflow == null || !aiWorkflow.SubmitApprovedTranscript(pendingTranscript))
            {
                string reason = aiWorkflow != null
                    ? aiWorkflow.LastSubmissionRejection
                    : "The AI workflow component is unavailable.";
                transcriptPanel.ShowNotice("AI Request Unavailable",
                    reason + "\n\nThe completed recording remains available.", BackToRecording);
                return;
            }
            transcriptPanel.Hide(); ClearPendingRecording();
            State = RecordingWorkflowState.Ready;
            gestureController.RequireFreshRecordingGesture();
            UpdateDiagnostic("Transcript accepted; AI processing started");
        }

        private void BackToRecording()
        {
            if (!pendingRecording.HasValue) return;
            if (State == RecordingWorkflowState.Transcribing) CancelActiveRequest();
            transcriptPanel.Hide();
            State = RecordingWorkflowState.Reviewing;
            aiWorkflow?.NotifyReviewing();
            CompletedMicrophoneRecording recording = pendingRecording.Value;
            reviewPanel.Show(recording.Clip.length, TogglePlayback, SendToAI, Discard, Rerecord);
            UpdateDiagnostic("Returned to recording review");
        }

        private void Discard()
        {
            if (State == RecordingWorkflowState.Transcribing) return;
            StopPlayback(); reviewPanel.Hide(); transcriptPanel.Hide(); ClearPendingRecording();
            State = RecordingWorkflowState.Ready;
            aiWorkflow?.NotifyIdle();
            gestureController.RequireFreshRecordingGesture();
            UpdateDiagnostic("Recording discarded; ready");
        }

        private void Rerecord()
        {
            if (State != RecordingWorkflowState.Reviewing && State != RecordingWorkflowState.Playing) return;
            CancelActiveRequest(); StopPlayback(); reviewPanel.Hide(); transcriptPanel.Hide(); ClearPendingRecording();
            if (!sessionActive)
            {
                State = RecordingWorkflowState.Ready;
                UpdateDiagnostic("Rerecord unavailable: no active session");
                transcriptPanel.ShowNotice("Recording Unavailable", "Start or reopen a brainstorming session first.", transcriptPanel.Hide);
                return;
            }
            if (conversationalListening != null && conversationalListening.ListeningEnabled)
            {
                State = RecordingWorkflowState.Ready;
                UpdateDiagnostic("Rerecord requested; conversational listening resumed");
                return;
            }
            State = RecordingWorkflowState.Recording;
            UpdateDiagnostic("Rerecord requested; starting microphone");
            if (!microphoneRecorder.BeginRecording())
            {
                State = RecordingWorkflowState.Ready;
                string error = string.IsNullOrEmpty(microphoneRecorder.LastError)
                    ? "Microphone could not start." : microphoneRecorder.LastError;
                UpdateDiagnostic("Rerecord failed: " + error);
                transcriptPanel.ShowNotice("Recording Failed", error, transcriptPanel.Hide);
            }
        }

        private void StopPlayback()
        {
            if (playbackSource != null && playbackSource.isPlaying) playbackSource.Stop();
            if (playbackSource != null) playbackSource.clip = null;
            playbackStartedObserved = false;
            if (reviewPanel != null)
            {
                reviewPanel.SetPlaying(false);
                reviewPanel.SetPlaybackProgress(0f, pendingRecording.HasValue
                    ? pendingRecording.Value.Clip.length : 0f);
            }
        }

        private AudioClip GetOrCreateOutputTestTone()
        {
            if (outputTestTone != null) return outputTestTone;
            const int sampleRate = 48000;
            float[] samples = new float[sampleRate];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Sin(2f * Mathf.PI * 440f * i / sampleRate) * 0.2f;
            outputTestTone = AudioClip.Create("Thinking Space Output Test Tone", sampleRate, 1, sampleRate, false);
            outputTestTone.SetData(samples, 0);
            return outputTestTone;
        }

        public void ExportPendingRecordingForDevelopment()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!enableDevelopmentWavExport)
            {
                lastExportValidation = "Export is disabled in the Inspector.";
                reviewPanel.SetExportStatus(lastExportValidation);
                UpdateDiagnostic("WAV export rejected");
                return;
            }
            if (microphoneRecorder == null || microphoneRecorder.IsCaptureActive)
            {
                lastExportValidation = "Cannot export while microphone capture is active.";
                reviewPanel.SetExportStatus(lastExportValidation);
                UpdateDiagnostic("WAV export rejected");
                return;
            }
            if (!pendingRecording.HasValue || pendingRecording.Value.Clip == null)
            {
                lastExportValidation = "No completed recording is pending.";
                reviewPanel.SetExportStatus(lastExportValidation);
                UpdateDiagnostic("WAV export rejected");
                return;
            }
            try
            {
                CompletedMicrophoneRecording recording = pendingRecording.Value;
                if (pendingWav == null) pendingWav = WavEncoder.Encode16BitPcm(recording);
                WavValidationResult validation = WavEncoder.Validate16BitPcm(pendingWav, recording);
                lastExportValidation = validation.ToDiagnosticString();
                if (!validation.IsValid || !validation.MetadataMatchesRecording)
                {
                    reviewPanel.SetExportStatus("Export failed: " + validation.Error);
                    UpdateDiagnostic("WAV validation failed; file was not written");
                    return;
                }
                string identifier = SanitizeFileName(string.IsNullOrEmpty(pendingRecordingIdentifier)
                    ? DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") : pendingRecordingIdentifier);
                string directory = Path.Combine(Application.persistentDataPath, "ThinkingSpace", "Recordings");
                Directory.CreateDirectory(directory);
                string path = GetUniqueExportPath(directory, "thinking-space-recording-" + identifier, ".wav");
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    stream.Write(pendingWav, 0, pendingWav.Length);
                lastExportPath = path;
                reviewPanel.SetExportStatus("Exported " + Path.GetFileName(path)
                    + "\nQuest: " + path);
                UpdateDiagnostic("Development WAV exported successfully");
            }
            catch (Exception ex)
            {
                lastExportValidation = "Export failed: " + ex.Message;
                reviewPanel.SetExportStatus(lastExportValidation);
                UpdateDiagnostic("Development WAV export failed");
            }
#else
            lastExportValidation = "WAV export is unavailable in production builds.";
            UpdateDiagnostic("WAV export unavailable");
#endif
        }

        private static string GetUniqueExportPath(string directory, string baseName, string extension)
        {
            string path = Path.Combine(directory, baseName + extension);
            for (int suffix = 1; File.Exists(path); suffix++)
                path = Path.Combine(directory, baseName + "-" + suffix + extension);
            return path;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
            return value;
        }

        private void CancelActiveRequest()
        {
            requestGeneration++;
            lastRequestCompletion = "Cancelled by workflow; generation invalidated";
            transcriptionService?.Cancel();
            if (activeRequest != null) StopCoroutine(activeRequest);
            activeRequest = null;
            if (automaticConversationTurn)
            {
                automaticConversationTurn = false;
                aiWorkflow?.SetAutomaticConversationMode(false);
            }
        }

        private void ClearPendingRecording()
        {
            AudioClip clip = pendingRecording.HasValue ? pendingRecording.Value.Clip : null;
            pendingRecording = null; pendingWav = null; pendingRecordingIdentifier = null; pendingTranscript = null;
            if (clip != null) microphoneRecorder.DiscardLastCompletedRecording(clip);
        }

        [ContextMenu("Development/Review Assigned Test Clip")]
        public void ReviewDevelopmentClip()
        {
            if (developmentTestClip == null || developmentTestClip.samples <= 0 || !sessionActive) return;
            HandleRecordingCompleted(new CompletedMicrophoneRecording(developmentTestClip, developmentTestClip.samples,
                developmentTestClip.frequency, developmentTestClip.channels, developmentTestClip.length,
                0f, 0f, true, "Development Test"));
        }

        private string CreateRecordingIdentifier()
        {
            recordingSequence++;
            return DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + recordingSequence;
        }

        private void UpdateDiagnostic(string message)
        {
            DiagnosticText = "WORKFLOW: " + State + "   Session: " + (sessionActive ? "active" : "inactive")
                + "   Automatic conversation: " + (automaticConversationTurn ? "YES" : "no")
                + "\nPending audio: " + (pendingRecording.HasValue ? "YES" : "no")
                + "   WAV: " + (pendingWav != null ? pendingWav.Length : 0) + " bytes"
                + GetPlaybackDiagnostic()
                + "\nEXPORT Path: " + lastExportPath + "\n" + lastExportValidation + "\n" + message;
        }

        private string GetPlaybackDiagnostic()
        {
            if (!pendingRecording.HasValue) return "\nPLAYBACK: no clip";
            CompletedMicrophoneRecording recording = pendingRecording.Value;
            return "\nPLAYBACK Clip: " + recording.DurationSeconds.ToString("0.00") + "s   "
                + recording.SampleRate + " Hz   " + recording.Channels + " ch"
                + "   RMS: " + recording.Rms.ToString("0.0000") + "   Peak: " + recording.Peak.ToString("0.0000")
                + "\nSource active/enabled: " + playbackSource.gameObject.activeInHierarchy + "/" + playbackSource.enabled
                + "   mute: " + playbackSource.mute + "   volume: " + playbackSource.volume.ToString("0.00")
                + "   2D: " + (playbackSource.spatialBlend == 0f ? "YES" : "NO")
                + "   isPlaying: " + playbackSource.isPlaying
                + "   time: " + (playbackSource.clip != null ? playbackSource.time.ToString("0.00") : "0.00")
                + "   load: " + (recording.Clip != null ? recording.Clip.loadState.ToString() : "Missing")
                + "\nListener pause/volume: " + AudioListener.pause + "/" + AudioListener.volume.ToString("0.00")
                + "\nPlayback error: " + playbackError;
        }

        private static string BuildDiagnostic(CompletedMicrophoneRecording recording, int wavBytes,
            string state, long status, float requestSeconds, int transcriptLength, string error)
        {
            return "WORKFLOW: " + state + "\nRecording: " + recording.DurationSeconds.ToString("0.00")
                + "s   WAV: " + wavBytes + " bytes   Rate: " + recording.SampleRate
                + " Hz\nHTTP: " + status + "   Request: " + requestSeconds.ToString("0.00")
                + "s   Text: " + transcriptLength + " chars"
                + (string.IsNullOrEmpty(error) ? string.Empty : "\nError: " + error);
        }
    }
}
