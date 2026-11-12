using NUnit.Framework;
using ThinkingSpace.Speech;
using UnityEditor;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class IdeaBubbleMaterialVisualTests
    {
        [Test]
        public void EmissionStatePrecedenceMatchesInteractionPolicy()
        {
            Assert.That(IdeaBubbleView.EmissionMultiplier(false, true,
                IdeaBubbleVisualState.Selected), Is.EqualTo(0.10f));
            Assert.That(IdeaBubbleView.EmissionMultiplier(true, true,
                IdeaBubbleVisualState.Selected), Is.EqualTo(1.00f));
            Assert.That(IdeaBubbleView.EmissionMultiplier(true, false,
                IdeaBubbleVisualState.Selected), Is.EqualTo(0.85f));
            Assert.That(IdeaBubbleView.EmissionMultiplier(true, false,
                IdeaBubbleVisualState.Open), Is.EqualTo(0.55f));
            Assert.That(IdeaBubbleView.EmissionMultiplier(true, false,
                IdeaBubbleVisualState.Hovered), Is.EqualTo(0.40f));
            Assert.That(IdeaBubbleView.EmissionMultiplier(true, false,
                IdeaBubbleVisualState.Idle), Is.EqualTo(0.20f));
            Assert.That(IdeaBubbleView.Opacity(false, true,
                IdeaBubbleVisualState.Selected), Is.EqualTo(0.75f));
            Assert.That(IdeaBubbleView.Opacity(true, true,
                IdeaBubbleVisualState.Selected), Is.EqualTo(0.95f));
            Assert.That(IdeaBubbleView.Opacity(true, false,
                IdeaBubbleVisualState.Selected), Is.EqualTo(0.95f));
            Assert.That(IdeaBubbleView.Opacity(true, false,
                IdeaBubbleVisualState.Open), Is.EqualTo(0.88f));
            Assert.That(IdeaBubbleView.Opacity(true, false,
                IdeaBubbleVisualState.Hovered), Is.EqualTo(0.85f));
            Assert.That(IdeaBubbleView.Opacity(true, false,
                IdeaBubbleVisualState.Idle), Is.EqualTo(0.80f));
        }

        [Test]
        public void CategoryColorIsStableAndCategorySpecific()
        {
            Color first = IdeaBubbleView.CategoryColor("Education");
            Assert.That(IdeaBubbleView.CategoryColor("education"), Is.EqualTo(first));
            Assert.That(IdeaBubbleView.CategoryColor("Technology"), Is.Not.EqualTo(first));
            Color.RGBToHSV(first, out _, out float saturation, out _);
            Assert.That(saturation, Is.EqualTo(0.78f).Within(0.01f));
        }

        [Test]
        public void DedicatedMaterialPreservesOriginalAndUsesIdeaOpacity()
        {
            Material original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Bubble_Mat.mat");
            Material idea = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/ThinkingSpace/Speech/Materials/IdeaBubble_Mat.mat");
            Assert.That(original, Is.Not.Null);
            Assert.That(idea, Is.Not.Null);
            Assert.That(idea, Is.Not.SameAs(original));
            Assert.That(idea.shader, Is.SameAs(original.shader));
            Assert.That(idea.GetColor("_BaseColor").a, Is.EqualTo(0.80f).Within(0.001f));
            Assert.That(idea.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
            Assert.That(idea.IsKeywordEnabled("_EMISSION"), Is.True);
        }

        [Test]
        public void PrefabReferencesTheImportedBubbleMesh()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Orbi.fbx");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThinkingSpace/Speech/Resources/IdeaBubbleVisual.prefab");
            Assert.That(model, Is.Not.Null);
            Assert.That(prefab, Is.Not.Null);
            MeshFilter source = FindChild(model.transform, "Bubble").GetComponent<MeshFilter>();
            MeshFilter extracted = prefab.GetComponentInChildren<MeshFilter>(true);
            Assert.That(extracted, Is.Not.Null);
            Assert.That(extracted.sharedMesh, Is.SameAs(source.sharedMesh));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindChild(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
