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
        private Renderer visualRenderer;
        private GameObject visualRoot;
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
        private float categorySaturation = 0.78f;
        private float localVisualRadius;

        private const string VisualResourcePath = "IdeaBubbleVisual";
        private const float IdleOpacity = 0.80f;
        private const float HoverOpacity = 0.85f;
        private const float OpenOpacity = 0.88f;
        private const float ActiveOpacity = 0.95f;
        private const float ManipulationOpacity = 0.95f;
        private const float UnavailableOpacity = 0.75f;
        private const float IdleEmission = 0.20f;
        private const float HoverEmission = 0.40f;
        private const float OpenEmission = 0.55f;
        private const float ActiveEmission = 0.85f;
        private const float ManipulationEmission = 1.00f;
        private const float UnavailableEmission = 0.10f;

        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public BrainstormIdeaRecord Idea => idea;
        public bool IsSelected => isSelected;
        public bool IsOpen => isOpen;
        public bool IsInteractionAvailable => interactionAvailable;
        public string VisualColor => idea!=null?IdeaVisualColorPalette.Normalize(idea.VisualColor):IdeaVisualColorPalette.Default;
        public IdeaBubbleVisualState VisualState => ResolveVisualState(
            interactionAvailable, hovered || directProximity, isOpen, isSelected);
        public float WorldRadius => localVisualRadius * Mathf.Max(Mathf.Abs(transform.lossyScale.x),
            Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));

        public void Initialize(BrainstormIdeaRecord value, float diameter,
            Action<BrainstormIdeaRecord, BubbleInteractionHand, int> onSelected,
            float saturation = 0.78f)
        {
            idea = value;
            selected = onSelected;
            visualDiameter = diameter;
            categorySaturation = Mathf.Clamp01(saturation);
            name = "Idea Bubble · " + value.IdeaId;
            BuildBubbleVisual(diameter, CategoryColor(value.Category, categorySaturation));
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

        public void SetVisualColor(string value)
        {
            if(idea==null)return;idea.VisualColor=IdeaVisualColorPalette.Normalize(value);RefreshVisualState();
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

        private void BuildBubbleVisual(float diameter, Color color)
        {
            GameObject prefab = Resources.Load<GameObject>(VisualResourcePath);
            if (prefab != null)
            {
                visualRoot = Instantiate(prefab, transform, false);
                visualRoot.name = "Idea Bubble Visual";
            }
            else if (!TryCreateFromAuthoredBubble(out visualRoot))
            {
                visualRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visualRoot.name = "Idea Bubble Visual (Fallback)";
                visualRoot.transform.SetParent(transform, false);
                Collider fallbackCollider = visualRoot.GetComponent<Collider>();
                if (fallbackCollider != null) Destroy(fallbackCollider);
            }
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one * diameter;

            Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);
            visualRenderer = visualRoot.GetComponentInChildren<Renderer>(true);
            if (visualRenderer == null) return;
            visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;

            Material source = visualRenderer.sharedMaterial;
            if (source == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                material = new Material(shader) { name = "Idea Bubble Runtime Material · " + IdeaId };
            }
            else material = new Material(source) { name = "Idea Bubble Runtime Material · " + IdeaId };
            ConfigureRuntimeMaterial(material);
            visualRenderer.sharedMaterial = material;
            localVisualRadius = CalculateLocalVisualRadius(visualRenderer, diameter);
        }

        private bool TryCreateFromAuthoredBubble(out GameObject result)
        {
            result = null;
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Transform source = null;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name != "Bubble" || transforms[i].GetComponent<MeshFilter>() == null) continue;
                Transform root = transforms[i].root;
                if (root != null && root.name == "Orbi") { source = transforms[i]; break; }
            }
            if (source == null) return false;
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceFilter.sharedMesh == null || sourceRenderer == null) return false;

            result = new GameObject("Idea Bubble Visual (Runtime Extracted)");
            result.transform.SetParent(transform, false);
            GameObject meshObject = new GameObject("Bubble Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            meshObject.transform.SetParent(result.transform, false);
            Mesh mesh = sourceFilter.sharedMesh;
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            meshObject.GetComponent<MeshRenderer>().sharedMaterial = sourceRenderer.sharedMaterial;
            Bounds bounds = mesh.bounds;
            float sourceDiameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float normalization = sourceDiameter > 0.0001f ? 1f / sourceDiameter : 1f;
            meshObject.transform.localScale = Vector3.one * normalization;
            meshObject.transform.localPosition = -bounds.center * normalization;
            return true;
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
            if (material == null) return;
            Color category=ResolveVisualHue(idea!=null?idea.Category:string.Empty,
                idea!=null?idea.VisualColor:null,categorySaturation);
            Color baseColor = new Color(category.r, category.g, category.b,
                Opacity(interactionAvailable, isMoving, VisualState));
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", category * EmissionMultiplier(
                    interactionAvailable, isMoving, VisualState));
        }

        public static float EmissionMultiplier(bool available, bool manipulating,
            IdeaBubbleVisualState state)
        {
            if (!available || state == IdeaBubbleVisualState.Unavailable) return UnavailableEmission;
            if (manipulating) return ManipulationEmission;
            if (state == IdeaBubbleVisualState.Selected) return ActiveEmission;
            if (state == IdeaBubbleVisualState.Open) return OpenEmission;
            if (state == IdeaBubbleVisualState.Hovered) return HoverEmission;
            return IdleEmission;
        }

        public static float Opacity(bool available, bool manipulating,
            IdeaBubbleVisualState state)
        {
            if (!available || state == IdeaBubbleVisualState.Unavailable) return UnavailableOpacity;
            if (manipulating) return ManipulationOpacity;
            if (state == IdeaBubbleVisualState.Selected) return ActiveOpacity;
            if (state == IdeaBubbleVisualState.Open) return OpenOpacity;
            if (state == IdeaBubbleVisualState.Hovered) return HoverOpacity;
            return IdleOpacity;
        }

        public static Color CategoryColor(string category, float saturation = 0.78f)
        {
            int hash = 17;
            string value = category ?? string.Empty;
            for (int i = 0; i < value.Length; i++) hash = hash * 31 + char.ToUpperInvariant(value[i]);
            float hue = Mathf.Abs(hash % 360) / 360f;
            return Color.HSVToRGB(hue, Mathf.Clamp01(saturation), 0.95f);
        }

        public static Color ResolveVisualHue(string category,string visualColor,float saturation=.78f)
            =>IdeaVisualColorPalette.Resolve(visualColor,CategoryColor(category,saturation));

        private static void ConfigureRuntimeMaterial(Material value)
        {
            if (value == null) return;
            if (value.HasProperty("_Surface")) value.SetFloat("_Surface", 1f);
            if (value.HasProperty("_ZWrite")) value.SetFloat("_ZWrite", 0f);
            if (value.HasProperty("_ReceiveShadows")) value.SetFloat("_ReceiveShadows", 0f);
            value.SetOverrideTag("RenderType", "Transparent");
            value.renderQueue = (int)RenderQueue.Transparent;
            value.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (value.HasProperty("_EmissionColor")) value.EnableKeyword("_EMISSION");
        }

        private float CalculateLocalVisualRadius(Renderer renderer, float fallbackDiameter)
        {
            if (renderer == null) return fallbackDiameter * 0.5f;
            Vector3 extent = renderer.bounds.extents;
            float worldExtent = Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
            float rootScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x),
                Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));
            return rootScale > 0.0001f && worldExtent > 0.0001f
                ? worldExtent / rootScale : fallbackDiameter * 0.5f;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
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
