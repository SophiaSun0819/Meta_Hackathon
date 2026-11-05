using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.Speech
{
    public enum MicrophoneStatusDisplayState
    { MicOff, Idle, SpeechDetected, FinishingThought, Thinking, Speaking, Paused }

    [DisallowMultipleComponent]
    public sealed class ConversationalListeningHud : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float notificationDuration = 1.5f;
        private Canvas canvas;
        private TMP_Text statusText, timerText, notificationText;
        private Image levelFill, peakMarker;
        private CanvasGroup notificationGroup;
        private float notificationStartedAt;
        private bool notificationActive, listeningVisible, initializedMode;

        public void Configure(Transform orby)
        {
            if (canvas == null) Build();
            if (orby != null && canvas.transform.parent != orby)
            {
                canvas.transform.SetParent(orby, false);
                canvas.transform.localPosition = new Vector3(0f, -0.17f, 0f);
                canvas.transform.localRotation = Quaternion.identity;
            }
        }

        public void SetListeningMode(bool enabled, bool showNotification)
        {
            if (canvas == null) Build();
            if (initializedMode && listeningVisible == enabled) return;
            listeningVisible = enabled;
            initializedMode = true;
            canvas.gameObject.SetActive(true);
            if (showNotification) ShowNotification(enabled ? "Mic On" : "Mic Off");
            if (!enabled) SetStatus(MicrophoneStatusDisplayState.MicOff, 0f, 0f, 0f);
        }

        public void SetStatus(MicrophoneStatusDisplayState state, float rms, float peak,
            float utteranceElapsed)
        {
            if (canvas == null) Build();
            if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
            string status = state == MicrophoneStatusDisplayState.MicOff ? "Mic Off"
                : state == MicrophoneStatusDisplayState.Idle ? "Mic On · Idle"
                : state == MicrophoneStatusDisplayState.SpeechDetected ? "Speech Detected"
                : state == MicrophoneStatusDisplayState.FinishingThought ? "Finishing Thought"
                : state == MicrophoneStatusDisplayState.Thinking ? "Thinking"
                : state == MicrophoneStatusDisplayState.Speaking ? "Speaking"
                : "Paused";
            statusText.text = status;
            statusText.color = state == MicrophoneStatusDisplayState.MicOff
                ? new Color(1f, 0.35f, 0.35f) : Color.white;
            timerText.text = FormatTime(utteranceElapsed);
            levelFill.fillAmount = Mathf.Clamp01(rms * 24f);
            float peakValue = Mathf.Clamp01(peak * 18f);
            RectTransform marker = peakMarker.rectTransform;
            marker.anchorMin = new Vector2(peakValue, 0f);
            marker.anchorMax = new Vector2(peakValue, 1f);
        }

        public static MicrophoneStatusDisplayState ResolveDisplayState(ConversationalListeningState listening,
            ThinkingSpace.Companion.OrbyConversationState orby, bool enabled, RecordingWorkflowState workflow)
        {
            if (orby == ThinkingSpace.Companion.OrbyConversationState.Speaking)
                return MicrophoneStatusDisplayState.Speaking;
            if (orby == ThinkingSpace.Companion.OrbyConversationState.Thinking
                || workflow == RecordingWorkflowState.Transcribing)
                return MicrophoneStatusDisplayState.Thinking;
            if (!enabled) return MicrophoneStatusDisplayState.MicOff;
            if (listening == ConversationalListeningState.SpeechActive)
                return MicrophoneStatusDisplayState.SpeechDetected;
            if (listening == ConversationalListeningState.SilenceCountdown
                || listening == ConversationalListeningState.Finalizing)
                return MicrophoneStatusDisplayState.FinishingThought;
            if (listening == ConversationalListeningState.Suspended
                || workflow != RecordingWorkflowState.Ready)
                return MicrophoneStatusDisplayState.Paused;
            return MicrophoneStatusDisplayState.Idle;
        }

        private void ShowNotification(string message)
        {
            notificationText.text = message;
            notificationStartedAt = Time.unscaledTime;
            notificationActive = true;
            notificationGroup.alpha = 0f;
            notificationGroup.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!notificationActive) return;
            float elapsed = Time.unscaledTime - notificationStartedAt;
            float fade = 0.2f;
            float alpha = elapsed < fade ? elapsed / fade
                : elapsed > notificationDuration - fade
                    ? Mathf.Clamp01((notificationDuration - elapsed) / fade) : 1f;
            notificationGroup.alpha = alpha;
            if (elapsed < notificationDuration) return;
            notificationActive = false;
            notificationGroup.gameObject.SetActive(false);
            if (!listeningVisible) SetStatus(MicrophoneStatusDisplayState.MicOff, 0f, 0f, 0f);
        }

        private void Build()
        {
            GameObject root = new GameObject("Conversational Microphone HUD", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 75;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300f, 82f);
            rect.localScale = Vector3.one * 0.0005f;

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.02f, 0.04f, 0.07f, 0.82f);
            background.raycastTarget = false;
            statusText = Text("Status", rect, 24f, FontStyles.Bold, TextAlignmentOptions.Left,
                new Vector2(0.05f, 0.54f), new Vector2(0.72f, 0.94f));
            timerText = Text("Timer", rect, 22f, FontStyles.Normal, TextAlignmentOptions.Right,
                new Vector2(0.72f, 0.54f), new Vector2(0.95f, 0.94f));

            RectTransform meter = Rect("Audio Meter", rect, new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.46f));
            Image meterBack = meter.gameObject.AddComponent<Image>();
            meterBack.color = new Color(0.08f, 0.12f, 0.17f, 1f); meterBack.raycastTarget = false;
            RectTransform fill = Rect("Live Level", meter, Vector2.zero, Vector2.one);
            levelFill = fill.gameObject.AddComponent<Image>();
            levelFill.color = new Color(0.15f, 0.9f, 0.65f, 1f);
            levelFill.type = Image.Type.Filled; levelFill.fillMethod = Image.FillMethod.Horizontal;
            levelFill.raycastTarget = false;
            RectTransform peak = Rect("Peak", meter, Vector2.zero, new Vector2(0.012f, 1f));
            peakMarker = peak.gameObject.AddComponent<Image>();
            peakMarker.color = Color.white; peakMarker.raycastTarget = false;

            GameObject notice = new GameObject("Mic Notification", typeof(RectTransform));
            notice.transform.SetParent(root.transform, false);
            RectTransform noticeRect = notice.GetComponent<RectTransform>();
            noticeRect.anchorMin = new Vector2(0.25f, 1.08f); noticeRect.anchorMax = new Vector2(0.75f, 1.62f);
            noticeRect.offsetMin = noticeRect.offsetMax = Vector2.zero;
            Image noticeBack = notice.AddComponent<Image>();
            noticeBack.color = new Color(0.03f, 0.12f, 0.18f, 0.94f); noticeBack.raycastTarget = false;
            notificationGroup = notice.AddComponent<CanvasGroup>();
            notificationGroup.interactable = false; notificationGroup.blocksRaycasts = false;
            notificationText = Text("Notification Text", noticeRect, 30f, FontStyles.Bold,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            notice.SetActive(false);
            root.SetActive(false);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        private static TMP_Text Text(string name, Transform parent, float size, FontStyles style,
            TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(name, parent, min, max);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset; text.fontSize = size; text.fontStyle = style;
            text.alignment = alignment; text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            GameObject value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
