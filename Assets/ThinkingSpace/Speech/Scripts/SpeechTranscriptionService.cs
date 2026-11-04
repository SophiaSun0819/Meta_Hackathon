using System;
using System.Collections;
using System.IO;
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
        [Header("Development Authentication")]
        [SerializeField, Tooltip("Environment variable used in the Editor or desktop development builds.")]
        private string developmentTokenEnvironmentVariable = "THINKING_SPACE_DEV_TOKEN";
        [SerializeField, Tooltip("Token filename under Application.persistentDataPath/ThinkingSpace on Quest.")]
        private string developmentTokenFileName = "dev-token.txt";

        private UnityWebRequest activeRequest;
        private string runtimeDevelopmentToken;
        public bool IsBusy => activeRequest != null;
        public string BackendUrl => backendUrl;
        public string BackendBaseUrl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(backendUrl)) return string.Empty;
                int api = backendUrl.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
                string value = api >= 0 ? backendUrl.Substring(0, api + 1) : backendUrl.TrimEnd('/') + "/";
                return value;
            }
        }

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
            string developmentToken = ResolveDevelopmentToken();
            if (string.IsNullOrEmpty(developmentToken))
            {
                completed?.Invoke(Failure(request,
                    "Development authentication token is not configured on this device.", 0, 0f));
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
            activeRequest.SetRequestHeader("X-ThinkingSpace-Token", developmentToken);

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

        public void SetDevelopmentTokenForCurrentRun(string token)
        {
            runtimeDevelopmentToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        public string GetDevelopmentTokenForCurrentRun() => ResolveDevelopmentToken();

        public string DevelopmentTokenPath => Path.Combine(
            Application.persistentDataPath, "ThinkingSpace", developmentTokenFileName);

        private string ResolveDevelopmentToken()
        {
            if (!string.IsNullOrEmpty(runtimeDevelopmentToken)) return runtimeDevelopmentToken;
            string fromEnvironment = Environment.GetEnvironmentVariable(developmentTokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return runtimeDevelopmentToken = fromEnvironment.Trim();
            try
            {
                string path = DevelopmentTokenPath;
                if (File.Exists(path))
                {
                    string fromFile = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(fromFile)) return runtimeDevelopmentToken = fromFile;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Thinking Space development token file could not be read: "
                    + ex.GetType().Name, this);
            }
            return null;
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
