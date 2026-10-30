using System;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    public enum MicrophoneRecordingState { Idle, RequestingPermission, Starting, Recording, Stopping, Error }

    public readonly struct CompletedMicrophoneRecording
    {
        public readonly AudioClip Clip;
        public readonly int SampleFrames;
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly float DurationSeconds;
        public readonly float Rms;
        public readonly float Peak;
        public readonly bool ContainsMeaningfulAudio;
        public readonly string SessionIdentifier;

        public CompletedMicrophoneRecording(AudioClip clip, int frames, int rate, int channels,
            float duration, float rms, float peak, bool containsMeaningfulAudio, string sessionIdentifier)
        {
            Clip = clip; SampleFrames = frames; SampleRate = rate; Channels = channels;
            DurationSeconds = duration; Rms = rms; Peak = peak;
            ContainsMeaningfulAudio = containsMeaningfulAudio; SessionIdentifier = sessionIdentifier;
        }
    }

    public interface IMicrophoneRecordingService
    {
        MicrophoneRecordingState State { get; }
        bool BeginRecording();
        void StopRecording();
        event Action RecordingStarted;
        event Action<CompletedMicrophoneRecording> RecordingCompleted;
        event Action<string> RecordingFailed;
    }

    [DisallowMultipleComponent]
    public sealed class QuestMicrophoneRecorder : MonoBehaviour, IMicrophoneRecordingService
    {
        [SerializeField, Range(8000, 48000)] private int requestedSampleRate = 48000;
        [SerializeField, Range(1f, 60f)] private float maximumDuration = 60f;
        [SerializeField, Range(0.05f, 2f)] private float minimumDuration = 0.15f;
        [SerializeField, Range(1f, 10f)] private float startTimeout = 5f;
        [Header("Live Metering")]
        [SerializeField, Range(5f, 30f)] private float meterUpdatesPerSecond = 12f;
        [SerializeField, Range(256, 4096)] private int meterWindowFrames = 2048;
        [SerializeField, Range(0.01f, 0.5f)] private float meterSmoothingSeconds = 0.12f;
        [SerializeField, Range(0.0001f, 0.05f)] private float meaningfulAudioRmsThreshold = 0.001f;
        [Header("Development Playback")]
        [SerializeField] private bool enableDevelopmentPlayback;

        private AudioClip captureClip;
        private string selectedDevice;
        private float stateEnteredAt;
        private string sessionIdentifier;
        private int operationVersion;
        private float[] meterSamples;
        private float nextMeterUpdateTime;
        private int previousMicrophonePosition;
        private int capturedSampleFrames;
        private AudioSource developmentPlaybackSource;
        private bool maximumDurationReached;

        public MicrophoneRecordingState State { get; private set; } = MicrophoneRecordingState.Idle;
        public string LastError { get; private set; } = string.Empty;
        public float LastRms { get; private set; }
        public float LiveRms { get; private set; }
        public float LivePeak { get; private set; }
        public float LastPeak { get; private set; }
        public string LastStatus { get; private set; } = "Idle";
        public CompletedMicrophoneRecording? LastCompletedRecording { get; private set; }
        public int CurrentSampleRate => captureClip != null ? captureClip.frequency : 0;
        public int CurrentChannels => captureClip != null ? captureClip.channels : 0;
        public int CurrentSampleFrames => IsCaptureActive ? capturedSampleFrames : 0;
        public float ElapsedSeconds => State == MicrophoneRecordingState.Recording ? Time.unscaledTime - stateEnteredAt : 0f;
        public bool IsCaptureActive => State == MicrophoneRecordingState.Starting || State == MicrophoneRecordingState.Recording;
        public bool IsRecording => State == MicrophoneRecordingState.Recording;
        public string SelectedDeviceLabel => string.IsNullOrEmpty(selectedDevice) ? "System default" : selectedDevice;
        public string PermissionStatus => HasPermission() ? "Granted" : "Not granted";

        public event Action RecordingStarted;
        public event Action<CompletedMicrophoneRecording> RecordingCompleted;
        public event Action<string> RecordingFailed;
        public event Action<MicrophoneRecordingState> StateChanged;

        public void SetSessionIdentifier(string value) => sessionIdentifier = value ?? string.Empty;

        public bool DiscardLastCompletedRecording(AudioClip expectedClip = null)
        {
            if (!LastCompletedRecording.HasValue) return false;
            AudioClip clip = LastCompletedRecording.Value.Clip;
            if (expectedClip != null && clip != expectedClip) return false;
            LastCompletedRecording = null;
            LastRms = LastPeak = 0f;
            if (clip != null) Destroy(clip);
            LastStatus = "Completed recording discarded";
            return true;
        }

        public bool BeginRecording()
        {
            if (State != MicrophoneRecordingState.Idle && State != MicrophoneRecordingState.Error) return false;
            LastError = string.Empty;
            LastStatus = "Preparing microphone";
            maximumDurationReached = false;
            int requestVersion = ++operationVersion;
            if (!HasPermission())
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                SetState(MicrophoneRecordingState.RequestingPermission);
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => HandlePermissionGranted(requestVersion);
                callbacks.PermissionDenied += _ => HandlePermissionDenied(requestVersion, false);
                callbacks.PermissionDeniedAndDontAskAgain += _ => HandlePermissionDenied(requestVersion, true);
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
                return true;
#else
                Fail("Microphone permission is unavailable on this platform.");
                return false;
#endif
            }
            return StartCapture();
        }

        private void HandlePermissionGranted(int requestVersion)
        {
            if (requestVersion == operationVersion && State == MicrophoneRecordingState.RequestingPermission)
                StartCapture();
        }

        private void HandlePermissionDenied(int requestVersion, bool permanently)
        {
            if (requestVersion != operationVersion) return;
            Fail(permanently
                ? "Microphone permission denied permanently; enable it in Quest app permissions."
                : "Microphone permission denied.");
        }

        private bool StartCapture()
        {
            try
            {
                string[] devices = Microphone.devices;
                selectedDevice = devices != null && devices.Length > 0 ? devices[0] : null;
                capturedSampleFrames = previousMicrophonePosition = 0;
                LiveRms = LivePeak = 0f;
                SetState(MicrophoneRecordingState.Starting);
                LastStatus = "Starting microphone";
                // Keep one extra second in the looping buffer so the 60-second safety stop
                // occurs before capture data can overwrite its beginning.
                captureClip = Microphone.Start(selectedDevice, true,
                    Mathf.CeilToInt(maximumDuration) + 1, requestedSampleRate);
                if (captureClip == null) { Fail("Unity Microphone.Start returned no AudioClip."); return false; }
                return true;
            }
            catch (Exception ex) { Fail("Microphone start failed: " + ex.Message); return false; }
        }

        private void Update()
        {
            if (State == MicrophoneRecordingState.Starting)
            {
                int position = Microphone.GetPosition(selectedDevice);
                if (position > 0)
                {
                    previousMicrophonePosition = position;
                    capturedSampleFrames = position;
                    EnsureMeterBuffer();
                    SetState(MicrophoneRecordingState.Recording);
                    LastStatus = "Recording started";
                    RecordingStarted?.Invoke();
                }
                else if (Time.unscaledTime - stateEnteredAt >= startTimeout)
                    Fail("Microphone did not begin producing samples before the start timeout.");
            }
            else if (State == MicrophoneRecordingState.Recording)
            {
                UpdateCapturedFrameCount();
                UpdateLiveMeter();
                if (Time.unscaledTime - stateEnteredAt >= maximumDuration)
                {
                    maximumDurationReached = true;
                    LastStatus = "Maximum recording duration reached";
                    StopRecording();
                }
                else if (!Microphone.IsRecording(selectedDevice))
                {
                    LastStatus = "Microphone stopped unexpectedly; finalizing available audio";
                    StopRecording();
                }
            }
        }

        private void UpdateCapturedFrameCount()
        {
            if (captureClip == null) return;
            int position = Mathf.Max(0, Microphone.GetPosition(selectedDevice));
            int delta = position >= previousMicrophonePosition
                ? position - previousMicrophonePosition
                : captureClip.samples - previousMicrophonePosition + position;
            if (delta > 0) capturedSampleFrames = Mathf.Min(captureClip.samples, capturedSampleFrames + delta);
            previousMicrophonePosition = position;
        }

        private void EnsureMeterBuffer()
        {
            if (captureClip == null) return;
            int length = Mathf.Min(meterWindowFrames, captureClip.samples) * captureClip.channels;
            if (meterSamples == null || meterSamples.Length != length) meterSamples = new float[length];
        }

        private void UpdateLiveMeter()
        {
            if (captureClip == null || Time.unscaledTime < nextMeterUpdateTime) return;
            nextMeterUpdateTime = Time.unscaledTime + 1f / meterUpdatesPerSecond;
            EnsureMeterBuffer();
            int microphonePosition = Mathf.Max(0, Microphone.GetPosition(selectedDevice));
            int windowFrames = meterSamples.Length / captureClip.channels;
            int startFrame = microphonePosition - windowFrames;
            if (startFrame < 0) startFrame += captureClip.samples;
            if (!captureClip.GetData(meterSamples, startFrame)) return;

            double sum = 0d;
            float peak = 0f;
            for (int i = 0; i < meterSamples.Length; i++)
            {
                float absolute = Mathf.Abs(meterSamples[i]);
                if (absolute > peak) peak = absolute;
                sum += meterSamples[i] * meterSamples[i];
            }
            float rms = Mathf.Sqrt((float)(sum / meterSamples.Length));
            float blend = 1f - Mathf.Exp(-(1f / meterUpdatesPerSecond) / meterSmoothingSeconds);
            LiveRms = Mathf.Lerp(LiveRms, rms, blend);
            LivePeak = Mathf.Max(peak, Mathf.Lerp(LivePeak, peak, blend * 0.45f));
        }

        public void StopRecording()
        {
            if (!IsCaptureActive) return;
            // A stopped device often reports position zero. Do not interpret that reset as
            // a circular-buffer wrap and accidentally finalize the entire backing clip.
            if (Microphone.IsRecording(selectedDevice)) UpdateCapturedFrameCount();
            int frames = capturedSampleFrames;
            AudioClip source = captureClip;
            if (frames <= 0 && State == MicrophoneRecordingState.Recording
                && Time.unscaledTime - stateEnteredAt >= maximumDuration - 0.1f && source != null)
                frames = source.samples;
            SetState(MicrophoneRecordingState.Stopping);
            if (Microphone.IsRecording(selectedDevice)) Microphone.End(selectedDevice);
            captureClip = null;
            LiveRms = LivePeak = 0f;

            if (source == null || frames <= 0) { Fail("Recording contained no microphone samples."); return; }
            frames = Mathf.Min(frames, source.samples);
            float duration = frames / (float)source.frequency;
            if (duration < minimumDuration) { Destroy(source); Fail("Recording was too short to use."); return; }

            float[] samples = new float[frames * source.channels];
            if (!source.GetData(samples, 0)) { Destroy(source); Fail("Could not read recorded microphone samples."); return; }
            AudioClip trimmed = AudioClip.Create("Quest Microphone Recording", frames, source.channels, source.frequency, false);
            trimmed.SetData(samples, 0);
            double sum = 0d;
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float absolute = Mathf.Abs(samples[i]);
                if (absolute > peak) peak = absolute;
                sum += samples[i] * samples[i];
            }
            float rms = samples.Length > 0 ? Mathf.Sqrt((float)(sum / samples.Length)) : 0f;
            LastRms = rms;
            LastPeak = peak;
            Destroy(source);
            if (LastCompletedRecording.HasValue && LastCompletedRecording.Value.Clip != null)
                Destroy(LastCompletedRecording.Value.Clip);
            bool meaningful = rms >= meaningfulAudioRmsThreshold && peak > meaningfulAudioRmsThreshold;
            var result = new CompletedMicrophoneRecording(trimmed, frames, trimmed.frequency,
                trimmed.channels, duration, rms, peak, meaningful, sessionIdentifier);
            LastCompletedRecording = result;
            string completionPrefix = maximumDurationReached
                ? "Maximum recording duration reached; recording finalized"
                : "Recording stopped successfully";
            LastStatus = completionPrefix + (meaningful ? "; audio detected" : "; audio level was near silent");
            SetState(MicrophoneRecordingState.Idle);
            RecordingCompleted?.Invoke(result);
        }

        public void CancelRecording(string reason = null)
        {
            bool hadOperation = State != MicrophoneRecordingState.Idle && State != MicrophoneRecordingState.Error;
            operationVersion++;
            if (IsCaptureActive && Microphone.IsRecording(selectedDevice)) Microphone.End(selectedDevice);
            if (captureClip != null) Destroy(captureClip);
            captureClip = null;
            LiveRms = LivePeak = 0f;
            LastError = reason ?? string.Empty;
            if (!string.IsNullOrEmpty(reason)) LastStatus = "Recording cancelled: " + reason;
            SetState(MicrophoneRecordingState.Idle);
            if (hadOperation && !string.IsNullOrEmpty(reason)) RecordingFailed?.Invoke(reason);
        }

        private void OnApplicationPause(bool paused) { if (paused && IsCaptureActive) CancelRecording("Recording cancelled while the application was paused."); }
        private void OnApplicationFocus(bool focused) { if (!focused && IsCaptureActive) CancelRecording("Recording cancelled when application focus was lost."); }
        private void OnDisable() => CancelRecording();

        private void OnDestroy()
        {
            if (LastCompletedRecording.HasValue && LastCompletedRecording.Value.Clip != null)
                Destroy(LastCompletedRecording.Value.Clip);
        }

        private static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            return Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
        }

        private void Fail(string message)
        {
            if (IsCaptureActive && Microphone.IsRecording(selectedDevice)) Microphone.End(selectedDevice);
            if (captureClip != null) Destroy(captureClip);
            captureClip = null;
            LiveRms = LivePeak = 0f;
            LastError = message;
            LastStatus = message;
            SetState(MicrophoneRecordingState.Error);
            Debug.LogError(message, this);
            RecordingFailed?.Invoke(message);
        }

        private void SetState(MicrophoneRecordingState next)
        {
            State = next;
            stateEnteredAt = Time.unscaledTime;
            StateChanged?.Invoke(next);
        }

        public string GetDiagnosticSummary()
        {
            return "Mic: " + State + "   Permission: " + PermissionStatus + "   Device: " + SelectedDeviceLabel
                + "\nElapsed: " + ElapsedSeconds.ToString("0.0") + "s   Rate: " + CurrentSampleRate
                + "   Channels: " + CurrentChannels + "   Frames: " + CurrentSampleFrames
                + "\nLIVE RMS: " + LiveRms.ToString("0.0000") + "   Peak: " + LivePeak.ToString("0.0000")
                + (LastCompletedRecording.HasValue
                    ? "\nCOMPLETED Duration: " + LastCompletedRecording.Value.DurationSeconds.ToString("0.00")
                        + "s   Frames: " + LastCompletedRecording.Value.SampleFrames
                        + "   Rate: " + LastCompletedRecording.Value.SampleRate
                        + "   Channels: " + LastCompletedRecording.Value.Channels
                        + "   RMS: " + LastRms.ToString("0.0000") + "   Peak: " + LastPeak.ToString("0.0000")
                        + "   Audio: " + (LastCompletedRecording.Value.ContainsMeaningfulAudio ? "detected" : "near silent")
                    : "\nCOMPLETED: none")
                + "\nStatus: " + LastStatus
                + (string.IsNullOrEmpty(LastError) ? string.Empty : "\nMic error: " + LastError);
        }

        [ContextMenu("Development/Play Last Completed Recording")]
        public void PlayLastCompletedRecording()
        {
            if (!enableDevelopmentPlayback || IsCaptureActive || !LastCompletedRecording.HasValue
                || LastCompletedRecording.Value.Clip == null) return;
            if (developmentPlaybackSource == null)
            {
                developmentPlaybackSource = gameObject.AddComponent<AudioSource>();
                developmentPlaybackSource.playOnAwake = false;
                developmentPlaybackSource.loop = false;
                developmentPlaybackSource.spatialBlend = 0f;
            }
            developmentPlaybackSource.clip = LastCompletedRecording.Value.Clip;
            developmentPlaybackSource.Play();
        }
    }
}
