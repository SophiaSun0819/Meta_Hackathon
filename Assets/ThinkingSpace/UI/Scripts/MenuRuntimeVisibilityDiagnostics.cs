using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI.DesignKit;

namespace ThinkingSpace.UI
{
    public sealed class MenuRuntimeVisibilitySnapshot
    {
        public string Trigger { get; internal set; }
        public string Screen { get; internal set; }
        public int CanvasInstanceId { get; internal set; }
        public bool CanvasEnabled { get; internal set; }
        public bool CanvasActive { get; internal set; }
        public RenderMode RenderMode { get; internal set; }
        public bool OverrideSorting { get; internal set; }
        public int SortingOrder { get; internal set; }
        public bool SkinInitialized { get; internal set; }
        public string VisualState { get; internal set; }
        public int ActiveGraphics { get; internal set; }
        public int VisibleGraphics { get; internal set; }
        public int ActiveButtons { get; internal set; }
        public int ActionableButtons { get; internal set; }
        public int ActiveLabels { get; internal set; }
        public int VisibleLabels { get; internal set; }
        public int TextVertices { get; internal set; }
        public int ActiveMasks { get; internal set; }
        public int PotentiallyClippedControls { get; internal set; }
        public int CulledGraphics { get; internal set; }
        public int ZeroRendererAlphaGraphics { get; internal set; }
        public int RenderableButtons { get; internal set; }
        public int RenderableLabels { get; internal set; }
        public int PotentialObstructions { get; internal set; }
        public bool ProbeActive { get; internal set; }
        public bool BorderActive { get; internal set; }
        public string TopGraphics { get; internal set; }

        public bool HasActionableContent => CanvasEnabled && CanvasActive
            && RenderableButtons > 0 && RenderableLabels > 0 && TextVertices > 0;

        public string ToSanitizedLine()
        {
            string result = "TS_UI_VISIBILITY v3 trigger=" + Safe(Trigger)
                + " screen=" + Safe(Screen)
                + " canvas=" + CanvasInstanceId + ":" + Bool(CanvasEnabled) + "/" + Bool(CanvasActive)
                + " mode=" + RenderMode + " sorting=" + Bool(OverrideSorting) + ":" + SortingOrder
                + " skin=" + Bool(SkinInitialized) + " state=" + Safe(VisualState)
                + " graphics=" + VisibleGraphics + "/" + ActiveGraphics
                + " buttons=" + ActionableButtons + "/" + ActiveButtons
                + " labels=" + VisibleLabels + "/" + ActiveLabels + " vertices=" + TextVertices
                + " masks=" + ActiveMasks + " clipped=" + PotentiallyClippedControls
                + " culled=" + CulledGraphics + " rendererAlpha0=" + ZeroRendererAlphaGraphics
                + " renderable=" + RenderableButtons + "/" + RenderableLabels
                + " obstructions=" + PotentialObstructions + " probe=" + Bool(ProbeActive)
                + " border=" + Bool(BorderActive) + " healthy=" + Bool(HasActionableContent)
                + " top=" + Safe(TopGraphics);
            return result.Length <= 1024 ? result : result.Substring(0, 1024);
        }

