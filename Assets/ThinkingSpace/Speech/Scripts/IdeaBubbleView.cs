using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    public enum IdeaBubbleVisualState { Idle, Hovered, Open, Selected, Unavailable }
    public enum BubbleInteractionHand { Unknown, Left, Right }

    [DisallowMultipleComponent]
    public sealed class IdeaBubbleView : MonoBehaviour
    {
        private BrainstormIdeaRecord idea;
        private Transform billboard;
        private Material material;
        private Material outlineMaterial;
        private Renderer outlineRenderer;
        private GameObject boundsRoot;
        private readonly Transform[] boundsCorners = new Transform[8];
        private readonly Renderer[] boundsCornerRenderers = new Renderer[8];
        private Material boundsLineMaterial, boundsCornerMaterial, boundsAccentMaterial;
        private Button selectionButton;
        private Action<BrainstormIdeaRecord, BubbleInteractionHand, int> selected;
        private Canvas interactionCanvas;
        private int lastMetaSelectionFrame = -10;
        private int lastMetaPointerId = int.MinValue;
        private float lastMetaSelectionTime = float.NegativeInfinity;
        private bool receivedMetaSelection;
        private bool hovered, directProximity, isOpen, isSelected, isMoving, interactionAvailable = true;
        private float visualDiameter;

        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public BrainstormIdeaRecord Idea => idea;
        public bool IsSelected => isSelected;
        public bool IsOpen => isOpen;
        public bool IsInteractionAvailable => interactionAvailable;
        public IdeaBubbleVisualState VisualState => ResolveVisualState(
            interactionAvailable, hovered || directProximity, isOpen, isSelected);
        public float WorldRadius => visualDiameter * 0.5f * Mathf.Max(Mathf.Abs(transform.lossyScale.x),
            Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));

        public void Initialize(BrainstormIdeaRecord value, float diameter,
            Action<BrainstormIdeaRecord, BubbleInteractionHand, int> onSelected)
        {
            idea = value;
            selected = onSelected;
            visualDiameter = diameter;
            name = "Idea Bubble · " + value.IdeaId;
            BuildSphere(diameter, CategoryColor(value.Category));
            BuildSelectionCanvas(diameter);
            BuildBoundingBox(diameter);
            RefreshVisualState();
        }

        private void OnEnable()
        {
            PointableCanvasModule.WhenSelected += HandleMetaSelection;
        }

        private void OnDisable()
        {
            PointableCanvasModule.WhenSelected -= HandleMetaSelection;
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

        public void SetDirectProximity(bool value)
        {
            directProximity = value && interactionAvailable;
            RefreshVisualState();
        }

        public void SetBoundingBoxVisible(bool visible)
        {
            if (boundsRoot != null) boundsRoot.SetActive(visible && interactionAvailable);
        }

        public Vector3 GetBoundsCornerWorldPosition(int index)
            => index >= 0 && index < boundsCorners.Length && boundsCorners[index] != null
                ? boundsCorners[index].position : transform.position;

        public void SetBoundsHandleState(int hoveredIndex, int acquiredIndex, int oppositeIndex,
            bool highlightOppositeSide = false)
        {
            for (int i = 0; i < boundsCornerRenderers.Length; i++)
                if (boundsCornerRenderers[i] != null)
                    boundsCornerRenderers[i].sharedMaterial = i == acquiredIndex || i == oppositeIndex
                        || (highlightOppositeSide && acquiredIndex >= 0
                            && IsOppositeSide(acquiredIndex, i))
                        ? boundsAccentMaterial : i == hoveredIndex ? boundsAccentMaterial : boundsCornerMaterial;
        }

        public static int OppositeCorner(int index) => index >= 0 && index < 8 ? index ^ 7 : -1;
        public static bool IsOppositeSide(int firstIndex, int secondIndex)
            => firstIndex >= 0 && firstIndex < 8 && secondIndex >= 0 && secondIndex < 8
                && (firstIndex & 1) != (secondIndex & 1);

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
            float parentScale = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
            billboard.localScale = Vector3.one * Mathf.Max(0.00065f, visualDiameter / 180f) / parentScale;
        }

        private void BuildBoundingBox(float diameter)
        {
            boundsRoot = new GameObject("Resize Bounding Box");
            boundsRoot.transform.SetParent(transform, false);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            boundsLineMaterial = new Material(shader) { color = new Color(.3f, .9f, 1f, .9f) };
            boundsCornerMaterial = new Material(shader) { color = new Color(.18f, .65f, .82f, 1f) };
            boundsAccentMaterial = new Material(shader) { color = new Color(1f, .72f, .16f, 1f) };
            float half = diameter * .62f;
            for (int i = 0; i < 8; i++)
            {
                GameObject corner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                corner.name = "Resize Corner " + i;
                corner.transform.SetParent(boundsRoot.transform, false);
                corner.transform.localPosition = new Vector3((i & 1) == 0 ? -half : half,
                    (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);
                corner.transform.localScale = Vector3.one * diameter * .12f;
                Collider collider = corner.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                boundsCorners[i] = corner.transform;
                boundsCornerRenderers[i] = corner.GetComponent<Renderer>();
                boundsCornerRenderers[i].sharedMaterial = boundsCornerMaterial;
            }
            int[,] edges = { {0,1},{0,2},{0,4},{1,3},{1,5},{2,3},{2,6},{3,7},
                {4,5},{4,6},{5,7},{6,7} };
            for (int edge = 0; edge < 12; edge++)
            {
                GameObject lineObject = new GameObject("Resize Edge " + edge);
                lineObject.transform.SetParent(boundsRoot.transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false; line.positionCount = 2;
                line.startWidth = line.endWidth = diameter * .018f;
                line.sharedMaterial = boundsLineMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
                line.SetPosition(0, boundsCorners[edges[edge,0]].localPosition);
                line.SetPosition(1, boundsCorners[edges[edge,1]].localPosition);
            }
            boundsRoot.SetActive(false);
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
            interactionCanvas = canvas;
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
            // PointableCanvas emits its handed pointer event before Unity's Button callback.
            // Ignore that duplicate callback, but retain an explicit unknown-owner fallback for
            // non-Meta input so selection itself remains accessible in the Editor.
            if (!interactionAvailable || receivedMetaSelection
                || Time.frameCount - lastMetaSelectionFrame <= 1) return;
            selected?.Invoke(idea, BubbleInteractionHand.Unknown, int.MinValue);
        }

        private void HandleMetaSelection(PointableCanvasEventArgs args)
        {
            if (!interactionAvailable || interactionCanvas == null || args == null
                || args.Canvas != interactionCanvas || !args.PointerId.HasValue) return;
            int pointerId = args.PointerId.Value;
            float now = Time.unscaledTime;
            if (pointerId == lastMetaPointerId && now - lastMetaSelectionTime < 0.2f) return;
            lastMetaPointerId = pointerId;
            lastMetaSelectionTime = now;
            lastMetaSelectionFrame = Time.frameCount;
            receivedMetaSelection = true;
            selected?.Invoke(idea, ResolvePointerHand(pointerId), pointerId);
        }

        internal static BubbleInteractionHand ResolvePointerHand(int pointerId)
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (!(behaviour is IInteractorView interactor) || interactor.Identifier != pointerId) continue;
                Transform current = behaviour.transform;
                while (current != null)
                {
                    MonoBehaviour[] components = current.GetComponents<MonoBehaviour>();
                    for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                    {
                        if (!(components[componentIndex] is IHand hand)) continue;
                        try
                        {
                            if (hand.Handedness == Handedness.Left) return BubbleInteractionHand.Left;
                            if (hand.Handedness == Handedness.Right) return BubbleInteractionHand.Right;
                        }
                        catch (InvalidOperationException)
                        {
                            return BubbleInteractionHand.Unknown;
                        }
                        catch (NullReferenceException)
                        {
                            return BubbleInteractionHand.Unknown;
                        }
                    }
                    current = current.parent;
                }
                return BubbleInteractionHand.Unknown;
            }
            return BubbleInteractionHand.Unknown;
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
            if (boundsLineMaterial != null) Destroy(boundsLineMaterial);
            if (boundsCornerMaterial != null) Destroy(boundsCornerMaterial);
            if (boundsAccentMaterial != null) Destroy(boundsAccentMaterial);
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
