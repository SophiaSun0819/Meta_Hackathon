using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.Speech
{
    /// <summary>
    /// Lightweight, runtime-created recording visual. It avoids project font and material
    /// dependencies so the prototype can be dropped into the existing Quest scene safely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PinchRecordingIndicator : MonoBehaviour
    {
        [Header("Headset-relative Placement")]
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.12f, 0.5f);

        [Header("Appearance")]
        [SerializeField, Min(0.005f)] private float sphereSize = 0.025f;
        [SerializeField, Min(0f)] private float labelOffset = 0.055f;
        [SerializeField] private Color recordingColor = new Color(1f, 0.025f, 0.025f, 1f);
        [SerializeField, Min(0f)] private float emissionIntensity = 3f;
        [SerializeField] private bool pulse = true;
        [SerializeField, Range(0f, 0.75f)] private float pulseAmount = 0.18f;
        [SerializeField, Min(0f)] private float pulseSpeed = 1.5f;

        [Header("Recording Status Panel")]
        [SerializeField] private Vector3 statusPanelOffset = new Vector3(0.1f, 0f, 0f);
        [SerializeField, Range(0.005f, 0.25f)] private float meterFullScaleRms = 0.08f;
        [SerializeField] private bool showNumericRms = true;

        [Header("Motion")]
        [SerializeField, Min(0.001f)] private float followSmoothTime = 0.05f;

        private Transform sphereTransform;
        private Transform labelTransform;
        private Transform statusPanelTransform;
        private Text statusText;
        private RectTransform meterFill;
        private Material sphereMaterial;
        private Vector3 targetPosition;
        private Vector3 followVelocity;
        private Transform headset;
        private bool isVisible;

        public float SphereSize => sphereSize;
        public float LabelOffset => labelOffset;

        private void Awake()
        {
            BuildVisuals();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (!isVisible || headset == null)
            {
                return;
            }

            targetPosition = headset.TransformPoint(headsetLocalPosition);
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref followVelocity,
                followSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            if (headset != null && labelTransform != null)
            {
                Vector3 toHeadset = headset.position - labelTransform.position;
                if (toHeadset.sqrMagnitude > 0.0001f)
                {
                    // A World Space Canvas faces along its forward axis.
                    labelTransform.rotation = Quaternion.LookRotation(-toHeadset.normalized, Vector3.up);
                }
            }

            if (headset != null && statusPanelTransform != null)
            {
                Vector3 toHeadset = headset.position - statusPanelTransform.position;
                if (toHeadset.sqrMagnitude > 0.0001f)
                    statusPanelTransform.rotation = Quaternion.LookRotation(-toHeadset.normalized, Vector3.up);
            }

            if (sphereTransform != null)
            {
                float pulseScale = pulse
                    ? 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) * pulseAmount
                    : 1f;
                sphereTransform.localScale = Vector3.one * sphereSize * pulseScale;
            }
        }

        public void Show(Vector3 worldPosition, Transform headsetTransform)
        {
            headset = headsetTransform;
            targetPosition = worldPosition;
            transform.position = worldPosition;
            followVelocity = Vector3.zero;
            SetVisible(true);
        }

        public void Show(Transform headsetTransform)
        {
            headset = headsetTransform;
            followVelocity = Vector3.zero;
            if (headset != null)
            {
                targetPosition = headset.TransformPoint(headsetLocalPosition);
                transform.position = targetPosition;
            }
            SetVisible(true);
        }

        public void SetTarget(Vector3 worldPosition, Transform headsetTransform)
        {
            targetPosition = worldPosition;
            headset = headsetTransform;
        }

        public void SetHeadset(Transform headsetTransform)
        {
            headset = headsetTransform;
        }

        public void Hide()
        {
            SetVisible(false);
            headset = null;
            followVelocity = Vector3.zero;
        }

        public void SetRecordingStatus(float elapsedSeconds, float rms, float peak, string state, string status)
        {
            if (statusText != null)
            {
                int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
                string elapsed = (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00");
                statusText.text = state + "   " + elapsed
                    + (showNumericRms ? "\nLevel " + rms.ToString("0.0000") + "   Peak " + peak.ToString("0.0000") : string.Empty)
                    + "\n" + status;
            }
            if (meterFill != null)
            {
                float normalized = Mathf.Clamp01(rms / meterFullScaleRms);
                meterFill.anchorMax = new Vector2(Mathf.Sqrt(normalized), 1f);
            }
        }

        private void BuildVisuals()
        {
            if (sphereTransform != null)
            {
                return;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Recording Glow";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * sphereSize;
            sphereTransform = sphere.transform;

            Collider generatedCollider = sphere.GetComponent<Collider>();
            if (generatedCollider != null)
            {
                Destroy(generatedCollider);
            }

            Renderer sphereRenderer = sphere.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                sphereMaterial = new Material(shader)
                {
                    name = "Runtime Recording Glow"
                };
                sphereMaterial.color = recordingColor;
                sphereMaterial.EnableKeyword("_EMISSION");
                sphereMaterial.SetColor("_EmissionColor", recordingColor * emissionIntensity);
                sphereRenderer.sharedMaterial = sphereMaterial;
            }

            GameObject canvasObject = new GameObject("Recording Label", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.transform.localPosition = Vector3.up * labelOffset;
            labelTransform = canvasObject.transform;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(220f, 48f);
            canvasRect.localScale = Vector3.one * 0.00045f;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textObject.GetComponent<Text>();
            text.text = "\u25CF Recording";
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 32;
            text.fontStyle = FontStyle.Bold;
            text.color = recordingColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildStatusPanel();
        }

        private void BuildStatusPanel()
        {
            GameObject canvasObject = new GameObject("Recording Status", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.transform.localPosition = statusPanelOffset;
            statusPanelTransform = canvasObject.transform;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 51;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(300f, 112f);
            canvasRect.localScale = Vector3.one * 0.0004f;

            Image background = CreateImage("Background", canvasRect, new Color(0.015f, 0.02f, 0.035f, 0.88f));
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = Vector2.zero;
            background.rectTransform.offsetMax = Vector2.zero;

            GameObject textObject = new GameObject("Status Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(canvasRect, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0.28f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -7f);
            statusText = textObject.GetComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 22;
            statusText.alignment = TextAnchor.MiddleLeft;
            statusText.color = Color.white;
            statusText.raycastTarget = false;
            statusText.horizontalOverflow = HorizontalWrapMode.Overflow;

            Image meterBackground = CreateImage("Level Meter Background", canvasRect, new Color(0.18f, 0.18f, 0.2f, 1f));
            RectTransform meterRect = meterBackground.rectTransform;
            meterRect.anchorMin = new Vector2(0f, 0f);
            meterRect.anchorMax = new Vector2(1f, 0.24f);
            meterRect.offsetMin = new Vector2(12f, 9f);
            meterRect.offsetMax = new Vector2(-12f, 0f);
            Image fill = CreateImage("Live Level", meterRect, new Color(0.15f, 1f, 0.32f, 1f));
            meterFill = fill.rectTransform;
            meterFill.anchorMin = Vector2.zero;
            meterFill.anchorMax = new Vector2(0f, 1f);
            meterFill.offsetMin = Vector2.zero;
            meterFill.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string objectName, Transform parent, Color color)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            if (sphereTransform != null)
            {
                sphereTransform.gameObject.SetActive(visible);
            }

            if (labelTransform != null)
            {
                labelTransform.gameObject.SetActive(visible);
            }
            if (statusPanelTransform != null) statusPanelTransform.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (sphereMaterial != null)
            {
                Destroy(sphereMaterial);
            }
        }
    }
}
