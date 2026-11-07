using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    public enum OrbyVoicePriority { IdleReminder=0, Acknowledgement=10, Tutorial=20, AIResponse=30, Critical=40 }

    [Serializable]
    public sealed class OrbyPrerecordedEntry
    {
        public string EventKey;
        public AudioClip Clip;
        [TextArea] public string ExactSpokenText;
        [TextArea] public string SubtitleText;
        public bool TranscriptVerified;
        public OrbyVoicePriority Priority;
        public string AllowedTriggerConditions;
    }

}
