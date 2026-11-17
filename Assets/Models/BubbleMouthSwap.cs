using UnityEngine;

namespace ThinkingSpace.Companion
{
    /// <summary>
    /// Drives the authored mouth meshes from Orby's authoritative playback source.
    /// It deliberately never owns, starts, or stops audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BubbleMouthSwap : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AudioSource voice;
        [SerializeField] private GameObject closedMouth;
        [SerializeField] private Transform openMouth;

        [Header("Natural motion")]
        [SerializeField, Range(1f, 40f)] private float sensitivity = 14f;
        [SerializeField, Range(0.01f, 0.5f)] private float openThreshold = 0.065f;
        [SerializeField, Range(0.001f, 0.25f)] private float closeThreshold = 0.028f;
        [SerializeField, Min(0.01f)] private float attackSeconds = 0.055f;
        [SerializeField, Min(0.01f)] private float releaseSeconds = 0.11f;
        [SerializeField, Range(0.5f, 1.5f)] private float minimumHeight = 0.82f;
        [SerializeField, Range(0.5f, 1.5f)] private float maximumHeight = 1.16f;
        [SerializeField, Range(0.5f, 1.5f)] private float narrowWidth = 0.94f;
        [SerializeField, Range(0.5f, 1.5f)] private float wideWidth = 1.06f;
        [SerializeField, Min(0f)] private float vowelSpeed = 4.5f;

        private readonly float[] samples = new float[256];
        private Vector3 openBase = Vector3.one;
        private float level;
        private bool mouthOpen;

        public AudioSource VoiceSource => voice;
        public bool IsTalking { get; private set; }
        public bool IsMouthOpen => mouthOpen;
        public GameObject ClosedMouth => closedMouth;
        public Transform OpenMouth => openMouth;

        private void Awake()
        {
            ResolveAuthoredMouths();
            ResetToRest();
        }

        public bool ResolveAuthoredMouths()
        {
            if (closedMouth == null)
            {
                Transform closed = FindDescendant(transform, "ClosedMouth");
                if (closed != null) closedMouth = closed.gameObject;
            }
            if (openMouth == null) openMouth = FindDescendant(transform, "MouthOpen");
            if (openMouth != null) openBase = openMouth.localScale;
            return closedMouth != null && openMouth != null;
        }

        public void BindVoiceSource(AudioSource authoritativeSource)
        {
            voice = authoritativeSource;
            ResetToRest();
        }

        public void ResetToRest()
        {
            level = 0f;
            mouthOpen = false;
            IsTalking = false;
            SetOpen(false);
            if (openMouth != null) openMouth.localScale = openBase;
        }

        private void Update()
        {
            bool playbackActive = voice != null && voice.enabled && !voice.mute &&
                voice.volume > 0.0001f && voice.isPlaying;
            float target = playbackActive ? SampleOutputLevel(voice, samples, sensitivity) : 0f;
            level = SmoothLevel(level, target, attackSeconds, releaseSeconds, Time.unscaledDeltaTime);
            mouthOpen = ShouldMouthOpen(mouthOpen, level, openThreshold, closeThreshold) && playbackActive;
            IsTalking = playbackActive;
            SetOpen(mouthOpen);

            if (mouthOpen && openMouth != null)
            {
                float normalized = Mathf.InverseLerp(closeThreshold, 1f, level);
                float vowel = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * vowelSpeed);
                openMouth.localScale = CalculateMouthScale(openBase, normalized, vowel,
                    minimumHeight, maximumHeight, narrowWidth, wideWidth);
            }
            else if (openMouth != null) openMouth.localScale = openBase;
        }

        private void OnDisable() => ResetToRest();
        private void OnDestroy() => ResetToRest();

        public static float SmoothLevel(float current, float target, float attack, float release,
            float deltaTime)
        {
            float duration = target > current ? attack : release;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(0.001f, duration));
            return Mathf.Lerp(current, Mathf.Clamp01(target), blend);
        }

        public static bool ShouldMouthOpen(bool currentlyOpen, float currentLevel,
            float openAt, float closeAt)
        {
            float open = Mathf.Max(0.001f, openAt);
            float close = Mathf.Clamp(closeAt, 0f, open);
            return currentlyOpen ? currentLevel > close : currentLevel >= open;
        }

        public static Vector3 CalculateMouthScale(Vector3 baseScale, float level, float vowel,
            float minHeight, float maxHeight, float minWidth, float maxWidth)
        {
            float width = Mathf.Lerp(minWidth, maxWidth, Mathf.Clamp01(vowel));
            float height = Mathf.Lerp(minHeight, maxHeight, Mathf.Clamp01(level));
            return new Vector3(baseScale.x * width, baseScale.y * height, baseScale.z);
        }

        private static float SampleOutputLevel(AudioSource source, float[] buffer, float gain)
        {
            source.GetOutputData(buffer, 0);
            float sum = 0f;
            for (int i = 0; i < buffer.Length; i++) sum += buffer[i] * buffer[i];
            return Mathf.Clamp01(Mathf.Sqrt(sum / buffer.Length) * Mathf.Max(0f, gain));
        }

        private static Transform FindDescendant(Transform root, string targetName)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
                if (descendants[i].name == targetName) return descendants[i];
            return null;
        }

        private void SetOpen(bool open)
        {
            if (closedMouth != null && closedMouth.activeSelf == open) closedMouth.SetActive(!open);
            if (openMouth != null && openMouth.gameObject.activeSelf != open)
                openMouth.gameObject.SetActive(open);
        }
    }
}
