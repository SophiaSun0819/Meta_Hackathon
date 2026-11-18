using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI.DesignKit
{
    /// <summary>
    /// Brings the runtime-built speech panels (transcript, review, AI reply, idea details, status,
    /// subtitles, mic pill) in line with the DesignKit menu: rounded dark glass, a thin cyan rim,
    /// one cyan accent for the main action, coral for destructive actions, muted secondary text.
    /// Call after ThinkingSpaceUIStyler has styled the panel.
    /// </summary>
    public static class QuestUIPanelSkin
    {
        public const string OutlineName = "DesignKit Outline";
        public const float CornerRadius = 24f;
        private const int TextureSize = 64;
        private const float RimThickness = 2.5f;
        private static Sprite roundedFill, roundedRim;

        public static Sprite RoundedFill => roundedFill != null ? roundedFill : roundedFill = BuildSprite(false);
        public static Sprite RoundedRim => roundedRim != null ? roundedRim : roundedRim = BuildSprite(true);

        /// <param name="cornerScale">Larger values make rounder corners (use about 1.7 for a pill).</param>
        public static void Apply(GameObject root, float cornerScale = 1f, bool animateIn = true)
        {
            if (root == null) return;
            Image background = root.GetComponent<Image>();
            if (background != null)
            {
                Rounded(background, QuestUIDesignTokens.Panel, cornerScale);
                EnsureRim(root.transform, cornerScale);
            }
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name == "SlimUI Decoration")
                {
                    Image decoration = child.GetComponent<Image>();
                    if (decoration != null) { decoration.color = Color.clear; decoration.raycastTarget = false; }
                }
                else if (child.name.StartsWith("UI Frame ")) child.gameObject.SetActive(false);
            }
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) StyleButton(button);
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.GetComponentInParent<Button>(true) == null) StyleText(text);
            if (animateIn && root.GetComponent<QuestUIAppear>() == null) root.AddComponent<QuestUIAppear>();
        }

        public static ButtonRole RoleFor(string buttonName)
        {
            string name = (buttonName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("confirm delete")) return ButtonRole.Danger;
            if (name.Contains("send to orby") || name.StartsWith("retry") || name.StartsWith("primary"))
                return ButtonRole.Primary;
            if (name.StartsWith("delete") || name.StartsWith("discard")) return ButtonRole.DangerQuiet;
            return ButtonRole.Secondary;
        }

        public static void StyleButton(Button button)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null) return;
            ButtonRole role = RoleFor(button.name);
            Color fill = role == ButtonRole.Primary ? QuestUIDesignTokens.Cyan
                : role == ButtonRole.Danger ? QuestUIDesignTokens.Coral : QuestUIDesignTokens.Raised;
            Color label = role == ButtonRole.Primary || role == ButtonRole.Danger ? QuestUIDesignTokens.Panel
                : role == ButtonRole.DangerQuiet ? QuestUIDesignTokens.Coral : QuestUIDesignTokens.Text;
            Rounded(image, Color.white, 1f);
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.white, .18f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(fill, Color.black, .2f);
            colors.disabledColor = QuestUIDesignTokens.Disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .1f;
            button.colors = colors;
            image.color = Color.white;
            foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = label;
                text.fontStyle = FontStyles.Bold;
            }
        }

        public static void StyleText(TMP_Text text)
        {
            if (text == null) return;
            string name = text.name.ToLowerInvariant();
            if (name.Contains("avatar")) return;
            bool secondary = name.Contains("page") || name.Contains("time") || name.Contains("status")
                || name.Contains("duration") || name.Contains("indicator") || name.Contains("timer");
            text.color = secondary ? QuestUIDesignTokens.Muted : QuestUIDesignTokens.Text;
            if (name.Contains("heading") || name.Contains("title")) text.characterSpacing = 1.5f;
        }

        public static void Rounded(Image image, Color color, float cornerScale)
        {
            if (image == null) return;
            image.sprite = RoundedFill;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(.1f, cornerScale);
            image.preserveAspect = false;
            image.color = color;
        }

        private static void EnsureRim(Transform root, float cornerScale)
        {
            Transform existing = root.Find(OutlineName);
            GameObject rim = existing != null ? existing.gameObject
                : new GameObject(OutlineName, typeof(RectTransform), typeof(Image));
            rim.transform.SetParent(root, false);
            RectTransform rect = rim.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rim.transform.SetAsFirstSibling();
            Image image = rim.GetComponent<Image>();
            image.sprite = RoundedRim;
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(.1f, cornerScale);
            Color cyan = QuestUIDesignTokens.Cyan; cyan.a = .55f;
            image.color = cyan;
            image.raycastTarget = false;
        }

        private static Sprite BuildSprite(bool rimOnly)
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = rimOnly ? "DesignKit Rounded Rim" : "DesignKit Rounded Fill",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[TextureSize * TextureSize];
            float half = TextureSize * .5f;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = RoundedRectDistance(x + .5f - half, y + .5f - half, half, CornerRadius);
                float outside = Mathf.Clamp01(distance + .5f);
                float alpha = 1f - outside;
                if (rimOnly) alpha *= Mathf.Clamp01(distance + RimThickness + .5f);
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = CornerRadius + 1f;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Signed distance to a rounded square edge; negative inside.</summary>
        private static float RoundedRectDistance(float x, float y, float half, float radius)
        {
            float qx = Mathf.Abs(x) - (half - radius), qy = Mathf.Abs(y) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        public enum ButtonRole { Primary, Secondary, Danger, DangerQuiet }
    }

    /// <summary>Fades and gently scales a panel in each time it is shown (play mode only).</summary>
    [DisallowMultipleComponent]
    public sealed class QuestUIAppear : MonoBehaviour
    {
        public const float Duration = .2f;
        public const float StartScale = .94f;
        private CanvasGroup group;
        private Vector3 restingScale;
        private bool hasRestingScale;
        private float progress = 1f;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            if (!hasRestingScale) { restingScale = transform.localScale; hasRestingScale = true; }
            progress = 0f;
            Apply(0f);
        }

        private void OnDisable()
        {
            if (!hasRestingScale) return;
            progress = 1f;
            Apply(1f);
        }

        private void Update()
        {
            if (progress >= 1f) return;
            progress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / Duration);
            Apply(progress);
        }

        public static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

        private void Apply(float t)
        {
            float eased = Ease(t);
            if (group != null) group.alpha = eased;
            if (hasRestingScale) transform.localScale = restingScale * Mathf.Lerp(StartScale, 1f, eased);
        }
    }
}
