using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    public enum IdeaBubbleVisualState { Idle, Hovered, Open, Selected, Unavailable }

    [DisallowMultipleComponent]
    public sealed class IdeaBubbleView : MonoBehaviour
    {
        private BrainstormIdeaRecord idea;
        private Transform billboard;
        private Material material;
        private Material outlineMaterial;
        private Renderer outlineRenderer;
        private Button selectionButton;
        private Action<BrainstormIdeaRecord> selected;
        private bool hovered, isOpen, isSelected, isMoving, interactionAvailable = true;
        private float visualDiameter;

        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public BrainstormIdeaRecord Idea => idea;
        public bool IsSelected => isSelected;
        public bool IsOpen => isOpen;
        public bool IsInteractionAvailable => interactionAvailable;
        public IdeaBubbleVisualState VisualState => ResolveVisualState(
            interactionAvailable, hovered, isOpen, isSelected);

        public void Initialize(BrainstormIdeaRecord value, float diameter,
            Action<BrainstormIdeaRecord> onSelected)
        {
            idea = value;
            selected = onSelected;
            visualDiameter = diameter;
            name = "Idea Bubble · " + value.IdeaId;
            BuildSphere(diameter, CategoryColor(value.Category));
            BuildSelectionCanvas(diameter);
            RefreshVisualState();
        }

        public void SetInteractionAvailable(bool available)
        {
            if (interactionAvailable == available) return;
            interactionAvailable = available;
            if (!available) hovered = false;
            if (selectionButton != null) selectionButton.interactable = available;
            RefreshVisualState();
        }

        public void SetSelected(bool value)
        {
            if (isSelected == value) return;
            isSelected = value;
            RefreshVisualState();
        }

        public void SetOpen(bool value)
        {
            if (isOpen == value) return;
            isOpen = value;
            if (!value) isSelected = false;
            RefreshVisualState();
        }

        public void SetMoving(bool value)
        {
            if (isMoving == value) return;
            isMoving = value;
            RefreshVisualState();
        }

        internal void SetHovered(bool value)
        {
            value &= interactionAvailable;
            if (hovered == value) return;
            hovered = value;
            RefreshVisualState();
        }

        public static IdeaBubbleVisualState ResolveVisualState(bool available, bool hover,
            bool open, bool selectedValue)
        {
            if (!available) return IdeaBubbleVisualState.Unavailable;
            if (selectedValue) return IdeaBubbleVisualState.Selected;
            if (open) return IdeaBubbleVisualState.Open;
            return hover ? IdeaBubbleVisualState.Hovered : IdeaBubbleVisualState.Idle;
        }

        public static IdeaBubbleVisualState ResolveVisualState(bool available, bool hover, bool selectedValue)
            => ResolveVisualState(available, hover, selectedValue, selectedValue);

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (billboard == null || camera == null) return;
            Vector3 away = billboard.position - camera.transform.position;
            if (away.sqrMagnitude > 0.0001f)
                billboard.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            if (isMoving) RefreshVisualState();
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

            GameObject outline = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outline.name = "Interaction Outline";
            outline.transform.SetParent(transform, false);
            outline.transform.localScale = Vector3.one * (diameter * 1.08f);
            Collider outlineCollider = outline.GetComponent<Collider>();
            if (outlineCollider != null) Destroy(outlineCollider);
            outlineRenderer = outline.GetComponent<Renderer>();
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineMaterial = new Material(shader) { name = "Idea Bubble Outline Runtime Material" };
            if (outlineMaterial.HasProperty("_Cull")) outlineMaterial.SetFloat("_Cull", 1f);
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.enabled = false;
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
            selectionButton = root.AddComponent<Button>();
            selectionButton.targetGraphic = hitArea;
            selectionButton.onClick.AddListener(HandleSelection);
            BubblePointerRelay relay = root.AddComponent<BubblePointerRelay>();
            relay.Initialize(this);

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

            GameObject labelObject = new GameObject("Bubble Title", typeof(RectTransform));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.03f, 1.08f);
            labelRect.anchorMax = new Vector2(0.97f, 1.50f);
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 25f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = 25f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.text = string.IsNullOrWhiteSpace(idea.Title) ? "Untitled Idea" : idea.Title;
        }

        private void HandleSelection()
        {
            if (interactionAvailable) selected?.Invoke(idea);
        }

        private void RefreshVisualState()
        {
            if (outlineRenderer == null || outlineMaterial == null) return;
            IdeaBubbleVisualState state = VisualState;
            bool visible = state == IdeaBubbleVisualState.Hovered || state == IdeaBubbleVisualState.Open
                || state == IdeaBubbleVisualState.Selected;
            outlineRenderer.enabled = visible;
            if (!visible) return;
            bool selectedState = state == IdeaBubbleVisualState.Selected;
            bool openState = state == IdeaBubbleVisualState.Open;
            Color color = selectedState || openState
                ? new Color(1f, 0.72f, 0.16f, 1f)
                : new Color(0.42f, 0.92f, 1f, 1f);
            float movementPulse = isMoving ? 0.018f + Mathf.Sin(Time.unscaledTime * 7f) * 0.008f : 0f;
            outlineRenderer.transform.localScale = Vector3.one * visualDiameter
                * ((selectedState ? 1.16f : openState ? 1.11f : 1.08f) + movementPulse);
            outlineMaterial.color = color;
            if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor", color);
            if (outlineMaterial.HasProperty("_EmissionColor"))
            {
                outlineMaterial.EnableKeyword("_EMISSION");
                float intensity = selectedState ? 2.25f : openState ? 1.65f : 1.25f;
                outlineMaterial.SetColor("_EmissionColor", color * (isMoving ? intensity + 0.55f : intensity));
            }
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
            if (outlineMaterial != null) Destroy(outlineMaterial);
        }
    }

    internal sealed class BubblePointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private readonly System.Collections.Generic.HashSet<int> hoveringPointers =
            new System.Collections.Generic.HashSet<int>();
        private IdeaBubbleView owner;

        public void Initialize(IdeaBubbleView value) => owner = value;

        public void OnPointerEnter(PointerEventData eventData)
        {
            hoveringPointers.Add(eventData != null ? eventData.pointerId : 0);
            owner?.SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hoveringPointers.Remove(eventData != null ? eventData.pointerId : 0);
            owner?.SetHovered(hoveringPointers.Count > 0);
        }

        private void OnDisable()
        {
            hoveringPointers.Clear();
            owner?.SetHovered(false);
        }
    }
}
