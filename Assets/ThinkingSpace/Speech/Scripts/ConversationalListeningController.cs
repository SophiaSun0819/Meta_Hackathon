using System;
using UnityEngine;
using ThinkingSpace.Companion;

namespace ThinkingSpace.Speech
{
    public enum ConversationalListeningState
    {
        Off, WaitingForCapture, Calibrating, WaitingForSpeech,
        SpeechActive, SilenceCountdown, Suspended, Finalizing, Error
    }

    public readonly struct VoiceActivityUpdate
    {
        public readonly bool SpeechStarted, SpeechActive, UtteranceQualified, UtteranceQualifiedNow,
            ImpulseRejected, ShouldFinalize, SilenceReset;
        public readonly float NoiseFloor, SpeechThreshold, ReleaseThreshold, SilenceElapsed, VoicedDuration;
        public VoiceActivityUpdate(bool started, bool active, bool qualified, bool qualifiedNow, bool rejected,
            bool finalize, bool silenceReset, float noise, float threshold, float release, float silence, float voiced)
        { SpeechStarted = started; SpeechActive = active; UtteranceQualified = qualified;
          UtteranceQualifiedNow = qualifiedNow; ImpulseRejected = rejected;
          ShouldFinalize = finalize; SilenceReset = silenceReset; NoiseFloor = noise;
          SpeechThreshold = threshold; ReleaseThreshold = release;
          SilenceElapsed = silence; VoicedDuration = voiced; }
    }

    public sealed class AdaptiveVoiceActivityDetector
    {
        private readonly float thresholdMultiplier, thresholdOffset, minimumThreshold;
        private readonly float startConfirmation, minimumVoicedDuration, hysteresisRatio;
        private float silenceDuration;
        private const float ReleaseHangoverSeconds = 0.25f;
        private float candidateDuration, voicedDuration, silenceElapsed, releaseBandElapsed;
        private bool speechActive, utteranceQualified;
        public float NoiseFloor { get; private set; }
        public float SpeechThreshold => Mathf.Max(minimumThreshold,
            NoiseFloor * thresholdMultiplier + thresholdOffset);
        public float ReleaseThreshold => Mathf.Max(minimumThreshold * 0.7f,
            NoiseFloor * Mathf.Max(1.15f, thresholdMultiplier * hysteresisRatio)
                + thresholdOffset * 0.5f);

        public AdaptiveVoiceActivityDetector(float initialNoiseFloor, float multiplier, float offset,
            float minimum, float startSeconds, float minimumVoiced, float silenceSeconds, float hysteresis)
        {
            NoiseFloor = Mathf.Max(0.00001f, initialNoiseFloor);
            thresholdMultiplier = multiplier; thresholdOffset = offset; minimumThreshold = minimum;
            startConfirmation = startSeconds; minimumVoicedDuration = minimumVoiced;
            silenceDuration = silenceSeconds; hysteresisRatio = hysteresis;
        }

        public VoiceActivityUpdate Process(float rms, float deltaTime)
        {
            rms = Mathf.Max(0f, rms); deltaTime = Mathf.Max(0f, deltaTime);
            float threshold = SpeechThreshold;
            bool started = false, qualified = false, rejected = false, silenceReset = false;
            if (!speechActive)
            {
                if (rms >= threshold)
                {
                    candidateDuration += deltaTime;
                    if (candidateDuration >= startConfirmation)
                    {
                        speechActive = true; started = true;
                        voicedDuration = candidateDuration;
                        silenceElapsed = releaseBandElapsed = 0f;
                    }
                }
                else
                {
                    candidateDuration = 0f;
                    float blend = 1f - Mathf.Exp(-deltaTime / 2.5f);
                    NoiseFloor = Mathf.Lerp(NoiseFloor, rms, blend);
                }
            }
            else
            {
                float releaseThreshold = ReleaseThreshold;
                if (rms >= threshold)
                {
                    voicedDuration += deltaTime;
                    silenceReset = silenceElapsed > 0f;
                    silenceElapsed = releaseBandElapsed = 0f;
                }
                else if (rms >= releaseThreshold)
                {
                    releaseBandElapsed += deltaTime;
                    if (releaseBandElapsed <= ReleaseHangoverSeconds)
                    {
                        voicedDuration += deltaTime;
                        silenceReset = silenceElapsed > 0f;
                        silenceElapsed = 0f;
                    }
                    else silenceElapsed += deltaTime;
                }
                else
                {
                    releaseBandElapsed = 0f;
                    silenceElapsed += deltaTime;
                }
                if (!utteranceQualified && voicedDuration >= minimumVoicedDuration)
                { utteranceQualified = true; qualified = true; }
                if (!utteranceQualified && silenceElapsed >= 0.55f)
                {
                    speechActive = false; rejected = true;
                    candidateDuration = voicedDuration = silenceElapsed = releaseBandElapsed = 0f;
                }
            }
            bool finalize = speechActive && utteranceQualified
                && silenceElapsed >= silenceDuration;
            return new VoiceActivityUpdate(started, speechActive, utteranceQualified, qualified, rejected, finalize,
                silenceReset,
                NoiseFloor, SpeechThreshold, ReleaseThreshold, silenceElapsed, voicedDuration);
        }

