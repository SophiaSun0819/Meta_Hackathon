#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ThinkingSpace.Editor
{
    /// <summary>
    /// Meta XR 207 injects its local AgentBridge token into a Resources asset even when
    /// the development agent is disabled. Keep disabled builds and the tracked asset free
    /// of that credential without changing the Meta package or unrelated XR functionality.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class DisabledMetaDevAgentCredentialGuard :
        IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string SettingsAssetPath = "Assets/Resources/DevAgentSettings.asset";

        // Meta injects at order 1. Running last prevents a later build callback from
        // reintroducing the token before Resources are packaged.
        public int callbackOrder => int.MaxValue;

        static DisabledMetaDevAgentCredentialGuard()
        {
            ScheduleEditorSanitization();
            EditorApplication.projectChanged -= ScheduleEditorSanitization;
            EditorApplication.projectChanged += ScheduleEditorSanitization;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!TrySanitizeProjectAsset(true, out bool enabled, out string failure))
                throw new BuildFailedException(failure);
            if (!enabled && !IsProjectCredentialEmpty())
                throw new BuildFailedException(
                    "Disabled Meta DevAgent credential could not be removed before packaging.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            // Meta restores its pre-build token in its order-1 postprocessor. Because this
            // guard runs last, the tracked asset is clean again before control returns.
            if (!TrySanitizeProjectAsset(true, out _, out string failure))
                Debug.LogError("Thinking Space DevAgent credential cleanup failed: " + failure);
            ScheduleEditorSanitization();
        }

        internal static bool ShouldClear(bool enabled, string accessToken)
            => !enabled && !string.IsNullOrEmpty(accessToken);

        internal static bool TrySanitizeSerializedObject(SerializedObject serialized,
            out bool enabled, out bool changed)
        {
            enabled = false;
            changed = false;
            if (serialized == null || serialized.targetObject == null) return false;
            serialized.Update();
            SerializedProperty enabledProperty = serialized.FindProperty("enabled");
            SerializedProperty tokenProperty = serialized.FindProperty("accessToken");
            if (enabledProperty == null || tokenProperty == null
                || tokenProperty.propertyType != SerializedPropertyType.String) return false;
            enabled = enabledProperty.boolValue;
            if (!ShouldClear(enabled, tokenProperty.stringValue)) return true;
            tokenProperty.stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
            return true;
        }

        internal static bool IsProjectCredentialEmpty()
        {
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath);
            if (settings == null) return true;
            var serialized = new SerializedObject(settings);
            SerializedProperty token = serialized.FindProperty("accessToken");
            return token != null && string.IsNullOrEmpty(token.stringValue);
        }

        private static bool TrySanitizeProjectAsset(bool save, out bool enabled,
            out string failure)
        {
            enabled = false;
            failure = string.Empty;
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingsAssetPath);
            if (settings == null) return true;
            var serialized = new SerializedObject(settings);
            if (!TrySanitizeSerializedObject(serialized, out enabled, out bool changed))
            {
                failure = "Meta DevAgent settings schema was not recognized.";
                return false;
            }
            if (changed && save)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
            if (!enabled && !IsProjectCredentialEmpty())
            {
                failure = "Meta DevAgent credential remained populated after sanitization.";
                return false;
            }
            return true;
        }

        internal static bool NormalizeEmptyCredentialLine(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            byte[] source = File.ReadAllBytes(path);
            byte[] marker = Encoding.UTF8.GetBytes("accessToken:");
            using var output = new MemoryStream(source.Length);
            bool changed = false;
            int index = 0;
            while (index < source.Length)
            {
                int lineStart = index;
                while (index < source.Length && source[index] != 10 && source[index] != 13) index++;
                int lineEnd = index;
                int markerIndex = FindMarker(source, lineStart, lineEnd, marker);
                int valueStart = markerIndex < 0 ? -1 : markerIndex + marker.Length;
                bool emptyWithWhitespace = valueStart >= 0 && valueStart < lineEnd;
                for (int i = valueStart; emptyWithWhitespace && i < lineEnd; i++)
                    emptyWithWhitespace = source[i] == 32 || source[i] == 9;
                int writeEnd = emptyWithWhitespace ? valueStart : lineEnd;
                output.Write(source, lineStart, writeEnd - lineStart);
                changed |= emptyWithWhitespace;
                if (index < source.Length && source[index] == 13)
                {
                    output.WriteByte(source[index++]);
                    if (index < source.Length && source[index] == 10) output.WriteByte(source[index++]);
                }
                else if (index < source.Length) output.WriteByte(source[index++]);
            }
            if (changed) File.WriteAllBytes(path, output.ToArray());
            return changed;
        }

        private static int FindMarker(byte[] source, int start, int end, byte[] marker)
        {
            for (int i = start; i + marker.Length <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                    if (source[i + j] != marker[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        private static void ScheduleEditorSanitization()
        {
            EditorApplication.delayCall -= SanitizeAfterEditorInitialization;
            EditorApplication.delayCall += SanitizeAfterEditorInitialization;
        }

        private static void SanitizeAfterEditorInitialization()
        {
            if (!TrySanitizeProjectAsset(true, out _, out string failure))
                Debug.LogError("Thinking Space DevAgent credential cleanup failed: " + failure);
        }
    }
}
#endif
