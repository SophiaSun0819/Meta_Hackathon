#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;

namespace ThinkingSpace.UI.Editor
{
    public static class BrainstormMenuPrefabBuilder
    {
        private const string PrefabPath = "Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab";
        private const string TemplatePath = "Assets/SlimUI/Modern Menu 1/Prefabs/Canvas Templates/Canvas_DefaultTemplate1.prefab";
        private const string ButtonPrefabPath = "Assets/SlimUI/Modern Menu 1/Prefabs/Buttons/Btn_MainMenu.prefab";
        private const string SpeechPrefabPath = "Assets/ThinkingSpace/Speech/Prefabs/PinchRecordingPrototype.prefab";
        private const string FontPath = "Assets/SlimUI/Modern Menu 1/Fonts/RUBIK-MEDIUM SDF.asset";
        private const string PanelPath = "Assets/SlimUI/Modern Menu 1/Graphics/Frames/Panel 1920x1080px.png";
        private const string FramePath = "Assets/SlimUI/Modern Menu 1/Graphics/Frames/Panel Frame 512px.png";
        private const string ButtonPath = "Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Fram 256px.png";
        private const string ButtonHoverPath = "Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Frame Hover 256px.png";
        private const string ButtonPressedPath = "Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Frame Press 256px.png";
        private const string ThumbnailPath = "Assets/SlimUI/Modern Menu 1/Graphics/Thumbnails/SciFi Menu 256px.jpg";
        private const string CursorPath = "Assets/SlimUI/Modern Menu 1/Graphics/Cursors/Cursor 2 64px.png";
        private const string AdaptedCursorPath = "Assets/ThinkingSpace/UI/Graphics/SlimUI Meta Hover Cursor.png";

        private static TMP_FontAsset font;
        private static Sprite panelSprite;
        private static Sprite frameSprite;
        private static Sprite buttonSprite;
        private static Sprite buttonHoverSprite;
        private static Sprite buttonPressedSprite;
        private static Sprite thumbnailSprite;
        private static Sprite cursorSprite;
        private static GameObject slimButtonPrefab;
        private static readonly Color Background = new Color(0.018f, 0.025f, 0.045f, 1f);
        private static readonly Color Button = new Color(0.18f, 0.65f, 0.92f, 1f);
        private static readonly Color Accent = new Color(1f, 0.68f, 0.08f, 1f);

        [MenuItem("Thinking Space/UI/Rebuild Brainstorm Template Menu Prefab")]
        public static void BuildFromMenu() => Build();
        public static void BuildFromCommandLine() => Build();

        private static void Build()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new System.InvalidOperationException("Missing TMP font: " + FontPath);
            panelSprite = LoadSprite(PanelPath);
            frameSprite = LoadSprite(FramePath);
            buttonSprite = LoadSprite(ButtonPath);
            buttonHoverSprite = LoadSprite(ButtonHoverPath);
            buttonPressedSprite = LoadSprite(ButtonPressedPath);
            thumbnailSprite = LoadSprite(ThumbnailPath);
            cursorSprite = PrepareCursorSprite();
            slimButtonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            GameObject sourceTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            if (slimButtonPrefab == null) throw new System.InvalidOperationException("Missing SlimUI button prefab: " + ButtonPrefabPath);
            if (sourceTemplate == null) throw new System.InvalidOperationException("Missing SlimUI template: " + TemplatePath);

            GameObject root = new GameObject("BrainstormMenuTemplate", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(BrainstormMenuView));
            GameObject originalTemplate = (GameObject)PrefabUtility.InstantiatePrefab(sourceTemplate);
            originalTemplate.name = "Original Canvas_DefaultTemplate1 Hierarchy (Disabled)";
            originalTemplate.transform.SetParent(root.transform, false);
            originalTemplate.SetActive(false);
            RemoveDemoComponents(originalTemplate);
            BrainstormMenuView view = root.GetComponent<BrainstormMenuView>();
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(720f, 720f);
            rootRect.localScale = Vector3.one * 0.000278f;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 20;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

            PointableCanvas pointable = root.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(canvas);
            PlaneSurface plane = root.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            BoundsClipper bounds = root.AddComponent<BoundsClipper>();
            bounds.Size = new Vector3(720f, 720f, 0.01f);
            ClippedPlaneSurface clipped = root.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { bounds });
            RayInteractable ray = root.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke = root.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.InjectOptionalPointableElement(pointable);

