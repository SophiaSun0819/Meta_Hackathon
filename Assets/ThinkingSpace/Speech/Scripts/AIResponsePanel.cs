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
    public sealed class AIResponsePanel : MonoBehaviour
    {
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.04f, 0.65f);
        [SerializeField] private Vector2 panelSize = new Vector2(600f, 410f);
        [SerializeField, Min(0.0001f)] private float canvasScale = 0.0005f;
        [SerializeField, Min(180)] private int charactersPerPage = 520;
        private Transform headset;
        private Canvas canvas;
        private TMP_Text heading, body, pageIndicator, primaryLabel, secondaryLabel;
        private Button previousButton, nextButton, primaryButton, secondaryButton;
        private readonly List<string> pages = new List<string>();
        private int page;
        private Action primaryAction, secondaryAction;

        private void Awake() { Build(); canvas.gameObject.SetActive(false); }
        public void SetHeadset(Transform value) => headset = value;

        public void Show(string title, string content, string primaryText, Action primary,
            string secondaryText, Action secondary)
        {
            if (canvas == null) Build();
            heading.text = title ?? "Orby";
            SetPages(content);
            primaryAction = primary;
            secondaryAction = secondary;
            ConfigureButton(primaryButton, primaryLabel, primaryText, primary);
            ConfigureButton(secondaryButton, secondaryLabel, secondaryText, secondary);
            canvas.gameObject.SetActive(true);
            PositionAtHeadset();
        }

        public void Hide()
        {
            primaryAction = secondaryAction = null;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private static void ConfigureButton(Button button, TMP_Text label, string text, Action action)
        {
            bool visible = action != null;
            button.gameObject.SetActive(visible);
            button.interactable = visible;
            label.text = text ?? string.Empty;
        }

        private void SetPages(string content)
        {
            pages.Clear();
            string value = content ?? string.Empty;
            if (value.Length == 0) pages.Add(string.Empty);
            for (int offset = 0; offset < value.Length;)
            {
                int count = Mathf.Min(charactersPerPage, value.Length - offset);
                if (offset + count < value.Length)
                {
                    int split = value.LastIndexOfAny(new[] { ' ', '\n', '\t' }, offset + count - 1, count);
                    if (split >= offset + charactersPerPage / 2) count = split - offset + 1;
                }
                pages.Add(value.Substring(offset, count).Trim());
                offset += count;
            }
            page = 0;
            RefreshPage();
        }

        private void ChangePage(int direction)
        { page = Mathf.Clamp(page + direction, 0, pages.Count - 1); RefreshPage(); }

        private void RefreshPage()
        {
            body.text = pages.Count > 0 ? pages[page] : string.Empty;
            pageIndicator.text = "Page " + (page + 1) + " / " + Mathf.Max(1, pages.Count);
            previousButton.interactable = page > 0;
            nextButton.interactable = page < pages.Count - 1;
        }

        private void LateUpdate()
        { if (canvas != null && canvas.gameObject.activeSelf) PositionAtHeadset(); }

        private void PositionAtHeadset()
        {
            if (headset == null || canvas == null) return;
            canvas.transform.position = headset.TransformPoint(headsetLocalPosition);
            Vector3 away = canvas.transform.position - headset.position;
            if (away.sqrMagnitude > 0.0001f)
                canvas.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private void Build()
        {
            if (canvas != null) return;
            EnsureMetaEventSystem();
            GameObject root = new GameObject("AI Response Panel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 65;
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

            heading = Text("Heading", rect, 36f, FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.96f));
            body = Text("Content", rect, 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft,
                new Vector2(0.07f, 0.30f), new Vector2(0.93f, 0.80f));
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Ellipsis;
            previousButton = Button("Previous", rect, new Vector2(0.07f, 0.20f), new Vector2(0.30f, 0.28f), out _);
            previousButton.onClick.AddListener(() => ChangePage(-1));
            pageIndicator = Text("Page", rect, 22f, FontStyles.Normal, TextAlignmentOptions.Center,
                new Vector2(0.32f, 0.20f), new Vector2(0.68f, 0.28f));
            nextButton = Button("Next", rect, new Vector2(0.70f, 0.20f), new Vector2(0.93f, 0.28f), out _);
            nextButton.onClick.AddListener(() => ChangePage(1));
            primaryButton = Button("Primary", rect, new Vector2(0.07f, 0.05f), new Vector2(0.47f, 0.17f), out primaryLabel);
            primaryButton.onClick.AddListener(() => { if (primaryButton.interactable) primaryAction?.Invoke(); });
            secondaryButton = Button("Secondary", rect, new Vector2(0.53f, 0.05f), new Vector2(0.93f, 0.17f), out secondaryLabel);
            secondaryButton.onClick.AddListener(() => { if (secondaryButton.interactable) secondaryAction?.Invoke(); });
        }

        private static TMP_Text Text(string name, Transform parent, float size, FontStyles style,
            TextAlignmentOptions alignment, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(name, parent); rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset; text.fontSize = size; text.fontStyle = style;
            text.alignment = alignment; text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private static Button Button(string name, Transform parent, Vector2 min, Vector2 max, out TMP_Text label)
        {
            RectTransform rect = Rect(name + " Button", parent); rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.08f, 0.52f, 0.72f, 1f);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            label = Text("Label", rect, 25f, FontStyles.Bold, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            label.text = name; return button;
        }

        private static RectTransform Rect(string name, Transform parent)
        { GameObject value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false); return value.GetComponent<RectTransform>(); }

        private static void EnsureMetaEventSystem()
        {
            EventSystem system = FindFirstObjectByType<EventSystem>();
            if (system == null) system = new GameObject("Thinking Space UI Event System").AddComponent<EventSystem>();
            if (system.GetComponent<PointableCanvasModule>() == null)
            { PointableCanvasModule module = system.gameObject.AddComponent<PointableCanvasModule>(); module.ExclusiveMode = false; }
        }
    }
}
