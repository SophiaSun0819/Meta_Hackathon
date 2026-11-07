using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class IdeaBubbleView : MonoBehaviour
    {
        private BrainstormIdeaRecord idea;
        private Transform billboard;
        private Material material;
        private Action<BrainstormIdeaRecord> selected;

        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public BrainstormIdeaRecord Idea => idea;

        public void Initialize(BrainstormIdeaRecord value, float diameter,
            Action<BrainstormIdeaRecord> onSelected)
        {
            idea = value;
            selected = onSelected;
            name = "Idea Bubble · " + value.IdeaId;
            BuildSphere(diameter, CategoryColor(value.Category));
            BuildSelectionCanvas(diameter);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (billboard == null || camera == null) return;
            Vector3 away = billboard.position - camera.transform.position;
            if (away.sqrMagnitude > 0.0001f)
                billboard.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private void BuildSphere(float diameter, Color color)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Placeholder Sphere";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * diameter;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "Idea Bubble Runtime Material" };
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            sphere.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildSelectionCanvas(float diameter)
        {
            GameObject root = new GameObject("Title And Interaction", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            billboard = root.transform;
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 45;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            root.AddComponent<GraphicRaycaster>();
            PointableCanvas pointable = root.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(canvas);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 180f);
            rect.localScale = Vector3.one * Mathf.Max(0.00065f, diameter / 180f);

            Image hitArea = root.AddComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0.001f);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = hitArea;
            button.onClick.AddListener(() => selected?.Invoke(idea));

            PlaneSurface plane = root.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            BoundsClipper clipper = root.AddComponent<BoundsClipper>();
            clipper.Size = new Vector3(180f, 180f, 0.01f);
            ClippedPlaneSurface clipped = root.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane,
                new System.Collections.Generic.List<IBoundsClipper> { clipper });
            RayInteractable ray = root.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke = root.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.InjectOptionalPointableElement(pointable);

            GameObject labelObject = new GameObject("Short Title", typeof(RectTransform));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.05f, 0.02f);
            labelRect.anchorMax = new Vector2(0.95f, 0.30f);
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 25f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.text = ShortTitle(idea.Title);
        }

        private static string ShortTitle(string value)
        {
            string title = string.IsNullOrWhiteSpace(value) ? "Untitled Idea" : value.Trim();
            return title.Length <= 42 ? title : title.Substring(0, 39) + "…";
        }

        private static Color CategoryColor(string category)
        {
            int hash = 17;
            string value = category ?? string.Empty;
            for (int i = 0; i < value.Length; i++) hash = hash * 31 + char.ToUpperInvariant(value[i]);
            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, 0.58f, 0.95f);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
