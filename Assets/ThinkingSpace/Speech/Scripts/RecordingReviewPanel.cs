using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class RecordingReviewPanel : MonoBehaviour
    {
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.03f, 0.62f);
        [SerializeField] private Vector2 panelSize = new Vector2(560f, 430f);
        [SerializeField, Min(0.0001f)] private float canvasScale = 0.0005f;

        private Canvas canvas;
        private Transform headset;
        private TMP_Text durationText;
        private TMP_Text playbackTimeText;
        private TMP_Text exportStatusText;
        private Image playbackProgressFill;
        private float recordingDuration;
        private TMP_Text playLabel;
        private Button playButton, sendButton, discardButton, rerecordButton, exportButton;
        private Action play, send, discard, rerecord, export;

        private void Awake() { BuildPanel(); Hide(); }
        public void SetHeadset(Transform value) => headset = value;

        public void Show(float duration, Action onPlay, Action onSend, Action onDiscard, Action onRerecord)
        {
            play = onPlay; send = onSend; discard = onDiscard; rerecord = onRerecord;
            recordingDuration = Mathf.Max(0f, duration);
            durationText.text = "Total  " + FormatDuration(recordingDuration);
            SetPlaybackProgress(0f, recordingDuration);
            SetExportStatus(string.Empty);
            SetPlaying(false);
            SetButtonsInteractable(true);
            canvas.gameObject.SetActive(true);
            PositionAtHeadset();
        }

        public void ConfigureDevelopmentExport(bool visible, Action onExport)
        {
            export = onExport;
            if (exportButton != null)
            {
                exportButton.gameObject.SetActive(visible && onExport != null);
                exportButton.interactable = visible && onExport != null;
            }
        }

        public void SetPlaying(bool value)
        {
            if (playLabel != null) playLabel.text = value ? "Stop Playback" : "Play Recording";
        }

        public void SetPlaybackProgress(float elapsed, float total)
        {
            recordingDuration = Mathf.Max(0f, total);
            float current = Mathf.Clamp(elapsed, 0f, recordingDuration);
            float remaining = Mathf.Max(0f, recordingDuration - current);
            if (playbackProgressFill != null)
                playbackProgressFill.fillAmount = recordingDuration > 0f ? current / recordingDuration : 0f;
            if (playbackTimeText != null)
                playbackTimeText.text = FormatDuration(current) + " elapsed     -" + FormatDuration(remaining) + " remaining";
            if (durationText != null) durationText.text = "Total  " + FormatDuration(recordingDuration);
        }

        public void SetExportStatus(string message)
        {
            if (exportStatusText == null) return;
            exportStatusText.text = message ?? string.Empty;
            exportStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        public void SetButtonsInteractable(bool value)
        {
            if (playButton != null) playButton.interactable = value;
            if (sendButton != null) sendButton.interactable = value;
            if (discardButton != null) discardButton.interactable = value;
            if (rerecordButton != null) rerecordButton.interactable = value;
            if (exportButton != null && exportButton.gameObject.activeSelf) exportButton.interactable = value;
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (canvas != null && canvas.gameObject.activeSelf) PositionAtHeadset();
        }

        private void PositionAtHeadset()
        {
            if (headset == null || canvas == null) return;
            canvas.transform.position = headset.TransformPoint(headsetLocalPosition);
            Vector3 forward = canvas.transform.position - headset.position;
            if (forward.sqrMagnitude > 0.0001f)
                canvas.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void BuildPanel()
        {
            if (canvas != null) return;
            EnsureMetaEventSystem();
            GameObject root = new GameObject("Recording Review Panel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 61;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            root.AddComponent<GraphicRaycaster>();
            PointableCanvas pointable = root.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(canvas);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = panelSize;
            rect.localScale = Vector3.one * canvasScale;
            Image background = root.AddComponent<Image>();
            background.color = new Color(0.035f, 0.055f, 0.09f, 0.98f);
            background.raycastTarget = false;
            PlaneSurface plane = root.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            BoundsClipper clipper = root.AddComponent<BoundsClipper>();
            clipper.Size = new Vector3(panelSize.x, panelSize.y, 0.01f);
            ClippedPlaneSurface clipped = root.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { clipper });
            RayInteractable ray = root.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped); ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke = root.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped); poke.InjectOptionalPointableElement(pointable);

            TMP_Text heading = CreateText("Heading", rect, "Recording Complete", 40f, FontStyles.Bold);
            SetAnchors(heading.rectTransform, new Vector2(0.06f, 0.85f), new Vector2(0.94f, 0.97f));
            durationText = CreateText("Duration", rect, "Duration  00:00", 27f, FontStyles.Normal);
            SetAnchors(durationText.rectTransform, new Vector2(0.08f, 0.76f), new Vector2(0.92f, 0.85f));

            RectTransform progressBackground = CreateRect("Playback Progress", rect);
            SetAnchors(progressBackground, new Vector2(0.08f, 0.69f), new Vector2(0.92f, 0.73f));
            Image progressBackgroundImage = progressBackground.gameObject.AddComponent<Image>();
            progressBackgroundImage.color = new Color(0.12f, 0.18f, 0.26f, 1f);
            progressBackgroundImage.raycastTarget = false;
            RectTransform progressFill = CreateRect("Fill", progressBackground);
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = Vector2.one;
            progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
            playbackProgressFill = progressFill.gameObject.AddComponent<Image>();
            playbackProgressFill.color = new Color(0.08f, 0.72f, 0.92f, 1f);
            playbackProgressFill.type = Image.Type.Filled;
            playbackProgressFill.fillMethod = Image.FillMethod.Horizontal;
            playbackProgressFill.fillOrigin = 0;
            playbackProgressFill.fillAmount = 0f;
            playbackProgressFill.raycastTarget = false;
            playbackTimeText = CreateText("Playback Times", rect, "00:00 elapsed     -00:00 remaining", 22f, FontStyles.Normal);
            SetAnchors(playbackTimeText.rectTransform, new Vector2(0.08f, 0.61f), new Vector2(0.92f, 0.68f));

            exportStatusText = CreateText("Export Status", rect, string.Empty, 18f, FontStyles.Normal);
            exportStatusText.color = new Color(0.55f, 0.9f, 1f, 1f);
            exportStatusText.textWrappingMode = TextWrappingModes.Normal;
            SetAnchors(exportStatusText.rectTransform, new Vector2(0.06f, 0.49f), new Vector2(0.94f, 0.61f));
            exportStatusText.gameObject.SetActive(false);

            playButton = CreateButton("Play Recording", rect, new Vector2(0.07f, 0.31f), new Vector2(0.48f, 0.47f), out playLabel);
            sendButton = CreateButton("Send to AI", rect, new Vector2(0.52f, 0.31f), new Vector2(0.93f, 0.47f), out _);
            discardButton = CreateButton("Discard", rect, new Vector2(0.07f, 0.12f), new Vector2(0.48f, 0.28f), out _);
            rerecordButton = CreateButton("Rerecord", rect, new Vector2(0.52f, 0.12f), new Vector2(0.93f, 0.28f), out _);
            exportButton = CreateButton("Export WAV (Dev)", rect, new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.1f), out _);
            exportButton.gameObject.SetActive(false);
            playButton.onClick.AddListener(() => play?.Invoke());
            sendButton.onClick.AddListener(() => send?.Invoke());
            discardButton.onClick.AddListener(() => discard?.Invoke());
            rerecordButton.onClick.AddListener(() => rerecord?.Invoke());
            exportButton.onClick.AddListener(() => export?.Invoke());
        }

        private static Button CreateButton(string label, Transform parent, Vector2 min, Vector2 max, out TMP_Text text)
        {
            RectTransform rect = CreateRect(label + " Button", parent);
            SetAnchors(rect, min, max);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.52f, 0.72f, 1f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            text = CreateText("Label", rect, label, 26f, FontStyles.Bold);
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.text = value; text.fontSize = size; text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            return item.GetComponent<RectTransform>();
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        private static void EnsureMetaEventSystem()
        {
            EventSystem system = FindFirstObjectByType<EventSystem>();
            if (system == null) system = new GameObject("Thinking Space UI Event System").AddComponent<EventSystem>();
            if (system.GetComponent<PointableCanvasModule>() == null)
            {
                PointableCanvasModule module = system.gameObject.AddComponent<PointableCanvasModule>();
                module.ExclusiveMode = false;
            }
        }
    }
}