        private static string Bool(bool value) => value ? "1" : "0";
        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            StringBuilder builder = new StringBuilder(Mathf.Min(value.Length, 160));
            for (int i = 0; i < value.Length && builder.Length < 160; i++)
            {
                char c = value[i];
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/'
                    || c == ':' || c == '.' ? c : '_');
            }
            return builder.ToString();
        }
    }

    public static class MenuRuntimeVisibilityDiagnostics
    {
        private const float VisibleAlpha = 0.02f;
        private const string ProbeName = "TS Development Menu Render Probe";
        private const string OriginalComparisonName = "Original Legacy Comparison";
        private const string NormalizedComparisonName = "Normalized Legacy Comparison";

        public static MenuRuntimeVisibilitySnapshot Capture(Canvas canvas, BrainstormMenuView view,
            QuestUIActiveMenuSkin skin, string screen, string trigger)
        {
            MenuRuntimeVisibilitySnapshot snapshot = new MenuRuntimeVisibilitySnapshot
            {
                Trigger = trigger,
                Screen = screen,
                CanvasInstanceId = canvas != null ? canvas.GetInstanceID() : 0,
                CanvasEnabled = canvas != null && canvas.enabled,
                CanvasActive = canvas != null && canvas.gameObject.activeInHierarchy,
                RenderMode = canvas != null ? canvas.renderMode : RenderMode.WorldSpace,
                OverrideSorting = canvas != null && canvas.overrideSorting,
                SortingOrder = canvas != null ? canvas.sortingOrder : 0,
                SkinInitialized = skin != null && skin.IsInitialized,
                VisualState = view != null ? view.RuntimeVisualState : "view-missing"
            };
            if (canvas == null) return snapshot;

            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            List<Graphic> visibleGraphics = new List<Graphic>();
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || !graphic.gameObject.activeInHierarchy || !graphic.enabled) continue;
                snapshot.ActiveGraphics++;
                if (graphic.canvasRenderer.cull) snapshot.CulledGraphics++;
                if (graphic.canvasRenderer.GetAlpha() <= VisibleAlpha) snapshot.ZeroRendererAlphaGraphics++;
                if (!IsRendererVisible(graphic)) continue;
                snapshot.VisibleGraphics++;
                visibleGraphics.Add(graphic);
            }

            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy || !button.enabled) continue;
                snapshot.ActiveButtons++;
                Graphic target = button.targetGraphic;
                bool visible = target != null && target.enabled && IsRendererVisible(target)
                    && RectHasArea(target.rectTransform);
                bool clipped = visible && IsOutsideActiveMask(target.rectTransform);
                if (clipped) snapshot.PotentiallyClippedControls++;
                if (button.interactable && visible && !clipped)
                {
                    snapshot.ActionableButtons++;
                    snapshot.RenderableButtons++;
                }
            }

            TMP_Text[] labels = canvas.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null || !label.gameObject.activeInHierarchy || !label.enabled) continue;
                snapshot.ActiveLabels++;
                if (string.IsNullOrWhiteSpace(label.text)
                    || !IsRendererVisible(label)
                    || !RectHasArea(label.rectTransform) || IsOutsideActiveMask(label.rectTransform)) continue;
                try { label.ForceMeshUpdate(false, false); }
                catch (Exception) { continue; }
                int vertices = 0;
                if (label.textInfo != null && label.textInfo.meshInfo != null)
                    for (int mesh = 0; mesh < label.textInfo.meshInfo.Length; mesh++)
                        vertices += label.textInfo.meshInfo[mesh].vertexCount;
                if (vertices <= 0) continue;
                snapshot.VisibleLabels++;
                snapshot.RenderableLabels++;
                snapshot.TextVertices += vertices;
            }

            snapshot.ActiveMasks = CountActiveMasks(canvas.transform);
            snapshot.PotentialObstructions = CountPotentialObstructions(canvas, buttons, labels);
            Transform probe = canvas.transform.Find(ProbeName);
            snapshot.ProbeActive = probe != null && probe.gameObject.activeInHierarchy;
            snapshot.BorderActive = FindActive(canvas.transform, "DesignKit Thin Border");
            visibleGraphics.Sort((left, right) => right.depth.CompareTo(left.depth));
            StringBuilder top = new StringBuilder();
            for (int i = 0; i < visibleGraphics.Count && i < 5; i++)
            {
                Graphic graphic = visibleGraphics[i];
                if (top.Length > 0) top.Append(',');
                top.Append(SafeName(graphic.name)).Append('@').Append(graphic.depth)
                    .Append(':').Append(graphic.canvasRenderer.GetAlpha().ToString("0.00"));
            }
            snapshot.TopGraphics = top.ToString();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            LogCanvasDetails(canvas);
            LogElementDetails(canvas, buttons, labels);
#endif
            return snapshot;
        }

        public static bool ToggleProbe(Canvas canvas)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (canvas == null) return false;
            Transform existing = canvas.transform.Find(ProbeName);
            GameObject probe = existing != null ? existing.gameObject : CreateProbe(canvas);
            Transform original = probe.transform.Find(OriginalComparisonName);
            Transform normalized = probe.transform.Find(NormalizedComparisonName);
            string state;
            if (!probe.activeSelf)
            {
                probe.SetActive(true);
                if (original != null) original.gameObject.SetActive(true);
                if (normalized != null) normalized.gameObject.SetActive(false);
                state = "original";
            }
            else if (original != null && original.gameObject.activeSelf)
            {
                original.gameObject.SetActive(false);
                if (normalized != null) normalized.gameObject.SetActive(true);
                state = "normalized";
            }
            else
            {
                probe.SetActive(false);
                if (original != null) original.gameObject.SetActive(true);
                if (normalized != null) normalized.gameObject.SetActive(false);
                state = "off";
            }
            if (probe.activeSelf) probe.transform.SetAsLastSibling();
            Debug.Log("TS_UI_PROBE state=" + state + " active=" + Bool(probe.activeSelf)
                + " path=" + SafePath(probe.transform, canvas.transform));
            return probe.activeSelf;
