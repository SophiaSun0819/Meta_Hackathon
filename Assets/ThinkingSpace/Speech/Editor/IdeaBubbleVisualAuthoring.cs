using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThinkingSpace.Speech.Editor
{
    internal static class IdeaBubbleVisualAuthoring
    {
        private const string ModelPath = "Assets/Orbi.fbx";
        private const string OriginalMaterialPath = "Assets/Models/Bubble_Mat.mat";
        private const string MaterialFolder = "Assets/ThinkingSpace/Speech/Materials";
        private const string MaterialPath = MaterialFolder + "/IdeaBubble_Mat.mat";
        private const string ResourcesFolder = "Assets/ThinkingSpace/Speech/Resources";
        private const string PrefabPath = ResourcesFolder + "/IdeaBubbleVisual.prefab";

        [MenuItem("Thinking Space/Ensure Idea Bubble Visual Assets")]
        public static void EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null
                && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;

            Material original = AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterialPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (original == null || model == null)
            {
                Debug.LogError("Idea bubble visual extraction requires Assets/Orbi.fbx and "
                    + "Assets/Models/Bubble_Mat.mat.");
                return;
            }

            Transform bubble = FindChild(model.transform, "Bubble");
            MeshFilter sourceFilter = bubble != null ? bubble.GetComponent<MeshFilter>() : null;
            MeshRenderer sourceRenderer = bubble != null ? bubble.GetComponent<MeshRenderer>() : null;
            if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null)
            {
                Debug.LogError("The imported Orbi Bubble must contain a MeshFilter and MeshRenderer.");
                return;
            }

            EnsureFolder("Assets/ThinkingSpace/Speech", "Materials");
            EnsureFolder("Assets/ThinkingSpace/Speech", "Resources");
            Material ideaMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (ideaMaterial == null)
            {
                ideaMaterial = new Material(original) { name = "IdeaBubble_Mat" };
                ConfigureMaterial(ideaMaterial);
                AssetDatabase.CreateAsset(ideaMaterial, MaterialPath);
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                GameObject root = new GameObject("IdeaBubbleVisual");
                GameObject meshObject = new GameObject("Bubble Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                meshObject.transform.SetParent(root.transform, false);
                Mesh mesh = sourceFilter.sharedMesh;
                meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = meshObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = ideaMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Bounds bounds = mesh.bounds;
                float diameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float normalization = diameter > 0.0001f ? 1f / diameter : 1f;
                meshObject.transform.localScale = Vector3.one * normalization;
                meshObject.transform.localPosition = -bounds.center * normalization;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Object.DestroyImmediate(root);
                Debug.Log($"Created IdeaBubbleVisual from imported Bubble mesh '{mesh.name}' "
                    + $"with source diameter {diameter:F5}.");
            }
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureMaterial(Material material)
        {
            Color baseColor = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor") : Color.white;
            baseColor.a = 0.80f;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", new Color(baseColor.r, baseColor.g, baseColor.b) * 0.15f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
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

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
