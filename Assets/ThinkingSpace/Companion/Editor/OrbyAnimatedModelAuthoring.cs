using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ThinkingSpace.Companion.Editor
{
    public static class OrbyAnimatedModelAuthoring
    {
        private const string ScenePath = "Assets/Scenes/Sandbox.unity";
        private const string PrefabPath = "Assets/Models/OrbiM_N.prefab";
        private const float PriorWorldScale = 0.06654542f;

        [MenuItem("Thinking Space/Companion/Integrate Animated Orby In Sandbox")]
        public static void IntegrateSandbox()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new System.IO.FileNotFoundException("Animated Orby prefab is missing.", PrefabPath);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject animated = roots.FirstOrDefault(root => root.name == "OrbiM_N");
            GameObject legacy = roots.FirstOrDefault(root => root.name == "Orbi");
            Vector3 position = animated != null ? animated.transform.position :
                legacy != null ? legacy.transform.position : new Vector3(0f, 1.5f, 0.9f);
            Quaternion facing = legacy != null ? legacy.transform.rotation : Quaternion.Euler(0f, 180f, 0f);

            if (animated != null) Object.DestroyImmediate(animated);
            if (legacy != null) Object.DestroyImmediate(legacy);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) throw new System.InvalidOperationException("Could not instantiate animated Orby prefab.");
            instance.name = "OrbiM_N";
            instance.transform.SetPositionAndRotation(position, facing);
            instance.transform.localScale = Vector3.one * PriorWorldScale;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("UNITY047_ORBY_SCENE_OK one animated prefab; scale=" + PriorWorldScale.ToString("0.00000000"));
        }
    }
}
