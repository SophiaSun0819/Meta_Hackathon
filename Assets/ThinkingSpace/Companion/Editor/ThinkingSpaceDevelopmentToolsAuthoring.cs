#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ThinkingSpace.Companion.Editor
{
    [InitializeOnLoad]
    internal static class ThinkingSpaceDevelopmentToolsAuthoring
    {
        static ThinkingSpaceDevelopmentToolsAuthoring()
        {
            EditorApplication.delayCall += EnsureAuthoredTools;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
        }

        private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.name == "Sandbox") EditorApplication.delayCall += EnsureAuthoredTools;
        }

        [MenuItem("Thinking Space/Development/Ensure Development Tools In Sandbox")]
        private static void EnsureAuthoredTools()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.name != "Sandbox") return;

            GameObject root = GameObject.Find("ThinkingSpace Development Tools");
            bool changed = false;
            if (root == null)
            {
                root = new GameObject("ThinkingSpace Development Tools");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.tag = "EditorOnly";
                Undo.RegisterCreatedObjectUndo(root, "Create Thinking Space Development Tools");
                changed = true;
            }
            if (root.GetComponent<ThinkingSpaceDevelopmentReset>() == null)
            {
                Undo.AddComponent<ThinkingSpaceDevelopmentReset>(root);
                changed = true;
            }
            if (root.GetComponent<ThinkingSpaceDevelopmentTools>() == null)
            {
                Undo.AddComponent<ThinkingSpaceDevelopmentTools>(root);
                changed = true;
            }
            if (changed) EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    [CustomEditor(typeof(ThinkingSpaceDevelopmentTools))]
    internal sealed class ThinkingSpaceDevelopmentToolsInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            ThinkingSpaceDevelopmentTools tools = (ThinkingSpaceDevelopmentTools)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Safe Runtime Tests", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Show Full Menu At Headset")) tools.ShowMenu();
                if (GUILayout.Button("Hide Full Menu")) tools.HideMenu();
                if (GUILayout.Button("Simulate One Menu Callback")) tools.SimulateMenuCallback();
                if (GUILayout.Button("Test Prerecorded Orby Playback")) tools.TestPrerecordedOrbyPlayback();
                if (GUILayout.Button("Inspect Session / Recording Eligibility")) tools.InspectSessionAndRecordingEligibility();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Destructive Development Reset", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Every reset requires a request followed by Confirm. A timestamped backup is created before data changes.", MessageType.Warning);
                if (GUILayout.Button("1 · Request Onboarding Reset")) tools.RequestOnboardingReset();
                if (GUILayout.Button("1 · Request Sessions Reset")) tools.RequestSessionsReset();
                if (GUILayout.Button("1 · Request Full Development Reset")) tools.RequestFullReset();
                if (GUILayout.Button("2 · Confirm Requested Reset")) tools.ConfirmReset();
                if (GUILayout.Button("Cancel Pending Reset")) tools.CancelReset();
            }
        }
    }
}
#endif
