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
    public sealed class TranscriptPanel : MonoBehaviour
    {
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.03f, 0.62f);
        [SerializeField] private Vector2 panelSize = new Vector2(620f, 430f);
        [SerializeField, Min(0.0001f)] private float canvasScale = 0.0005f;
        [SerializeField, Min(200)] private int charactersPerPage = 700;

        private Transform headset;
        private Canvas canvas;
        private TMP_Text heading;
        private TMP_Text body;
        private Button retryButton;
        private Button closeButton;
        private TMP_Text closeLabel;
        private TMP_Text pageIndicator;
        private Button previousButton;
        private Button nextButton;
        private readonly List<string> pages = new List<string>();
        private int currentPage;
        private Action retryAction;
        private Action closeAction;

        private void Awake()
        {
            BuildPanel();
            canvas.gameObject.SetActive(false);
        }

        public void SetHeadset(Transform value) => headset = value;

        public void ShowTranscribing(string recordingIdentifier, Action back)
        {
            Show();
            heading.text = "Transcribing...";
            SetPagedBody("Sending completed recording to the transcription service.\n\nRequest: " + recordingIdentifier);
            retryButton.gameObject.SetActive(false);
            closeAction = back;
            closeLabel.text = "Back to Recording";
            closeButton.gameObject.SetActive(back != null);
            closeButton.interactable = back != null;
        }

        public void ShowTranscript(string transcript, Action confirm, Action back)
        {
            Show();
            heading.text = "Transcription Complete";
            SetPagedBody(transcript);
            retryAction = confirm;
            retryButton.GetComponentInChildren<TMP_Text>(true).text = "Send / Confirm";
            retryButton.gameObject.SetActive(confirm != null);
            retryButton.interactable = confirm != null;
            closeAction = back;
            closeLabel.text = "Back to Recording";
            closeButton.gameObject.SetActive(true);
            closeButton.interactable = true;
        }

        public void ShowError(string message, Action retry, Action back)
        {
            Show();
            heading.text = "Transcription Failed";
            SetPagedBody(message + "\n\nThe completed recording is still available in memory.");
            retryAction = retry;
            retryButton.GetComponentInChildren<TMP_Text>(true).text = "Retry";
            closeAction = back;
            retryButton.gameObject.SetActive(retry != null);
            retryButton.interactable = retry != null;
            closeLabel.text = "Back to Recording";
            closeButton.gameObject.SetActive(back != null);
            closeButton.interactable = back != null;
        }

        public void ShowNotice(string headingText, string message, Action closed)
        {
            Show();
            heading.text = headingText;
            SetPagedBody(message);
            retryButton.gameObject.SetActive(false);
            closeAction = closed;
            closeLabel.text = "Close";
            closeButton.gameObject.SetActive(closed != null);
            closeButton.interactable = closed != null;
        }

        public void Hide()
        {
            retryAction = null;
            closeAction = null;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void Show()
        {
            if (canvas == null) BuildPanel();
            canvas.gameObject.SetActive(true);
            PositionAtHeadset();
        }

        private void SetPagedBody(string value)
        {
            pages.Clear();
            string text = value ?? string.Empty;
            if (text.Length == 0) pages.Add(string.Empty);
            else
            {
                int offset = 0;
                while (offset < text.Length)
                {
                    int count = Mathf.Min(charactersPerPage, text.Length - offset);
                    if (offset + count < text.Length)
                    {
                        int breakAt = text.LastIndexOfAny(new[] { ' ', '\n', '\t' }, offset + count - 1, count);
                        if (breakAt >= offset + charactersPerPage / 2) count = breakAt - offset + 1;
                    }
                    pages.Add(text.Substring(offset, count).Trim());
                    offset += count;
                }
            }
            currentPage = 0;
            RefreshPage();
        }

        private void ChangePage(int delta)
        {
            currentPage = Mathf.Clamp(currentPage + delta, 0, pages.Count - 1);
            RefreshPage();
        }

        private void RefreshPage()
        {
            if (body != null) body.text = pages.Count > 0 ? pages[currentPage] : string.Empty;
            if (pageIndicator != null) pageIndicator.text = "Page " + (currentPage + 1) + " / " + Mathf.Max(1, pages.Count);
            if (previousButton != null) previousButton.interactable = currentPage > 0;
            if (nextButton != null) nextButton.interactable = currentPage < pages.Count - 1;
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
            GameObject root = new GameObject("Transcript Panel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 60;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            root.AddComponent<GraphicRaycaster>();
            PointableCanvas pointable = root.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(canvas);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = panelSize;
            rootRect.localScale = Vector3.one * canvasScale;
            Image panel = root.AddComponent<Image>();
            panel.color = new Color(0.035f, 0.055f, 0.09f, 0.98f);
            panel.raycastTarget = false;

            PlaneSurface plane = root.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            BoundsClipper clipper = root.AddComponent<BoundsClipper>();
            clipper.Size = new Vector3(panelSize.x, panelSize.y, 0.01f);
            ClippedPlaneSurface clipped = root.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { clipper });
            RayInteractable ray = root.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke = root.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.InjectOptionalPointableElement(pointable);

            heading = CreateText("Heading", rootRect, 38f, FontStyles.Bold, TextAlignmentOptions.Center);
            RectTransform headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0.04f, 0.82f);
            headingRect.anchorMax = new Vector2(0.96f, 0.97f);
            headingRect.offsetMin = headingRect.offsetMax = Vector2.zero;

            RectTransform viewport = CreateRect("Transcript Viewport", rootRect);
            viewport.anchorMin = new Vector2(0.06f, 0.28f);
            viewport.anchorMax = new Vector2(0.94f, 0.8f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.26f);
            viewportImage.raycastTarget = false;
            body = CreateText("Transcript Page", viewport, 27f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = new Vector2(14f, 12f);
            body.rectTransform.offsetMax = new Vector2(-14f, -12f);

            previousButton = CreateButton("Previous", rootRect, new Vector2(0.08f, 0.18f), new Vector2(0.31f, 0.26f));
            previousButton.onClick.AddListener(() => ChangePage(-1));
            pageIndicator = CreateText("Page Indicator", rootRect, 23f, FontStyles.Normal, TextAlignmentOptions.Center);
            pageIndicator.rectTransform.anchorMin = new Vector2(0.34f, 0.18f);
            pageIndicator.rectTransform.anchorMax = new Vector2(0.66f, 0.26f);
            pageIndicator.rectTransform.offsetMin = pageIndicator.rectTransform.offsetMax = Vector2.zero;
            nextButton = CreateButton("Next", rootRect, new Vector2(0.69f, 0.18f), new Vector2(0.92f, 0.26f));
            nextButton.onClick.AddListener(() => ChangePage(1));

            retryButton = CreateButton("Retry", rootRect, new Vector2(0.08f, 0.04f), new Vector2(0.43f, 0.16f));
            retryButton.onClick.AddListener(() =>
            {
                if (!retryButton.interactable || retryAction == null) return;
                retryButton.interactable = false;
                retryAction.Invoke();
            });
            closeButton = CreateButton("Close", rootRect, new Vector2(0.57f, 0.04f), new Vector2(0.92f, 0.16f));
            closeLabel = closeButton.GetComponentInChildren<TMP_Text>(true);
            closeButton.onClick.AddListener(() =>
            {
                if (!closeButton.interactable || closeAction == null) return;
                closeButton.interactable = false;
                closeAction.Invoke();
            });
        }

        private static TMP_Text CreateText(
            string name, Transform parent, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string label, Transform parent, Vector2 min, Vector2 max)
        {
            RectTransform rect = CreateRect(label + " Button", parent);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.52f, 0.72f, 1f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text text = CreateText("Label", rect, 28f, FontStyles.Bold, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.text = label;
            return button;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            return item.GetComponent<RectTransform>();
        }

        private static void EnsureMetaEventSystem()
        {
            EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null) eventSystem = new GameObject("Thinking Space UI Event System").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<PointableCanvasModule>() == null)
            {
                PointableCanvasModule module = eventSystem.gameObject.AddComponent<PointableCanvasModule>();
                module.ExclusiveMode = false;
            }
        }
    }
}