#else
            return false;
#endif
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static GameObject CreateProbe(Canvas canvas)
        {
            GameObject probe = new GameObject(ProbeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            probe.transform.SetParent(canvas.transform, false);
            RectTransform rect = probe.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(700f, 330f);
            rect.anchoredPosition = Vector2.zero;
            Image image = probe.GetComponent<Image>();
            image.color = new Color(0f, 1f, 1f, 1f);
            image.material = null;
            image.raycastTarget = false;

            GameObject labelObject = new GameObject("Known Good TMP Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(probe.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, .68f); labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 8f); labelRect.offsetMax = new Vector2(-18f, -8f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "TS MENU RENDER PROBE";
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSize = 34f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(.015f, .04f, .07f, 1f);
            label.raycastTarget = false;
            BuildLegacyComparison(canvas, probe.transform, false);
            BuildLegacyComparison(canvas, probe.transform, true);
            probe.SetActive(false);
            return probe;
        }

        private static void BuildLegacyComparison(Canvas canvas, Transform probe, bool normalized)
        {
            Button sourceButton = FindLegacySessionButton(canvas);
            TMP_Text sourceLabel = sourceButton != null ? sourceButton.GetComponentInChildren<TMP_Text>(true) : null;
            GameObject group = new GameObject(normalized ? NormalizedComparisonName : OriginalComparisonName,
                typeof(RectTransform));
            group.transform.SetParent(probe, false);
            RectTransform groupRect = group.GetComponent<RectTransform>();
            groupRect.anchorMin = Vector2.zero; groupRect.anchorMax = new Vector2(1f, .66f);
            groupRect.offsetMin = new Vector2(18f, 12f); groupRect.offsetMax = new Vector2(-18f, -8f);

            GameObject imageObject = new GameObject(normalized ? "Normalized Legacy Button Image" : "Original Legacy Button Image",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(group.transform, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(.05f, .12f); imageRect.anchorMax = new Vector2(.47f, .88f);
            imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
            Image copyImage = imageObject.GetComponent<Image>();
            Image sourceImage = sourceButton != null ? sourceButton.targetGraphic as Image : null;
            if (sourceImage != null)
            {
                copyImage.sprite = sourceImage.sprite; copyImage.type = sourceImage.type;
                copyImage.fillMethod = sourceImage.fillMethod; copyImage.fillAmount = sourceImage.fillAmount;
                copyImage.material = sourceImage.material; copyImage.color = sourceImage.color;
                copyImage.preserveAspect = sourceImage.preserveAspect;
            }
            copyImage.raycastTarget = false;

            GameObject textObject = new GameObject(normalized ? "Normalized Legacy Rubik Label" : "Original Legacy Rubik Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(group.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(.52f, .12f); textRect.anchorMax = new Vector2(.95f, .88f);
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            TextMeshProUGUI copyLabel = textObject.GetComponent<TextMeshProUGUI>();
            copyLabel.text = sourceLabel != null ? sourceLabel.text : "LEGACY RUBIK LABEL";
            if (sourceLabel != null)
            {
                copyLabel.font = sourceLabel.font; copyLabel.fontSharedMaterial = sourceLabel.fontSharedMaterial;
                copyLabel.color = sourceLabel.color; copyLabel.fontStyle = sourceLabel.fontStyle;
            }
            copyLabel.fontSize = normalized ? 32f : Mathf.Max(24f, sourceLabel != null ? sourceLabel.fontSize : 28f);
            copyLabel.alignment = TextAlignmentOptions.Center;
            copyLabel.raycastTarget = false;
            if (normalized)
            {
                copyImage.material = null; copyImage.color = new Color(0f, 1f, 1f, 1f);
                copyImage.rectTransform.localScale = Vector3.one; copyImage.rectTransform.localRotation = Quaternion.identity;
                copyLabel.font = TMP_Settings.defaultFontAsset;
                copyLabel.fontSharedMaterial = copyLabel.font != null ? copyLabel.font.material : null;
                copyLabel.color = new Color(.015f, .04f, .07f, 1f);
                copyLabel.rectTransform.localScale = Vector3.one; copyLabel.rectTransform.localRotation = Quaternion.identity;
            }
            group.SetActive(!normalized);
        }

        private static Button FindLegacySessionButton(Canvas canvas)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
            Button first = null;
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy
                    || !SafePath(button.transform, canvas.transform).Contains("Session_Menu")) continue;
                if (first == null) first = button;
                if (button.interactable && button.targetGraphic is Image) return button;
            }
            return first;
        }

        private static void LogCanvasDetails(Canvas canvas)
        {
            Camera camera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            Vector3 canvasPosition = canvas.transform.position;
            Vector3 canvasNormal = canvas.transform.forward;
            float distance = camera != null ? Vector3.Distance(camera.transform.position, canvasPosition) : -1f;
            float facing = camera != null ? Vector3.Dot(canvasNormal,
                (camera.transform.position - canvasPosition).normalized) : 0f;
            Debug.Log("TS_UI_CANVAS mode=" + canvas.renderMode + " camera="
                + SafeName(camera != null ? camera.name : "none") + " position=" + Vec(canvasPosition)
                + " normal=" + Vec(canvasNormal) + " cameraDistance=" + F(distance)
                + " cameraFacingDot=" + F(facing) + " scale=" + Vec(canvas.transform.lossyScale));
        }

        private static void LogElementDetails(Canvas canvas, Button[] buttons, TMP_Text[] labels)
        {
            HashSet<Graphic> buttonGraphics = new HashSet<Graphic>();
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                Graphic target = button.targetGraphic;
                if (target != null) buttonGraphics.Add(target);
                Image targetImage = target as Image;
                Debug.Log(DescribeGraphic("button", target, canvas.transform,
                    "interactable=" + Bool(button.interactable)
                    + " sprite=" + Bool(targetImage != null && targetImage.sprite != null)
                    + " buttonSibling=" + button.transform.GetSiblingIndex()));
            }

            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null || !label.gameObject.activeInHierarchy) continue;
                Debug.Log(DescribeGraphic("label", label, canvas.transform, "sprite=0"));
            }

            Image[] images = canvas.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || !image.gameObject.activeInHierarchy || !image.enabled || buttonGraphics.Contains(image)) continue;
                bool fullPanel = IsFullPanelSurface(canvas.transform as RectTransform, image.rectTransform);
                bool obscures = fullPanel && CouldObscureActionable(image, buttons, labels);
                Debug.Log(DescribeGraphic("image", image, canvas.transform,
                    "sprite=" + Bool(image.sprite != null) + " full=" + Bool(fullPanel)
                    + " raycast=" + Bool(image.raycastTarget) + " obstruct=" + Bool(obscures)));
            }
        }