            RectTransform adaptedRoot = CreateRect("Thinking Space Adapted UI", rootRect);
            Stretch(adaptedRoot, 0f);
            Image background = CreateImage("SlimUI Template Background", adaptedRoot, Background, false);
            background.sprite = panelSprite;
            background.type = Image.Type.Sliced;
            Stretch(background.rectTransform, 0f);

            GameObject main = CreateScrollableScreen("Main Menu", adaptedRoot, out RectTransform mainContent);
            CreateThumbnail(mainContent);
            TMP_Text title = CreateLabel("Brainstorm", mainContent, 44f, 72f, FontStyles.Bold);
            title.color = Color.white;
            CreateLabel("Brainstorming Sessions", mainContent, 27f, 48f, FontStyles.Bold);
            TMP_Text empty = CreateLabel("No sessions yet", mainContent, 23f, 44f, FontStyles.Italic);
            empty.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            RectTransform sessions = CreateContainer("Session List", mainContent);
            Button template = CreateButton("Session Button Template", sessions, "Session 1", false);
            template.gameObject.SetActive(false);
            Button start = CreateButton("Start New Session", mainContent, "Start New Session", true);
            Button mainVoice = CreateButton("Voice Commands", mainContent, "Voice Commands", false);
            Button mainGestures = CreateButton("Hand Gestures", mainContent, "Hand Gestures", false);
            CreateLabel("Recording Hand", mainContent, 24f, 42f, FontStyles.Bold);
            Button mainLeft = CreateButton("Recording Hand Left", mainContent, "Left", false);
            Button mainRight = CreateButton("Recording Hand Right", mainContent, "Right", false);
            Button mainClose = CreateButton("Close Menu", mainContent, "Close Menu", false);

            GameObject session = CreateScrollableScreen("Session Menu", adaptedRoot, out RectTransform sessionContent);
            TMP_Text sessionTitle = CreateLabel("Session 1", sessionContent, 40f, 66f, FontStyles.Bold);
            TMP_Text sessionStatus = CreateLabel(string.Empty, sessionContent, 21f, 38f, FontStyles.Italic);
            sessionStatus.color = new Color(1f, 0.78f, 0.25f, 1f);
            Button challenge = CreateButton("Challenge My Idea", sessionContent, "Challenge My Idea", false);
            Button expand = CreateButton("Expand My Idea", sessionContent, "Expand My Idea", false);
            Button connect = CreateButton("Connect My Ideas", sessionContent, "Connect My Ideas", false);
            Button whiteboard = CreateButton("Spawn Whiteboard", sessionContent, "Spawn Whiteboard", false);
            Button sessionVoice = CreateButton("Voice Commands", sessionContent, "Voice Commands", false);
            Button sessionGestures = CreateButton("Hand Gestures", sessionContent, "Hand Gestures", false);
            CreateLabel("Recording Hand", sessionContent, 24f, 42f, FontStyles.Bold);
            Button sessionLeft = CreateButton("Recording Hand Left", sessionContent, "Left", false);
            Button sessionRight = CreateButton("Recording Hand Right", sessionContent, "Right", false);
            Button end = CreateButton("End Session", sessionContent, "End Session", false);
            Button sessionClose = CreateButton("Close Menu", sessionContent, "Close Menu", false);

            GameObject voice = CreateHelpScreen("Voice Commands Help", adaptedRoot, "Voice Commands",
                "Voice command reference will be added with speech recognition.", out Button voiceBack);
            GameObject gestures = CreateHelpScreen("Hand Gestures Help", adaptedRoot, "Hand Gestures",
                "Gesture reference will be expanded as interactions are finalized.", out Button gesturesBack);

            Image frame = CreateImage("SlimUI Template Frame", adaptedRoot, Color.white, false);
            frame.sprite = frameSprite;
            frame.type = Image.Type.Sliced;
            Stretch(frame.rectTransform, 5f);

