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
    public static class PalmMenuButtonPrefabBuilder
    {
        private const string PrefabPath = "Assets/ThinkingSpace/UI/Prefabs/PalmMenuButton.prefab";
        private const string SpeechPrefabPath = "Assets/ThinkingSpace/Speech/Prefabs/PinchRecordingPrototype.prefab";
        private const string FontPath = "Assets/SlimUI/Modern Menu 1/Fonts/RUBIK-MEDIUM SDF.asset";
        private const string ButtonPath = "Assets/SlimUI/Modern Menu 1/Graphics/Buttons/Button Fram 256px.png";

        [MenuItem("Thinking Space/UI/Rebuild Palm Menu Button Prefab")]
        public static void BuildFromMenu() => Build();
        public static void BuildFromCommandLine() => Build();

        private static void Build()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
            if (font == null || sprite == null)
                throw new System.InvalidOperationException("Palm Menu button SlimUI font or sprite is missing.");

            GameObject root = new GameObject("PalmMenuButton", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(Image), typeof(Button), typeof(PalmMenuButtonView));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(180f, 100f);
            rect.localScale = Vector3.one * 0.0003f;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 25;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
            Image image = root.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.12f, 0.68f, 1f, 1f);
            image.raycastTarget = true;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;

            PointableCanvas pointable = root.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(canvas);
            PlaneSurface plane = root.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            BoundsClipper bounds = root.AddComponent<BoundsClipper>();
            bounds.Size = new Vector3(180f, 100f, 0.01f);
            ClippedPlaneSurface clipped = root.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { bounds });
            RayInteractable ray = root.AddComponent<RayInteractable>();
            ray.InjectAllRayInteractable(clipped);
            ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke = root.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.InjectOptionalPointableElement(pointable);
            SerializedObject pokeSettings = new SerializedObject(poke);
            pokeSettings.FindProperty("_enterHoverNormal").floatValue = 0.03f;
            pokeSettings.FindProperty("_exitHoverNormal").floatValue = 0.05f;
            SerializedProperty drag = pokeSettings.FindProperty("_dragThresholds");
            drag.FindPropertyRelative("Enabled").boolValue = true;
            drag.FindPropertyRelative("DragNormal").floatValue = 0.01f;
            drag.FindPropertyRelative("DragTangent").floatValue = 0.01f;
            pokeSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject labelObject = new GameObject("Menu Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 8f);
            labelRect.offsetMax = new Vector2(-8f, -8f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.text = "Menu";
            label.fontSize = 42f;
            label.enableAutoSizing = false;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            SerializedObject view = new SerializedObject(root.GetComponent<PalmMenuButtonView>());
            Set(view, "worldCanvas", canvas); Set(view, "graphicRaycaster", raycaster);
            Set(view, "pointableCanvas", pointable); Set(view, "planeSurface", plane);
            Set(view, "boundsClipper", bounds); Set(view, "clippedPlaneSurface", clipped);
            Set(view, "rayInteractable", ray); Set(view, "pokeInteractable", poke);
            Set(view, "menuButton", button); Set(view, "buttonImage", image); Set(view, "label", label);
            view.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssignToSpeechPrefab(prefab.GetComponent<PalmMenuButtonView>());
            AssetDatabase.SaveAssets();
            Debug.Log("Built authored palm Menu button at " + PrefabPath);
        }

        private static void AssignToSpeechPrefab(PalmMenuButtonView view)
        {
            GameObject speechRoot = PrefabUtility.LoadPrefabContents(SpeechPrefabPath);
            BrainstormMenuController controller = speechRoot.GetComponent<BrainstormMenuController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("palmMenuButtonPrefab").objectReferenceValue = view;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(speechRoot, SpeechPrefabPath);
            PrefabUtility.UnloadPrefabContents(speechRoot);
        }

        private static void Set(SerializedObject serialized, string name, Object value)
        {
            serialized.FindProperty(name).objectReferenceValue = value;
        }
    }
}
#endif