#endif

        private static string DescribeGraphic(string kind, Graphic graphic, Transform root, string suffix)
        {
            if (graphic == null) return "TS_UI_ELEMENT kind=" + kind + " missing=1 " + suffix;
            Material material = graphic.materialForRendering;
            string materialName = material != null ? material.name : "none";
            string shaderName = material != null && material.shader != null ? material.shader.name : "none";
            RectTransform rect = graphic.rectTransform;
            Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Color rendererColor = graphic.canvasRenderer.GetColor();
            return "TS_UI_ELEMENT kind=" + kind
                + " path=" + SafePath(graphic.transform, root)
                + " active=" + Bool(graphic.gameObject.activeInHierarchy)
                + " enabled=" + Bool(graphic.enabled)
                + " cull=" + Bool(graphic.canvasRenderer.cull)
                + " rendererAlpha=" + graphic.canvasRenderer.GetAlpha().ToString("0.000")
                + " rendererColor=" + Col(rendererColor)
                + " color=" + Col(graphic.color)
                + " groupAlpha=" + ParentGroupAlpha(graphic.transform).ToString("0.000")
                + " depth=" + graphic.depth
                + " sibling=" + graphic.transform.GetSiblingIndex()
                + " material=" + SafeName(materialName)
                + " shader=" + SafeName(shaderName)
                + MaterialDepth(material)
                + " local=" + Vec(rect.localPosition) + " world=" + Vec(rect.position)
                + " size=" + Vec2(rect.rect.size) + " scale=" + Vec(rect.lossyScale)
                + " rotation=" + Vec(rect.rotation.eulerAngles) + " z=" + F(rect.position.z)
                + " corners=" + Vec(corners[0]) + ":" + Vec(corners[2])
                + " vertexColors=" + VertexColors(graphic) + " " + suffix;
        }

        private static string VertexColors(Graphic graphic)
        {
            TMP_Text text = graphic as TMP_Text;
            if (text != null)
            {
                try { text.ForceMeshUpdate(false, false); }
                catch (Exception) { return "tmp-update-failed"; }
                StringBuilder result = new StringBuilder();
                int count = 0;
                for (int mesh = 0; text.textInfo != null && mesh < text.textInfo.meshInfo.Length && count < 4; mesh++)
                {
                    Color32[] colors = text.textInfo.meshInfo[mesh].colors32;
                    int vertices = text.textInfo.meshInfo[mesh].vertexCount;
                    for (int i = 0; colors != null && i < vertices && count < 4; i++)
                    {
                        string value = Col(colors[i]);
                        if (result.ToString().Contains(value)) continue;
                        if (result.Length > 0) result.Append('|'); result.Append(value); count++;
                    }
                }
                return result.Length > 0 ? result.ToString() : "none";
            }
            try
            {
                Mesh meshData = graphic.canvasRenderer.GetMesh();
                if (meshData == null) return "none";
                Color32[] colors = meshData.colors32;
                return colors != null && colors.Length > 0 ? Col(colors[0]) : "none";
            }
            catch (Exception) { return "mesh-unavailable"; }
        }

        private static string MaterialDepth(Material material)
        {
            if (material == null) return " ztest=none zwrite=none stencil=none colorMask=none";
            return " ztest=" + Prop(material, "_ZTest") + " zwrite=" + Prop(material, "_ZWrite")
                + " stencil=" + Prop(material, "_StencilComp") + "/" + Prop(material, "_Stencil")
                + " colorMask=" + Prop(material, "_ColorMask");
        }

        private static string Prop(Material material, string property)
            => material.HasProperty(property) ? material.GetFloat(property).ToString("0.###") : "na";

        private static string F(float value) => value.ToString("0.###");
        private static string Vec(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string Vec2(Vector2 value) => F(value.x) + "," + F(value.y);
        private static string Col(Color value) => F(value.r) + "," + F(value.g) + "," + F(value.b) + "," + F(value.a);
        private static string Col(Color32 value) => value.r + "," + value.g + "," + value.b + "," + value.a;

        private static int CountPotentialObstructions(Canvas canvas, Button[] buttons, TMP_Text[] labels)
        {
            int count = 0;
            Image[] images = canvas.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || !image.gameObject.activeInHierarchy || !image.enabled
                    || image.GetComponent<Button>() != null || !IsRendererVisible(image)) continue;
                if (IsFullPanelSurface(canvas.transform as RectTransform, image.rectTransform)
                    && CouldObscureActionable(image, buttons, labels)) count++;
            }
            return count;
        }

        private static bool CouldObscureActionable(Image image, Button[] buttons, TMP_Text[] labels)
        {
            Rect imageBounds = WorldRect(image.rectTransform);
            for (int i = 0; i < buttons.Length; i++)
            {
                Graphic target = buttons[i] != null ? buttons[i].targetGraphic : null;
                if (target != null && target.gameObject.activeInHierarchy && RendersAfter(image, target)
                    && Overlaps(imageBounds, WorldRect(target.rectTransform))) return true;
            }
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label != null && label.gameObject.activeInHierarchy && RendersAfter(image, label)
                    && Overlaps(imageBounds, WorldRect(label.rectTransform))) return true;
            }
            return false;
        }

        private static bool IsFullPanelSurface(RectTransform canvas, RectTransform image)
        {
            if (canvas == null || image == null || canvas.rect.width <= 1f || canvas.rect.height <= 1f) return false;
            Rect canvasBounds = WorldRect(canvas);
            Rect imageBounds = WorldRect(image);
            return imageBounds.width >= canvasBounds.width * .7f && imageBounds.height >= canvasBounds.height * .7f;
        }

        private static bool RendersAfter(Graphic candidate, Graphic control)
        {
            if (candidate == null || control == null) return false;
            if (candidate.depth >= 0 && control.depth >= 0 && candidate.depth != control.depth)
                return candidate.depth > control.depth;
            List<Transform> candidatePath = PathFromRoot(candidate.transform);
            List<Transform> controlPath = PathFromRoot(control.transform);
            int common = 0;
            while (common < candidatePath.Count && common < controlPath.Count
                && candidatePath[common] == controlPath[common]) common++;
            if (common == 0) return false;
            if (common == controlPath.Count) return true;
            if (common == candidatePath.Count) return false;
            return candidatePath[common].GetSiblingIndex() > controlPath[common].GetSiblingIndex();
        }

        private static List<Transform> PathFromRoot(Transform transform)
        {
            List<Transform> path = new List<Transform>();
            for (Transform current = transform; current != null; current = current.parent) path.Add(current);
            path.Reverse();
            return path;
        }

        private static bool IsRendererVisible(Graphic graphic)
            => graphic != null && !graphic.canvasRenderer.cull
                && graphic.canvasRenderer.GetAlpha() > VisibleAlpha
                && EffectiveAlpha(graphic.transform, graphic.color.a) > VisibleAlpha;

        private static float ParentGroupAlpha(Transform transform)
        {
            float result = 1f;
            for (Transform current = transform; current != null; current = current.parent)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null) result *= group.alpha;
            }
            return result;
        }

        private static string SafePath(Transform transform, Transform root)
        {
            if (transform == null) return "missing";
            List<string> parts = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                parts.Add(SafeName(current.name));
                if (current == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Bool(bool value) => value ? "1" : "0";

        private static float EffectiveAlpha(Transform transform, float alpha)
        {
            float result = alpha;
            for (Transform current = transform; current != null; current = current.parent)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group != null) result *= group.alpha;
            }
            return result;
        }

        private static bool RectHasArea(RectTransform rect)
            => rect != null && rect.rect.width > 1f && rect.rect.height > 1f;

        private static bool IsOutsideActiveMask(RectTransform rect)
        {
            if (rect == null) return true;
            Rect bounds = WorldRect(rect);
            for (Transform current = rect.parent; current != null; current = current.parent)
            {
                RectMask2D rectMask = current.GetComponent<RectMask2D>();
                if (rectMask != null && rectMask.enabled && rectMask.gameObject.activeInHierarchy
                    && !Overlaps(bounds, WorldRect(rectMask.rectTransform))) return true;
                Mask mask = current.GetComponent<Mask>();
                RectTransform maskRect = current as RectTransform;
                if (mask != null && mask.enabled && mask.gameObject.activeInHierarchy && maskRect != null
                    && !Overlaps(bounds, WorldRect(maskRect))) return true;
            }
            return false;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = corners[0].x, maxX = corners[0].x, minY = corners[0].y, maxY = corners[0].y;
            for (int i = 1; i < corners.Length; i++)
            {
                minX = Mathf.Min(minX, corners[i].x); maxX = Mathf.Max(maxX, corners[i].x);
                minY = Mathf.Min(minY, corners[i].y); maxY = Mathf.Max(maxY, corners[i].y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static bool Overlaps(Rect left, Rect right)
            => left.xMin <= right.xMax && left.xMax >= right.xMin
                && left.yMin <= right.yMax && left.yMax >= right.yMin;

        private static int CountActiveMasks(Transform root)
        {
            int count = 0;
            RectMask2D[] rectMasks = root.GetComponentsInChildren<RectMask2D>(true);
            for (int i = 0; i < rectMasks.Length; i++)
                if (rectMasks[i].enabled && rectMasks[i].gameObject.activeInHierarchy) count++;
            Mask[] masks = root.GetComponentsInChildren<Mask>(true);
            for (int i = 0; i < masks.Length; i++)
                if (masks[i].enabled && masks[i].gameObject.activeInHierarchy) count++;
            return count;
        }

        private static bool FindActive(Transform root, string name)
        {
            if (root.name == name && root.gameObject.activeInHierarchy) return true;
            for (int i = 0; i < root.childCount; i++)
                if (FindActive(root.GetChild(i), name)) return true;
            return false;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unnamed";
            StringBuilder result = new StringBuilder(Mathf.Min(48, value.Length));
            for (int i = 0; i < value.Length && result.Length < 48; i++)
            {
                char c = value[i];
                result.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return result.ToString();
        }
    }
}