            Image front = CreateImage("FRONT Diagnostic", adaptedRoot, new Color(0.15f, 1f, 0.25f, 0.95f), false);
            RectTransform frontRect = front.rectTransform;
            frontRect.anchorMin = frontRect.anchorMax = new Vector2(0.5f, 1f);
            frontRect.pivot = new Vector2(0.5f, 1f);
            frontRect.anchoredPosition = new Vector2(0f, -8f);
            frontRect.sizeDelta = new Vector2(180f, 44f);
            TMP_Text frontText = CreateText("FRONT", frontRect, 27f, FontStyles.Bold);
            frontText.color = Color.black;
            front.gameObject.SetActive(false);

            main.SetActive(true);
            session.SetActive(false);
            voice.SetActive(false);
            gestures.SetActive(false);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(mainContent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(sessionContent);

            SerializedObject serializedView = new SerializedObject(view);
            Set(serializedView, "mainPanel", main);
            Set(serializedView, "sessionPanel", session);
            Set(serializedView, "voiceHelpPanel", voice);
            Set(serializedView, "gestureHelpPanel", gestures);
            Set(serializedView, "noSessionsLabel", empty.gameObject);
            Set(serializedView, "sessionList", sessions);
            Set(serializedView, "sessionButtonTemplate", template);
            Set(serializedView, "startSessionButton", start);
            Set(serializedView, "mainVoiceButton", mainVoice);
            Set(serializedView, "mainGesturesButton", mainGestures);
            Set(serializedView, "mainLeftHandButton", mainLeft);
            Set(serializedView, "mainRightHandButton", mainRight);
            Set(serializedView, "mainCloseButton", mainClose);
            Set(serializedView, "sessionTitle", sessionTitle);
            Set(serializedView, "sessionStatus", sessionStatus);
            Set(serializedView, "challengeButton", challenge);
            Set(serializedView, "expandButton", expand);
            Set(serializedView, "connectButton", connect);
            Set(serializedView, "whiteboardButton", whiteboard);
            Set(serializedView, "sessionVoiceButton", sessionVoice);
            Set(serializedView, "sessionGesturesButton", sessionGestures);
            Set(serializedView, "sessionLeftHandButton", sessionLeft);
            Set(serializedView, "sessionRightHandButton", sessionRight);
            Set(serializedView, "endSessionButton", end);
            Set(serializedView, "sessionCloseButton", sessionClose);
            Set(serializedView, "voiceBackButton", voiceBack);
            Set(serializedView, "gesturesBackButton", gesturesBack);
            Set(serializedView, "frontMarker", front.gameObject);
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssignToSpeechPrefab();
            Debug.Log("Built Canvas_DefaultTemplate1-adapted Brainstorm menu prefab at " + PrefabPath);
        }

