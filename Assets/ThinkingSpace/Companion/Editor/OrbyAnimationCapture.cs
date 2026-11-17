using System.IO;
using UnityEditor;
using UnityEngine;

namespace ThinkingSpace.Companion.Editor
{
    public static class OrbyAnimationCapture
    {
        private const string PrefabPath = "Assets/Models/OrbiM_N.prefab";
        private const string OutputFolder = "C:/Users/Henry/Documents/Codex/UNITY-047-orby-animation";

        [MenuItem("Thinking Space/Companion/Capture Orby Animation Reference")]
        public static void Capture()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new FileNotFoundException("Animated Orby prefab is missing.", PrefabPath);
            Directory.CreateDirectory(OutputFolder);

            GameObject instance = null;
            GameObject cameraObject = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                BubbleMouthSwap mouth = instance.GetComponentInChildren<BubbleMouthSwap>(true);
                if (mouth == null || !mouth.ResolveAuthoredMouths())
                    throw new System.InvalidOperationException("Animated Orby mouth hierarchy is incomplete.");

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                cameraObject = new GameObject("UNITY-047 Orby Capture Camera", typeof(Camera));
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.015f, 0.025f, 0.06f, 1f);
                camera.fieldOfView = 38f;
                camera.nearClipPlane = 0.01f;
                float distance = Mathf.Max(bounds.extents.magnitude * 2.25f, 0.5f);
                camera.transform.position = bounds.center + Vector3.forward * distance;
                camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);

                mouth.ResetToRest();
                Render(camera, "01-rest-closed.png");
                CaptureOpenFrame(camera, mouth, 0.25f, 0.15f, "02-speech-low.png");
                CaptureOpenFrame(camera, mouth, 0.62f, 0.5f, "03-speech-medium.png");
                CaptureOpenFrame(camera, mouth, 1f, 0.85f, "04-speech-peak.png");
                mouth.ResetToRest();
                Debug.Log("UNITY047_ORBY_CAPTURE_OK folder=" + OutputFolder);
            }
            finally
            {
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        private static void CaptureOpenFrame(Camera camera, BubbleMouthSwap mouth, float level,
            float vowel, string fileName)
        {
            mouth.ClosedMouth.SetActive(false);
            mouth.OpenMouth.gameObject.SetActive(true);
            mouth.OpenMouth.localScale = BubbleMouthSwap.CalculateMouthScale(
                Vector3.one, level, vowel, 0.82f, 1.16f, 0.94f, 1.06f);
            Render(camera, fileName);
        }

        private static void Render(Camera camera, string fileName)
        {
            RenderTexture target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            Texture2D image = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(OutputFolder, fileName), image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
        }
    }
}
