using System;
using UnityEngine;

namespace ThinkingSpace.Audio
{
    public enum ThinkingSpaceSfxCue
    {
        UiHover, UiSelect, MenuOpen, MenuClose,
        BubbleCreate, BubbleGrab, BubbleRelease, BubbleConnect, BubbleDelete, BubbleHover, BubbleSnap,
        RecordingStart, RecordingStop, RecordingTick, SessionSave, GentleError, UiBack
    }

    /// <summary>Small allocation-free playback pool isolated from voice/TTS AudioSources.</summary>
    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceSfxManager : MonoBehaviour
    {
        private const string ResourceRoot = "ThinkingSpaceSFX/";
        private const int UiVoices = 3, SpatialVoices = 5;
        private static readonly string[] ResourceNames =
        {
            "TS_UI_Hover_01", "TS_UI_Select_01", "TS_UI_MenuOpen_01", "TS_UI_MenuClose_01",
            "TS_Bubble_Create_01", "TS_Bubble_Grab_01", "TS_Bubble_Release_01",
            "TS_Bubble_Connect_01", "TS_Bubble_Delete_01", "TS_Bubble_Hover_01", "TS_Bubble_Snap_01",
            "TS_Recording_Start_01", "TS_Recording_Stop_01", "TS_Recording_Tick_01",
            "TS_Session_Save_01", "TS_UI_ErrorGentle_01", "TS_UI_Back_01"
        };
        private static readonly float[] Cooldowns =
        { .08f, .04f, .18f, .18f, .15f, .08f, .08f, .15f, .18f, .10f, .08f,
          .25f, .25f, .50f, .65f, .35f, .12f };
        private static readonly float[] Gains =
        { .20f, .28f, .25f, .23f, .28f, .30f, .27f, .32f, .26f, .17f, .25f,
          .32f, .29f, .14f, .30f, .25f, .20f };

        private static ThinkingSpaceSfxManager instance;
        [SerializeField, Range(0f, 1f)] private float masterVolume = .42f;
        private readonly AudioClip[] clips = new AudioClip[ResourceNames.Length];
        private readonly float[] lastPlayed = new float[ResourceNames.Length];
        private AudioSource[] uiSources, spatialSources;
        private int nextUi, nextSpatial;

        public static int CueCount => ResourceNames.Length;
        public int MissingClipCount { get; private set; }
        public float MasterVolume { get => masterVolume; set => masterVolume = Mathf.Clamp01(value); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var root = new GameObject("Thinking Space SFX");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<ThinkingSpaceSfxManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            for (int i = 0; i < lastPlayed.Length; i++) lastPlayed[i] = float.NegativeInfinity;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = Resources.Load<AudioClip>(ResourceRoot + ResourceNames[i]);
                if (clips[i] == null) MissingClipCount++;
            }
            uiSources = BuildPool("UI", UiVoices, 0f);
            spatialSources = BuildPool("Spatial", SpatialVoices, .82f);
            if (MissingClipCount > 0) Debug.LogWarning($"Thinking Space SFX: {MissingClipCount} clip(s) missing.", this);
        }

        private AudioSource[] BuildPool(string label, int count, float spatialBlend)
        {
            var pool = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject(label + " Voice " + (i + 1));
                child.transform.SetParent(transform, false);
                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = spatialBlend;
                source.dopplerLevel = 0f; source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = .25f; source.maxDistance = 5f; source.priority = 160;
                pool[i] = source;
            }
            return pool;
        }

        public static string ResourceName(ThinkingSpaceSfxCue cue) => ResourceNames[(int)cue];
        public static bool IsSpatial(ThinkingSpaceSfxCue cue)
            => cue >= ThinkingSpaceSfxCue.BubbleCreate && cue <= ThinkingSpaceSfxCue.BubbleSnap;
        public static float Cooldown(ThinkingSpaceSfxCue cue) => Cooldowns[(int)cue];

        public static void PlayUi(ThinkingSpaceSfxCue cue) => instance?.Play(cue, default, false);
        public static void PlayAt(ThinkingSpaceSfxCue cue, Vector3 position) => instance?.Play(cue, position, true);

        private void Play(ThinkingSpaceSfxCue cue, Vector3 position, bool spatial)
        {
            int index = (int)cue;
            if (index < 0 || index >= clips.Length || clips[index] == null) return;
            float now = Time.unscaledTime;
            if (now - lastPlayed[index] < Cooldowns[index]) return;
            lastPlayed[index] = now;
            AudioSource[] pool = spatial ? spatialSources : uiSources;
            int voice = spatial ? nextSpatial++ % pool.Length : nextUi++ % pool.Length;
            AudioSource source = pool[voice];
            if (spatial) source.transform.position = position;
            source.Stop();
            source.PlayOneShot(clips[index], masterVolume * Gains[index]);
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