        public void Calibrate(float rms, float deltaTime)
        {
            rms = Mathf.Max(0.00001f, rms);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / 0.2f);
            NoiseFloor = Mathf.Lerp(NoiseFloor, rms, blend);
            candidateDuration = voicedDuration = silenceElapsed = releaseBandElapsed = 0f;
            speechActive = utteranceQualified = false;
        }

        public void SetSilenceDuration(float seconds) => silenceDuration = Mathf.Clamp(seconds, 1f, 10f);
    }

    [DisallowMultipleComponent]
    public sealed class ConversationalListeningController : MonoBehaviour
    {
        [Header("Voice Activity Detection")]
        [SerializeField, Range(1f, 10f)] private float silenceDuration = 2f;
        [SerializeField, Range(0.05f, 0.5f)] private float speechStartConfirmation = 0.18f;
        [SerializeField, Range(0.15f, 1.5f)] private float minimumVoicedDuration = 0.65f;
        [SerializeField, Range(1.2f, 6f)] private float noiseMultiplier = 2.4f;
        [SerializeField, Range(0f, 0.02f)] private float thresholdOffset = 0.0015f;
        [SerializeField, Range(0.0005f, 0.03f)] private float minimumSpeechThreshold = 0.003f;
        [SerializeField, Range(0.3f, 0.9f)] private float releaseHysteresis = 0.65f;
        [SerializeField, Range(0.1f, 0.5f)] private float preRollSeconds = 0.25f;
        [SerializeField, Range(0.2f, 1.5f)] private float startupCalibrationSeconds = 0.6f;

        private QuestMicrophoneRecorder recorder;
        private SpeechTranscriptionCoordinator coordinator;
        private OrbyConversationStateController orby;
        private OrbyPlaceholderView orbyView;
        private ConversationalListeningHud hud;
        private AdaptiveVoiceActivityDetector detector;
        private bool sessionActive, ownsCapture, speechWasDetected, shutdownAfterUtterance;
        private int generation;
        private float captureStartedAt;

        public bool ListeningEnabled { get; private set; }
        public bool OwnsCapture => ownsCapture;
        public bool SpeechWasDetected => speechWasDetected;
        public ConversationalListeningState State { get; private set; } = ConversationalListeningState.Off;
        public float NoiseFloor { get; private set; }
        public float SpeechThreshold { get; private set; }
        public float ReleaseThreshold { get; private set; }
        public float SilenceRemaining { get; private set; }
        public float ContinuousSpeechDuration { get; private set; }
        public float LastDetectedSpeechTime { get; private set; } = -1f;
        public string LastFinalizationReason { get; private set; } = "None";
        public string LastSilenceDecision { get; private set; } = "None";
        public int SilenceResetCount { get; private set; }
        public string WorkflowBlocker { get; private set; } = "Listening disabled";
        public string DiagnosticText => "CONVERSATION: " + State
            + "   Enabled: " + (ListeningEnabled ? "YES" : "no")
            + "\nMic: " + (recorder != null ? recorder.State.ToString() : "missing")
            + "   RMS: " + (recorder != null ? recorder.LiveRms.ToString("0.0000") : "0")
            + "\nNoise: " + NoiseFloor.ToString("0.0000")
            + "   Start/release: " + SpeechThreshold.ToString("0.0000")
            + "/" + ReleaseThreshold.ToString("0.0000")
            + "\nSpeech: " + (speechWasDetected ? "YES" : "no")
            + "   Silence remaining: " + SilenceRemaining.ToString("0.0") + "s"
            + "\nContinuous voice: " + ContinuousSpeechDuration.ToString("0.0")
            + "s   Last speech: " + LastDetectedSpeechTime.ToString("0.0")
            + "\nRecorder elapsed: " + (recorder != null ? recorder.ElapsedSeconds.ToString("0.0") : "0")
            + "s   Finalization: " + LastFinalizationReason
            + "\nSilence decision: " + LastSilenceDecision + "   Resets: " + SilenceResetCount
            + "   Voice active: " + (OrbyVoiceDirector.Instance != null && OrbyVoiceDirector.Instance.IsSpeaking ? "YES" : "no")
            + "\nMic Off deferred: " + (shutdownAfterUtterance ? "YES — preserving utterance" : "no")
            + "\nBlocker: " + WorkflowBlocker;

        public void Configure(QuestMicrophoneRecorder microphone,
            SpeechTranscriptionCoordinator workflow)
        {
            recorder = microphone;
            coordinator = workflow;
            hud = GetComponent<ConversationalListeningHud>();
            if (hud == null) hud = gameObject.AddComponent<ConversationalListeningHud>();
            ResolveOrby();
        }

        public float SilenceDurationSeconds => silenceDuration;
        public void SetSilenceDuration(float seconds)
        {
            silenceDuration = ThinkingSpace.UI.ThinkingSpacePreferencesStore.ClampSilence(seconds);
            detector?.SetSilenceDuration(silenceDuration);
            if (!speechWasDetected) SilenceRemaining = silenceDuration;
        }

        public void SetSessionActive(bool active)
        {
            sessionActive = active;
            if (!active) DisableListening("Session ended", true);
        }

        public void EnableListening()
        {
            if (!sessionActive) { WorkflowBlocker = "No active brainstorming session"; return; }
            if (coordinator != null && !coordinator.CanBeginRecording)
            { WorkflowBlocker = "Recording is temporarily blocked"; return; }
            if (ListeningEnabled) return;
            ListeningEnabled = true;
            shutdownAfterUtterance = false;
            generation++;
            State = ConversationalListeningState.WaitingForCapture;
            WorkflowBlocker = "None";
            ResolveOrby();
            orby?.SetState(OrbyConversationState.Listening);
            hud?.SetListeningMode(true, true);
            OrbyVoiceDirector.Instance?.PlayPrerecorded("mic_on");
        }

        public void DisableListening(string reason = "Disabled by thumbs-down", bool cancelActiveSpeech = false)
        {
            bool wasEnabled = ListeningEnabled;
            ListeningEnabled = false;
            generation++;
            WorkflowBlocker = reason;
            if (ownsCapture && recorder != null && recorder.IsCaptureActive)
            {
                if (ShouldDeferMicrophoneShutdown(ownsCapture, recorder.IsCaptureActive,
                    speechWasDetected, cancelActiveSpeech))
                {
                    shutdownAfterUtterance = true;
                    WorkflowBlocker = "Mic Off requested; preserving active utterance until VAD finalizes it";
                    LastFinalizationReason = "Mic Off deferred until the active utterance completes";
                }
                else
                {
                    shutdownAfterUtterance = false;
                    coordinator?.NotifyConversationalCaptureCancelled();
                    recorder.CancelRecording();
                    ownsCapture = false;
                    State = ConversationalListeningState.Off;
                }
            }
            else State = ConversationalListeningState.Off;
            if (!shutdownAfterUtterance
                && (coordinator == null || coordinator.State == RecordingWorkflowState.Ready))
                orby?.SetState(OrbyConversationState.Idle);
            orbyView?.SetLiveAudioLevel(0f, false, 0f);
            if (wasEnabled) hud?.SetListeningMode(false, true);
            if(wasEnabled&&reason=="Disabled by thumbs-down"&&!shutdownAfterUtterance)
                OrbyVoiceDirector.Instance?.PlayPrerecorded("mic_off");
        }

        public static bool ShouldDeferMicrophoneShutdown(bool ownsCapture, bool captureActive,
            bool utteranceQualified, bool explicitCancellation)
            => ownsCapture && captureActive && utteranceQualified && !explicitCancellation;

        private void Update()
        {
            ResolveOrby();
            UpdateHud();
            if ((!ListeningEnabled && !shutdownAfterUtterance) || !sessionActive
                || recorder == null || coordinator == null) return;
            if (orby != null && orby.State == OrbyConversationState.Speaking)
            {
                if (ownsCapture && speechWasDetected)
                {
                    State = ConversationalListeningState.SilenceCountdown;
                    WorkflowBlocker = "Qualified utterance retained; Orby playback should be deferred";
                    return;
                }
                if (ownsCapture && recorder.IsCaptureActive)
                {
                    coordinator.NotifyConversationalCaptureCancelled();
                    recorder.CancelRecording();
                    ownsCapture = false;
                }
                State = ConversationalListeningState.Suspended;
                WorkflowBlocker = "Orby speech playback";
                return;
            }
            if (!ownsCapture)
            {
                if (!coordinator.CanBeginRecording)
                {
                    State = ConversationalListeningState.Suspended;
                    WorkflowBlocker = coordinator.State.ToString();
                    return;
                }
                StartMonitoringCapture();
                return;
            }
            if (!recorder.IsRecording) return;

            if (State == ConversationalListeningState.Calibrating)
            {
                detector.Calibrate(recorder.LiveRms, Time.unscaledDeltaTime);
                NoiseFloor = detector.NoiseFloor;
                SpeechThreshold = detector.SpeechThreshold;
                ReleaseThreshold = detector.ReleaseThreshold;
                float remaining = startupCalibrationSeconds - (Time.unscaledTime - captureStartedAt);
                WorkflowBlocker = "Calibrating ambient audio · " + Mathf.Max(0f, remaining).ToString("0.0") + "s";
                if (remaining > 0f) return;
                State = ConversationalListeningState.WaitingForSpeech;
                WorkflowBlocker = "Waiting for speech";
            }

            VoiceActivityUpdate update = detector.Process(recorder.LiveRms, Time.unscaledDeltaTime);
            NoiseFloor = update.NoiseFloor;
            SpeechThreshold = update.SpeechThreshold;
            ReleaseThreshold = update.ReleaseThreshold;
            ContinuousSpeechDuration = update.VoicedDuration;
            if (update.SpeechActive && recorder.LiveRms >= update.ReleaseThreshold)
                LastDetectedSpeechTime = Time.unscaledTime;
            SilenceRemaining = update.UtteranceQualified
                ? Mathf.Max(0f, silenceDuration - update.SilenceElapsed) : silenceDuration;
            if (update.SilenceReset)
            { SilenceResetCount++; LastSilenceDecision = "Countdown reset by renewed speech above activation threshold"; }
            else if (update.UtteranceQualified && update.SilenceElapsed > 0f)
                LastSilenceDecision = "Countdown advancing below sustained-speech threshold";
            if (update.UtteranceQualifiedNow)
            {
                speechWasDetected = true;
                recorder.BeginLogicalUtterance(update.VoicedDuration + preRollSeconds);
                coordinator.NotifyConversationalSpeechStarted();
                State = ConversationalListeningState.SpeechActive;
                WorkflowBlocker = "None";
                orby?.SetState(OrbyConversationState.SpeechActive);
            }
            if (update.ImpulseRejected)
            {
                speechWasDetected = false;
                State = ConversationalListeningState.WaitingForSpeech;
                WorkflowBlocker = "Brief noise ignored";
            }
            else if (update.UtteranceQualified && update.SilenceElapsed > 0f)
                State = ConversationalListeningState.SilenceCountdown;
            else if (speechWasDetected)
                State = ConversationalListeningState.SpeechActive;
            bool countingDown = State == ConversationalListeningState.SilenceCountdown;
            orbyView?.SetLiveAudioLevel(recorder.LiveRms, countingDown,
                silenceDuration > 0f ? SilenceRemaining / silenceDuration : 0f);
            if (update.ShouldFinalize)
            {
                State = ConversationalListeningState.Finalizing;
                WorkflowBlocker = "Finalizing utterance";
                LastFinalizationReason = shutdownAfterUtterance
                    ? "Mic Off: active utterance preserved through end-of-speech"
                    : silenceDuration.ToString("0.0") + " seconds of end-of-speech silence";
                recorder.StopRecording(LastFinalizationReason);
            }
        }

        private void StartMonitoringCapture()
        {
            detector = new AdaptiveVoiceActivityDetector(0.001f, noiseMultiplier, thresholdOffset,
                minimumSpeechThreshold, speechStartConfirmation, minimumVoicedDuration,
                silenceDuration, releaseHysteresis);
            speechWasDetected = false;
            SilenceRemaining = silenceDuration;
            NoiseFloor = detector.NoiseFloor;
            SpeechThreshold = detector.SpeechThreshold;
            ReleaseThreshold = detector.ReleaseThreshold;
            ContinuousSpeechDuration = 0f;
            LastDetectedSpeechTime = -1f;
            LastFinalizationReason = "None";
            LastSilenceDecision = "Awaiting calibrated microphone samples";
            SilenceResetCount = 0;
            ownsCapture = true;
            State = ConversationalListeningState.Calibrating;
            WorkflowBlocker = "Starting microphone monitor";
            captureStartedAt = Time.unscaledTime;
            if (!recorder.BeginMonitoring())
            {
                ownsCapture = false;
                State = ConversationalListeningState.Error;
                WorkflowBlocker = string.IsNullOrEmpty(recorder.LastError)
                    ? "Microphone failed to start" : recorder.LastError;
                orby?.SetError(WorkflowBlocker);
            }
        }

        public void NotifyRecordingStarted()
        {
            if (!ownsCapture) return;
            captureStartedAt = Time.unscaledTime;
            State = ConversationalListeningState.Calibrating;
            WorkflowBlocker = "Calibrating ambient audio";
            orby?.SetState(OrbyConversationState.Listening);
        }

        public void NotifyRecordingCompleted()
        {
            if (recorder != null) LastFinalizationReason = recorder.LastFinalizationReason;
            bool announceDeferredMicOff = shutdownAfterUtterance;
            ownsCapture = false;
            shutdownAfterUtterance = false;
            State = ListeningEnabled ? ConversationalListeningState.Suspended : ConversationalListeningState.Off;
            WorkflowBlocker = "Recording review pending";
            orbyView?.SetLiveAudioLevel(0f, false, 0f);
            if (announceDeferredMicOff) OrbyVoiceDirector.Instance?.PlayPrerecorded("mic_off");
        }

        public void NotifyRecordingFailed(string message)
        {
            ListeningEnabled = false;
            ownsCapture = false;
            shutdownAfterUtterance = false;
            State = ConversationalListeningState.Error;
            WorkflowBlocker = message ?? "Microphone failure";
            orby?.SetError(WorkflowBlocker);
        }

        private void ResolveOrby()
        {
            if (orby == null) orby = FindFirstObjectByType<OrbyConversationStateController>();
            if (orbyView == null) orbyView = FindFirstObjectByType<OrbyPlaceholderView>();
            if (hud != null && orbyView != null) hud.Configure(orbyView.transform);
        }

        private void UpdateHud()
        {
            if (hud == null || recorder == null) return;
            MicrophoneStatusDisplayState display = ConversationalListeningHud.ResolveDisplayState(
                State, orby != null ? orby.State : OrbyConversationState.Idle,
                ListeningEnabled, coordinator != null ? coordinator.State : RecordingWorkflowState.Ready);
            hud.SetStatus(display, recorder.LiveRms, recorder.LivePeak,
                recorder.LogicalUtteranceElapsedSeconds);
        }

        private void OnApplicationPause(bool paused)
        { if (paused) DisableListening("Application paused", true); }
        private void OnApplicationFocus(bool focused)
        { if (!focused) DisableListening("Application focus lost", true); }
        private void OnDisable() => DisableListening("Controller disabled", true);
    }
}
