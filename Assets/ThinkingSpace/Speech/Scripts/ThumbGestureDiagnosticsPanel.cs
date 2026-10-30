using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class ThumbGestureDiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private bool showDiagnostics;
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, 0.05f, 0.65f);
        [SerializeField, Min(12)] private int fontSize = 23;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.78f);

        private Transform panelTransform;
        private Text diagnosticText;
        private Transform headset;

        public bool ShowDiagnostics => showDiagnostics;

        private void Awake()
        {
            BuildPanel();
            panelTransform.gameObject.SetActive(showDiagnostics);
        }

        public void SetData(string value, Transform headsetTransform)
        {
            headset = headsetTransform;
            if (panelTransform == null) BuildPanel();
            panelTransform.gameObject.SetActive(showDiagnostics);
            if (showDiagnostics && diagnosticText != null) diagnosticText.text = value;
        }

        private void LateUpdate()
        {
            if (!showDiagnostics || headset == null || panelTransform == null) return;
            panelTransform.position = headset.TransformPoint(headsetLocalPosition);
            Vector3 toHeadset = headset.position - panelTransform.position;
            if (toHeadset.sqrMagnitude > 0.0001f)
                panelTransform.rotation = Quaternion.LookRotation(-toHeadset.normalized, Vector3.up);
        }

        private void BuildPanel()
        {
            if (panelTransform != null) return;

            GameObject canvasObject = new GameObject("Thumb Gesture Diagnostics", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            panelTransform = canvasObject.transform;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(920f, 660f);
            canvasRect.localScale = Vector3.one * 0.00046f;

            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image image = background.GetComponent<Image>();
            image.color = backgroundColor;
            image.raycastTarget = false;

            GameObject textObject = new GameObject("Diagnostic Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(22f, 18f);
            textRect.offsetMax = new Vector2(-22f, -18f);

            diagnosticText = textObject.GetComponent<Text>();
            diagnosticText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            diagnosticText.fontSize = fontSize;
            diagnosticText.alignment = TextAnchor.UpperLeft;
            diagnosticText.color = textColor;
            diagnosticText.raycastTarget = false;
            diagnosticText.horizontalOverflow = HorizontalWrapMode.Wrap;
            diagnosticText.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
