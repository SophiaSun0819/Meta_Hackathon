using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ThinkingSpace.Speech
{
    public enum TranscriptionRequestState { Idle, Encoding, Uploading, Completed, Failed, Cancelled }

    public sealed class SpeechTranscriptionRequest
    {
        public byte[] WavData;
        public string RecordingIdentifier;
        public string SessionIdentifier;
        public float RecordingDuration;
        public int SampleRate;
    }

    public sealed class SpeechTranscriptionResult
    {
        public bool Success;
        public string Transcript;
        public string Error;
        public string RecordingIdentifier;
        public string SessionIdentifier;
        public string Model;
        public long HttpStatus;
        public float RequestDurationSeconds;
    }

    public interface ISpeechTranscriptionService
    {
        bool IsBusy { get; }
        IEnumerator Transcribe(SpeechTranscriptionRequest request, Action<SpeechTranscriptionResult> completed);
        void Cancel();
    }

    [DisallowMultipleComponent]
    public sealed class SpeechTranscriptionService : MonoBehaviour, ISpeechTranscriptionService
    {
        [SerializeField, Tooltip("Hosted deployments must use HTTPS. For Quest development, use an HTTPS tunnel to your LAN backend.")]
        private string backendUrl = "";
        [SerializeField, Range(5, 120)] private int requestTimeoutSeconds = 45;

        private UnityWebRequest activeRequest;
        public bool IsBusy => activeRequest != null;
        public string BackendUrl => backendUrl;

        [Serializable]
        private sealed class BackendResponse
        {
            public string transcript;
            public string request_id;
            public string session_id;
            public string model;
        }

        [Serializable]
        private sealed class BackendError { public string error; public string detail; public string request_id; }

        public IEnumerator Transcribe(SpeechTranscriptionRequest request, Action<SpeechTranscriptionResult> completed)
        {
            if (IsBusy)
            {
                completed?.Invoke(Failure(request, "A transcription request is already active.", 0, 0f));
                yield break;
            }
            if (request == null || request.WavData == null || request.WavData.Length <= 44)
            {
                completed?.Invoke(Failure(request, "The WAV request is empty or invalid.", 0, 0f));
                yield break;
            }
            if (string.IsNullOrWhiteSpace(backendUrl))
            {
                completed?.Invoke(Failure(request, "Backend URL is not configured.", 0, 0f));
                yield break;
            }
            if (!backendUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !IsPrivateDevelopmentUrl(backendUrl))
            {
                completed?.Invoke(Failure(request, "Backend URL must use HTTPS outside private-network development.", 0, 0f));
                yield break;
            }

            float startedAt = Time.realtimeSinceStartup;
            activeRequest = new UnityWebRequest(backendUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(request.WavData),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = requestTimeoutSeconds
            };
            activeRequest.SetRequestHeader("Content-Type", "audio/wav");
            activeRequest.SetRequestHeader("X-Recording-ID", request.RecordingIdentifier ?? string.Empty);
            activeRequest.SetRequestHeader("X-Session-ID", request.SessionIdentifier ?? string.Empty);
            activeRequest.SetRequestHeader("X-Sample-Rate", request.SampleRate.ToString());
            activeRequest.SetRequestHeader("X-Recording-Duration", request.RecordingDuration.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));

            UnityWebRequest requestInFlight = activeRequest;
            yield return requestInFlight.SendWebRequest();
            float elapsed = Time.realtimeSinceStartup - startedAt;
            activeRequest = null;

            if (requestInFlight.result == UnityWebRequest.Result.Success)
            {
                BackendResponse response = JsonUtility.FromJson<BackendResponse>(requestInFlight.downloadHandler.text);
                if (response == null || string.IsNullOrWhiteSpace(response.transcript))
                    completed?.Invoke(Failure(request, "Backend returned an empty transcript.", requestInFlight.responseCode, elapsed));
                else
                    completed?.Invoke(new SpeechTranscriptionResult
                    {
                        Success = true,
                        Transcript = response.transcript,
                        RecordingIdentifier = string.IsNullOrEmpty(response.request_id) ? request.RecordingIdentifier : response.request_id,
                        SessionIdentifier = string.IsNullOrEmpty(response.session_id) ? request.SessionIdentifier : response.session_id,
                        Model = response.model,
                        HttpStatus = requestInFlight.responseCode,
                        RequestDurationSeconds = elapsed
                    });
            }
            else
            {
                string message = requestInFlight.error;
                if (!string.IsNullOrEmpty(requestInFlight.downloadHandler.text))
                {
                    BackendError backendError = JsonUtility.FromJson<BackendError>(requestInFlight.downloadHandler.text);
                    if (backendError != null)
                    {
                        if (!string.IsNullOrEmpty(backendError.error)) message = backendError.error;
                        else if (!string.IsNullOrEmpty(backendError.detail)) message = backendError.detail;
                    }
                }
                completed?.Invoke(Failure(request, "Transcription failed: " + message, requestInFlight.responseCode, elapsed));
            }
            requestInFlight.Dispose();
        }

        public void Cancel()
        {
            if (activeRequest == null) return;
            UnityWebRequest request = activeRequest;
            activeRequest = null;
            request.Abort();
            request.Dispose();
        }

        private static SpeechTranscriptionResult Failure(
            SpeechTranscriptionRequest request, string error, long status, float elapsed)
        {
            return new SpeechTranscriptionResult
            {
                Success = false, Error = error,
                RecordingIdentifier = request != null ? request.RecordingIdentifier : string.Empty,
                SessionIdentifier = request != null ? request.SessionIdentifier : string.Empty,
                HttpStatus = status, RequestDurationSeconds = elapsed
            };
        }

        private static bool IsPrivateDevelopmentUrl(string url)
        {
            return url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://10.", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://192.168.", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://172.16.", StringComparison.OrdinalIgnoreCase);
        }

        private void OnDisable() => Cancel();
    }
}