        private static void AssignToSpeechPrefab()
        {
            GameObject menuRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            BrainstormMenuView menu = menuRoot != null ? menuRoot.GetComponent<BrainstormMenuView>() : null;
            GameObject speechRoot = PrefabUtility.LoadPrefabContents(SpeechPrefabPath);
            BrainstormMenuController controller = speechRoot.GetComponent<BrainstormMenuController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("authoredMenuPrefab").objectReferenceValue = menu;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(speechRoot, SpeechPrefabPath);
            PrefabUtility.UnloadPrefabContents(speechRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject CreateScrollableScreen(string name, Transform parent, out RectTransform content)
        {
            RectTransform screen = CreateRect(name, parent);
            Stretch(screen, 24f);
            screen.gameObject.AddComponent<RectMask2D>();
            content = CreateContainer("Content", screen);
            ScrollRect scroll = screen.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = screen;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return screen.gameObject;
        }

        private static GameObject CreateHelpScreen(string name, Transform parent, string title,
            string body, out Button back)
        {
            RectTransform screen = CreateRect(name, parent);
            Stretch(screen, 28f);
            VerticalLayoutGroup layout = screen.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout);
            CreateLabel(title, screen, 40f, 70f, FontStyles.Bold);
            CreateLabel(body, screen, 25f, 180f, FontStyles.Normal);
            back = CreateButton("Back", screen, "Back", false);
            return screen.gameObject;
        }

        private static RectTransform CreateContainer(string name, Transform parent)
        {
            RectTransform content = CreateRect(name, parent);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout);
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        private static void ConfigureLayout(VerticalLayoutGroup layout)
        {
            layout.spacing = 10f;
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
        }

        private static TMP_Text CreateLabel(string value, Transform parent, float size, float height, FontStyles style)
        {
            RectTransform rect = CreateRect(value.Length > 0 ? value : "Status", parent);
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            return CreateText(value, rect, size, style);
        }

        private static Button CreateButton(string name, Transform parent, string label, bool accent)
        {
            GameObject clone = (GameObject)PrefabUtility.InstantiatePrefab(slimButtonPrefab);
            clone.name = name;
            clone.transform.SetParent(parent, false);
            StripDemoButtonBehaviour(clone);
            RectTransform rect = clone.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            LayoutElement element = clone.GetComponent<LayoutElement>();
            if (element == null) element = clone.AddComponent<LayoutElement>();
            element.preferredHeight = 62f;
            Image image = clone.GetComponent<Image>();
            image.color = accent ? Accent : Color.white;
            Button button = clone.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.RemoveAllListeners();
            TMP_Text text = clone.GetComponentInChildren<TMP_Text>(true);
            if (!(text is TextMeshProUGUI))
            {
                GameObject textObject = text.gameObject;
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                Object.DestroyImmediate(text);
                MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
                if (renderer != null) Object.DestroyImmediate(renderer);
                MeshFilter filter = textObject.GetComponent<MeshFilter>();
                if (filter != null) Object.DestroyImmediate(filter);
                text = textObject.AddComponent<TextMeshProUGUI>();
                Stretch(textRect, 8f);
            }
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.text = label;
            text.fontSize = 24f;
            text.color = Color.white;
            text.raycastTarget = false;
            AddCursorVisual(rect, clone);
            return button;
        }

        private static void CreateThumbnail(Transform parent)
        {
            RectTransform rect = CreateRect("SlimUI Thumbnail", parent);
            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 92f;
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = thumbnailSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static void AddCursorVisual(RectTransform buttonRect, GameObject button)
        {
            RectTransform cursor = CreateRect("SlimUI Meta Hover Cursor", buttonRect);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0f, 0.5f);
            cursor.pivot = new Vector2(0.5f, 0.5f);
            cursor.anchoredPosition = new Vector2(22f, 0f);
            cursor.sizeDelta = new Vector2(28f, 28f);
            Image image = cursor.gameObject.AddComponent<Image>();
            image.sprite = cursorSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            SlimUIHoverCursor hover = button.AddComponent<SlimUIHoverCursor>();
            hover.Configure(cursor.gameObject);
        }

        private static void RemoveDemoComponents(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is CanvasScaler || behaviour is GraphicRaycaster) continue;
                Object.DestroyImmediate(behaviour);
            }
        }

        private static void StripDemoButtonBehaviour(GameObject button)
        {
            foreach (Transform child in button.GetComponentsInChildren<Transform>(true))
            {
                MonoBehaviour[] behaviours = child.GetComponents<MonoBehaviour>();
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour is Graphic || behaviour is Selectable || behaviour is LayoutElement) continue;
                    Object.DestroyImmediate(behaviour);
                }
            }
        }

        private static TMP_Text CreateText(string value, RectTransform parent, float size, FontStyles style)
        {
            RectTransform rect = CreateRect("Text", parent);
            Stretch(rect, 6f);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(1f, 1f, 1f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Color color, bool raycast)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Set(SerializedObject serialized, string property, Object value)
        {
            serialized.FindProperty(property).objectReferenceValue = value;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new System.InvalidOperationException("Missing SlimUI sprite: " + path);
            return sprite;
        }

        private static Sprite PrepareCursorSprite()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ThinkingSpace/UI/Graphics"))
                AssetDatabase.CreateFolder("Assets/ThinkingSpace/UI", "Graphics");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptedCursorPath) == null)
            {
                if (!AssetDatabase.CopyAsset(CursorPath, AdaptedCursorPath))
                    throw new System.InvalidOperationException("Could not copy SlimUI cursor to " + AdaptedCursorPath);
            }

            TextureImporter importer = AssetImporter.GetAtPath(AdaptedCursorPath) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException("Missing cursor importer: " + AdaptedCursorPath);
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return LoadSprite(AdaptedCursorPath);
        }
    }
}
#endif
