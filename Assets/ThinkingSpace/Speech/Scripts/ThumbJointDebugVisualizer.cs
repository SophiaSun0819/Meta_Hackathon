using UnityEngine;
using UnityEngine.Rendering;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class ThumbJointDebugVisualizer : MonoBehaviour
    {
        [Header("Joint Markers")]
        [SerializeField] private bool showJointMarkers;
        [SerializeField, Min(0.003f)] private float markerSize = 0.025f;
        [SerializeField, Min(0.001f)] private float lineWidth = 0.006f;

        [Header("Renderer Self-Test")]
        [Tooltip("Shows test geometry in front of the headset without requiring hand tracking.")]
        [SerializeField] private bool showRenderingSelfTest;
        [SerializeField] private Vector3 selfTestHeadsetOffset = new Vector3(0f, -0.18f, 0.5f);

        private sealed class HandVisual
        {
            public GameObject root;
            public Transform thumbBase, thumbTip, indexBase, indexTip;
            public LineRenderer thumbLine;
        }

        private HandVisual leftVisual, rightVisual, selfTestVisual;
        private Material redMaterial, blueMaterial, lineMaterial;
        private Transform headset;
        private bool rendererAvailable = true;
        private string leftStatus = "not evaluated";
        private string rightStatus = "not evaluated";

        public bool ShowJointMarkers => showJointMarkers;
        public bool ShowRenderingSelfTest => showRenderingSelfTest;
        public string Status { get; private set; } = "not initialized";

        public void SetHeadset(Transform value) => headset = value;

        public void SetHand(bool isLeft, bool tracked, Transform thumbBase, Transform thumbTip,
            Transform indexBase, Transform indexTip, string unavailableReason)
        {
            if (!showJointMarkers)
            {
                SetHandActive(isLeft, false);
                SetSideStatus(isLeft, "joint markers disabled");
                RefreshStatus();
                return;
            }

            EnsureVisuals();
            if (!rendererAvailable)
            {
                SetSideStatus(isLeft, "marker renderer unavailable");
                RefreshStatus();
                return;
            }

            HandVisual visual = isLeft ? leftVisual : rightVisual;
            bool valid = tracked && thumbBase != null && thumbTip != null
                && indexBase != null && indexTip != null;
            visual.root.SetActive(valid);
            SetSideStatus(isLeft, valid ? "ready" : unavailableReason);
            RefreshStatus();
            if (!valid) return;

            visual.thumbBase.position = thumbBase.position;
            visual.thumbTip.position = thumbTip.position;
            visual.indexBase.position = indexBase.position;
            visual.indexTip.position = indexTip.position;
            visual.thumbLine.SetPosition(0, thumbBase.position);
            visual.thumbLine.SetPosition(1, thumbTip.position);
        }

        private void LateUpdate()
        {
            UpdateSelfTest();
            RefreshStatus();
        }

        private void UpdateSelfTest()
        {
            if (!showRenderingSelfTest)
            {
                if (selfTestVisual != null) selfTestVisual.root.SetActive(false);
                return;
            }

            EnsureVisuals();
            if (!rendererAvailable || selfTestVisual == null) return;
            if (headset == null)
            {
                selfTestVisual.root.SetActive(false);
                return;
            }

            selfTestVisual.root.SetActive(true);
            Vector3 center = headset.TransformPoint(selfTestHeadsetOffset);
            Vector3 horizontal = headset.right * 0.045f;
            Vector3 vertical = Vector3.up * 0.1f;
            selfTestVisual.thumbBase.position = center - horizontal;
            selfTestVisual.thumbTip.position = center - horizontal + vertical;
            selfTestVisual.indexBase.position = center + horizontal;
            selfTestVisual.indexTip.position = center + horizontal;
            selfTestVisual.thumbLine.SetPosition(0, center - horizontal);
            selfTestVisual.thumbLine.SetPosition(1, center - horizontal + vertical);
        }

        private void EnsureVisuals()
        {
            if (leftVisual != null || !rendererAvailable) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                rendererAvailable = false;
                return;
            }

            redMaterial = CreateMaterial(shader, new Color(1f, 0.02f, 0.02f, 1f), "Thumb Debug Red");
            blueMaterial = CreateMaterial(shader, new Color(0.02f, 0.35f, 1f, 1f), "Index Debug Blue");
            lineMaterial = CreateMaterial(shader, new Color(1f, 0.9f, 0.02f, 1f), "Thumb Direction Yellow");
            leftVisual = BuildHandVisual("Left Joint Diagnostics");
            rightVisual = BuildHandVisual("Right Joint Diagnostics");
            selfTestVisual = BuildHandVisual("Joint Marker Renderer Self-Test");
            selfTestVisual.thumbTip.gameObject.SetActive(false);
            selfTestVisual.indexTip.gameObject.SetActive(false);
            leftVisual.root.SetActive(false);
            rightVisual.root.SetActive(false);
            selfTestVisual.root.SetActive(false);
        }

        private HandVisual BuildHandVisual(string visualName)
        {
            HandVisual visual = new HandVisual();
            visual.root = new GameObject(visualName);
            visual.root.transform.SetParent(transform, false);
            visual.thumbBase = CreateMarker("Thumb Base (Red)", visual.root.transform, redMaterial);
            visual.thumbTip = CreateMarker("Thumb Tip (Red)", visual.root.transform, redMaterial);
            visual.indexBase = CreateMarker("Index Base (Blue)", visual.root.transform, blueMaterial);
            visual.indexTip = CreateMarker("Index Tip (Blue)", visual.root.transform, blueMaterial);
            GameObject lineObject = new GameObject("Measured Thumb Direction (Yellow)");
            lineObject.transform.SetParent(visual.root.transform, false);
            visual.thumbLine = lineObject.AddComponent<LineRenderer>();
            visual.thumbLine.sharedMaterial = lineMaterial;
            visual.thumbLine.positionCount = 2;
            visual.thumbLine.startWidth = lineWidth;
            visual.thumbLine.endWidth = lineWidth;
            visual.thumbLine.useWorldSpace = true;
            visual.thumbLine.numCapVertices = 6;
            ConfigureRenderer(visual.thumbLine);
            return visual;
        }

        private Transform CreateMarker(string markerName, Transform parent, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(parent, false);
            marker.transform.localScale = Vector3.one * markerSize;
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null) Destroy(markerCollider);
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = material;
            ConfigureRenderer(markerRenderer);
            return marker.transform;
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Material CreateMaterial(Shader shader, Color color, string materialName)
        {
            Material material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            material.enableInstancing = true;
            return material;
        }

        private void SetHandActive(bool isLeft, bool active)
        {
            HandVisual visual = isLeft ? leftVisual : rightVisual;
            if (visual != null) visual.root.SetActive(active);
        }

        private void SetSideStatus(bool isLeft, string value)
        {
            string status = string.IsNullOrEmpty(value) ? "joint positions unavailable" : value;
            if (isLeft) leftStatus = status;
            else rightStatus = status;
        }

        private void RefreshStatus()
        {
            if (!rendererAvailable)
            {
                Status = "Marker renderer unavailable: no compatible unlit shader";
                return;
            }
            string selfTestStatus = !showRenderingSelfTest ? "self-test off"
                : headset == null ? "self-test blocked: headset reference missing" : "self-test visible";
            Status = "Left: " + leftStatus + " | Right: " + rightStatus + " | " + selfTestStatus;
        }

        private void OnDisable()
        {
            SetHandActive(true, false);
            SetHandActive(false, false);
            if (selfTestVisual != null) selfTestVisual.root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (redMaterial != null) Destroy(redMaterial);
            if (blueMaterial != null) Destroy(blueMaterial);
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
