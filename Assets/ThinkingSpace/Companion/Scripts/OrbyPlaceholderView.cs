using TMPro;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbyPlaceholderView : MonoBehaviour
    {
        [SerializeField] private OrbyConversationStateController stateController;
        [SerializeField] private Transform headset;
        [SerializeField] private Vector3 headsetOffset = new Vector3(0f, -0.10f, 0.75f);
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.22f;
        [SerializeField, Min(0f)] private float floatAmplitude = 0.025f;
        [SerializeField, Min(0f)] private float floatSpeed = 1.5f;
        private Transform visual;
        private TextMeshPro label;
        private Material material;
        private Vector3 velocity;
        private OrbyConversationState currentState;
        private float liveAudioLevel, smoothedAudioLevel, silenceProgress;
        private bool silenceCountdown;

        private void Awake()
        {
            if (stateController == null) stateController = GetComponent<OrbyConversationStateController>();
            BuildReplaceableVisual();
            ApplyState(stateController != null ? stateController.State : OrbyConversationState.Idle);
        }
        private void OnEnable()
        { if (stateController != null) stateController.StateChanged += HandleStateChanged; }
        private void OnDisable()
        { if (stateController != null) stateController.StateChanged -= HandleStateChanged; }

        private void LateUpdate()
        {
            if (headset == null && Camera.main != null) headset = Camera.main.transform;
            if (headset == null) return;
            Vector3 target = headset.TransformPoint(headsetOffset);
            transform.position = Vector3.SmoothDamp(transform.position, target, ref velocity, followSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(transform.position - headset.position, Vector3.up),
                8f * Time.unscaledDeltaTime);
            if (visual != null) visual.localPosition = Vector3.up
                * (Mathf.Sin(Time.unscaledTime * floatSpeed * Mathf.PI * 2f) * floatAmplitude);
            if (visual != null)
            {
                smoothedAudioLevel = Mathf.Lerp(smoothedAudioLevel, liveAudioLevel,
                    1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.08f));
                float pulse = currentState == OrbyConversationState.SpeechActive
                    ? Mathf.Clamp01(smoothedAudioLevel * 35f) * 0.16f : 0f;
                if (silenceCountdown) pulse += (1f - silenceProgress) * 0.04f;
                visual.localScale = Vector3.one * 0.13f * (1f + pulse);
                if (currentState == OrbyConversationState.Thinking)
                    visual.localRotation = Quaternion.Euler(0f, Time.unscaledTime * 75f, 0f);
                else visual.localRotation = Quaternion.identity;
            }
        }

        public void SetHeadset(Transform value) => headset = value;
        public void SetLiveAudioLevel(float rms, bool isSilenceCountdown, float remainingNormalized)
        {
            liveAudioLevel = Mathf.Max(0f, rms);
            silenceCountdown = isSilenceCountdown;
            silenceProgress = Mathf.Clamp01(remainingNormalized);
            if (label != null && isSilenceCountdown)
                label.text = "Orby · Finishing thought…";
        }
        private void HandleStateChanged(OrbyConversationState previous, OrbyConversationState next) => ApplyState(next);
        private void ApplyState(OrbyConversationState state)
        {
            currentState = state;
            if (state != OrbyConversationState.SpeechActive)
            { liveAudioLevel = 0f; silenceCountdown = false; }
            if (label != null) label.text = "Orby · " + state;
            if (material == null) return;
            Color color = state == OrbyConversationState.Listening ? new Color(0.2f, 0.85f, 1f) :
                state == OrbyConversationState.SpeechActive ? new Color(0.2f, 1f, 0.72f) :
                state == OrbyConversationState.Thinking ? new Color(0.75f, 0.45f, 1f) :
                state == OrbyConversationState.Speaking ? new Color(0.25f, 1f, 0.55f) :
                state == OrbyConversationState.Paused ? new Color(1f, 0.7f, 0.2f) :
                state == OrbyConversationState.Reviewing ? new Color(0.25f, 0.65f, 1f) :
                state == OrbyConversationState.Error ? new Color(1f, 0.2f, 0.2f) : new Color(0.45f, 0.7f, 1f);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private void BuildReplaceableVisual()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Placeholder Visual (Replace With Final Orby Model)";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(transform, false); sphere.transform.localScale = Vector3.one * 0.13f;
            visual = sphere.transform;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            material = new Material(shader) { name = "Orby Placeholder Runtime Material" };
            sphere.GetComponent<Renderer>().sharedMaterial = material;

            GameObject textObject = new GameObject("Status Label");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            label = textObject.AddComponent<TextMeshPro>();
            label.fontSize = 0.9f;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.35f, 0.04f);
        }
    }

    public static class OrbyPlaceholderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsurePlaceholderForRuntime()
        {
            if (Object.FindFirstObjectByType<OrbyConversationStateController>() != null) return;
            GameObject root = new GameObject("Orby Placeholder Companion");
            if (Application.isPlaying) Object.DontDestroyOnLoad(root);
            root.AddComponent<OrbyConversationStateController>();
            root.AddComponent<OrbyPlaceholderView>();
        }
    }
}
