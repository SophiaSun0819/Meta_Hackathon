using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ThinkingSpace.Pairing
{
    /// Network adapter only. It is not present in the active scene and cannot enable live pairing by itself.
    [DisallowMultipleComponent]
    public sealed class Backend034UnityHttpExecutor : MonoBehaviour, IBackend034HttpExecutor
    {
        [SerializeField] private string approvedOrigin = string.Empty;
        [SerializeField, Range(5, 60)] private int timeoutSeconds = 20;

        public void Configure(string origin)
        {
            approvedOrigin = ValidateOrigin(origin).AbsoluteUri.TrimEnd('/');
        }

        public void Send(Backend034HttpRequest request, Action<Backend034HttpResponse> completed)
        {
            if (completed == null) throw new ArgumentNullException(nameof(completed));
            Uri origin;
            try { origin = ValidateOrigin(approvedOrigin); }
            catch (ArgumentException) { completed(new Backend034HttpResponse(0, "{\"error\":\"CONTRACT_UNSUPPORTED\"}")); return; }
            if (string.IsNullOrEmpty(request.Path) || request.Path[0] != '/' || request.Path.Contains(".."))
            { completed(new Backend034HttpResponse(0, "{\"error\":\"CONTRACT_UNSUPPORTED\"}")); return; }
            StartCoroutine(SendRoutine(new Uri(origin, request.Path), request, completed));
        }

        public static Uri ValidateOrigin(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("PAIRING_ORIGIN_INVALID");
            bool loopback = uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp;
            if (uri.Scheme != Uri.UriSchemeHttps && !loopback) throw new ArgumentException("PAIRING_ORIGIN_INVALID");
            return uri;
        }

        private IEnumerator SendRoutine(Uri uri, Backend034HttpRequest input, Action<Backend034HttpResponse> completed)
        {
            using (var request = new UnityWebRequest(uri, input.Method))
            {
                if (input.Body != null)
                {
                    byte[] bytes = new UTF8Encoding(false, true).GetBytes(input.Body);
                    if (bytes.Length > JudgeAccountSyncContract.MaximumRequestBytes)
                    { completed(new Backend034HttpResponse(0, "{\"error\":\"SYNC_BODY_INVALID\"}")); yield break; }
                    request.uploadHandler = new UploadHandlerRaw(bytes);
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Accept", "application/json");
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();
                string body = request.downloadHandler?.text ?? string.Empty;
                if (Encoding.UTF8.GetByteCount(body) > JudgeAccountSyncContract.MaximumRequestBytes)
                    body = "{\"error\":\"CONTRACT_UNSUPPORTED\"}";
                completed(new Backend034HttpResponse(request.responseCode, body, RetryAfterMilliseconds(request)));
            }
        }

        private static int RetryAfterMilliseconds(UnityWebRequest request)
        {
            string value = request.GetResponseHeader("Retry-After");
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
                return Math.Min(300000, seconds * 1000);
            return 0;
        }
    }
}
