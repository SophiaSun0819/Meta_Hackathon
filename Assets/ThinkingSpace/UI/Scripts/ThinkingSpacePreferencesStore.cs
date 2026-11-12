using System;
using System.IO;
using UnityEngine;
using ThinkingSpace.Speech;

namespace ThinkingSpace.UI
{
    public enum StartupBehavior { PreserveCurrentBehavior }
    public enum WristMenuHand { Left, Right }

    [Serializable]
    public sealed class ThinkingSpacePreferencesData
    {
        public int SchemaVersion = ThinkingSpacePreferencesStore.CurrentSchemaVersion;
        public float SilenceDurationSeconds = 2f;
        public ThumbRecordingGestureController.RecordingMode RecordingMode =
            ThumbRecordingGestureController.RecordingMode.Conversational;
        public StartupBehavior StartupBehavior = StartupBehavior.PreserveCurrentBehavior;
        public bool SubtitlesEnabled = true;
        public string PreferredName = string.Empty;
        public bool WelcomeCompleted;
        public bool MenuTutorialCompleted;
        public bool FirstSessionTutorialCompleted;
        public bool FirstReviewOptionsCompleted;
        public bool PersonalizedGreetingCompleted;
        public WristMenuHand WristMenuHand = WristMenuHand.Left;
        public string InterfaceLanguageCode = ThinkingSpacePreferencesStore.DefaultInterfaceLanguageCode;
        public string ConversationLanguageMode = ThinkingSpacePreferencesStore.DefaultConversationLanguageMode;
        public string ConversationLanguageCode = string.Empty;
        public string VoiceProfileId = ThinkingSpacePreferencesStore.DefaultVoiceProfileId;
    }

    public sealed class ThinkingSpacePreferencesStore
    {
        public const int CurrentSchemaVersion = 5;
        public const string FileName = "preferences-v1.json";
        public const string DefaultInterfaceLanguageCode = "en";
        public const string DefaultConversationLanguageMode = "auto";
        public const string DefaultVoiceProfileId = "orby-auto";
        public static readonly string[] SupportedConversationLanguages = { "en", "es", "fr", "zh-CN", "hi-IN" };
        public static readonly string[] SupportedVoiceProfiles = { "orby-auto", "orby-en", "orby-es", "orby-fr", "orby-zh", "orby-hi" };
        public const float MinimumSilenceSeconds = 1f, MaximumSilenceSeconds = 10f, StepSeconds = 0.5f;
        private readonly string directory, path, backupPath;
        public ThinkingSpacePreferencesData Data { get; private set; } = new ThinkingSpacePreferencesData();
        public string PersistencePath => path;
        public string LastError { get; private set; } = string.Empty;

        public static ThinkingSpacePreferencesStore CreateForApplication() =>
            new ThinkingSpacePreferencesStore(Path.Combine(Application.persistentDataPath, "ThinkingSpace"));

        public ThinkingSpacePreferencesStore(string storageDirectory)
        {
            directory = storageDirectory;
            path = Path.Combine(directory, FileName);
            backupPath = path + ".backup";
            Load();
        }

        public static float ClampSilence(float value) =>
            Mathf.Clamp(Mathf.Round(value / StepSeconds) * StepSeconds,
                MinimumSilenceSeconds, MaximumSilenceSeconds);

        public bool SetSilenceDuration(float value)
        {
            float previous = Data.SilenceDurationSeconds;
            Data.SilenceDurationSeconds = ClampSilence(value);
            if (Save()) return true;
            Data.SilenceDurationSeconds = previous;
            return false;
        }

        public bool SetRecordingMode(ThumbRecordingGestureController.RecordingMode value)
        {
            if (!Enum.IsDefined(typeof(ThumbRecordingGestureController.RecordingMode), value)) return false;
            var previous = Data.RecordingMode;
            Data.RecordingMode = value;
            if (Save()) return true;
            Data.RecordingMode = previous;
            return false;
        }

        public bool SetSubtitlesEnabled(bool value) => Mutate(() => Data.SubtitlesEnabled = value);
        public bool SetWristMenuHand(WristMenuHand value)
        {
            if (!Enum.IsDefined(typeof(WristMenuHand), value)) return false;
            return Mutate(() => Data.WristMenuHand = value);
        }
        public bool SetInterfaceLanguage(string languageCode)
        {
            string normalized = NormalizeInterfaceLanguage(languageCode);
            if (normalized == null) return false;
            return Mutate(() => Data.InterfaceLanguageCode = normalized);
        }

        public bool SetConversationLanguage(string mode, string languageCode = null)
        {
            string normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedMode != "auto" && normalizedMode != "explicit") return false;
            string normalizedCode = NormalizeConversationLanguage(languageCode);
            if (normalizedMode == "explicit" && string.IsNullOrEmpty(normalizedCode)) return false;
            return Mutate(() =>
            {
                Data.ConversationLanguageMode = normalizedMode;
                Data.ConversationLanguageCode = normalizedMode == "auto" ? string.Empty : normalizedCode;
            });
        }

        public bool SetVoiceProfileId(string value)
        {
            string normalized = NormalizeVoiceProfile(value);
            if (normalized == null) return false;
            return Mutate(() => Data.VoiceProfileId = normalized);
        }

        public static string NormalizeInterfaceLanguage(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
            if (normalized == "en" || normalized == "es" || normalized == "fr") return normalized;
            return null;
        }

        public static string NormalizeConversationLanguage(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
            switch (normalized)
            {
                case "en": case "es": case "fr": return normalized;
                case "zh": case "zh-cn": return "zh-CN";
                case "hi": case "hi-in": return "hi-IN";
                default: return null;
            }
        }

        public static string NormalizeVoiceProfile(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? DefaultVoiceProfileId : value.Trim().ToLowerInvariant();
            for (int i=0;i<SupportedVoiceProfiles.Length;i++)
                if (SupportedVoiceProfiles[i] == normalized) return normalized;
            return null;
        }

        private static string NormalizeOptionalLanguageCode(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('_', '-');
            if (normalized.Length == 0) return string.Empty;
            if (normalized.Length > 16 || !char.IsLetter(normalized[0])) return null;
            for (int i = 0; i < normalized.Length; i++)
                if (!(char.IsLetter(normalized[i]) || normalized[i] == '-')) return null;
            return normalized;
        }

        private static string NormalizeIdentifier(string value, string fallback, int maximumLength)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            if (normalized.Length > maximumLength) return null;
            for (int i = 0; i < normalized.Length; i++)
                if (!(char.IsLetterOrDigit(normalized[i]) || normalized[i] == '-' || normalized[i] == '_')) return null;
            return normalized;
        }
        public bool SetPreferredName(string value)
        {
            string sanitized = (value ?? string.Empty).Trim();
            if (sanitized.Length > 32) return false;
            for (int i=0;i<sanitized.Length;i++)
                if (!(char.IsLetterOrDigit(sanitized[i]) || sanitized[i]==' ' || sanitized[i]=='-' || sanitized[i]=='\''))
                    return false;
            return Mutate(() => Data.PreferredName = sanitized);
        }
        public bool SetOnboardingProgress(bool welcome, bool menu, bool firstSession) => Mutate(() =>
        { Data.WelcomeCompleted=welcome; Data.MenuTutorialCompleted=menu; Data.FirstSessionTutorialCompleted=firstSession; });
        public bool SetFirstReviewOptionsCompleted(bool value)=>Mutate(()=>Data.FirstReviewOptionsCompleted=value);
        public bool SetPersonalizedGreetingCompleted(bool value)=>Mutate(()=>Data.PersonalizedGreetingCompleted=value);
        public bool ResetOnboardingForDevelopment() => Mutate(() =>
        { Data.PreferredName=string.Empty;Data.WelcomeCompleted=false;Data.MenuTutorialCompleted=false;Data.FirstSessionTutorialCompleted=false;Data.PersonalizedGreetingCompleted=false; });
        public bool ResetAllForDevelopment()
        {ThinkingSpacePreferencesData previous=Data;Data=new ThinkingSpacePreferencesData();if(Save())return true;Data=previous;return false;}

        private bool Mutate(Action change)
        {
            string snapshot=JsonUtility.ToJson(Data); change();
            if(Save())return true;
            Data=JsonUtility.FromJson<ThinkingSpacePreferencesData>(snapshot); return false;
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(directory);
                Data.SchemaVersion = CurrentSchemaVersion;
                string temporary = path + ".temporary";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                { writer.Write(JsonUtility.ToJson(Data, true)); writer.Flush(); stream.Flush(true); }
                if (File.Exists(path)) File.Copy(path, backupPath, true);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                LastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                LastError = "Preferences save failed: " + exception.Message;
                Debug.LogWarning(LastError);
                return false;
            }
        }

        private void Load()
        {
            if (TryLoad(path) || TryLoad(backupPath)) return;
            Data = new ThinkingSpacePreferencesData();
        }

        private bool TryLoad(string candidate)
        {
            if (!File.Exists(candidate)) return false;
            try
            {
                var loaded = JsonUtility.FromJson<ThinkingSpacePreferencesData>(File.ReadAllText(candidate));
                if (loaded == null || loaded.SchemaVersion < 1 || loaded.SchemaVersion > CurrentSchemaVersion)
                    throw new InvalidDataException("Unsupported preferences schema.");
                loaded.SilenceDurationSeconds = ClampSilence(loaded.SilenceDurationSeconds);
                if (!Enum.IsDefined(typeof(ThumbRecordingGestureController.RecordingMode), loaded.RecordingMode))
                    loaded.RecordingMode = ThumbRecordingGestureController.RecordingMode.Conversational;
                if (!Enum.IsDefined(typeof(WristMenuHand), loaded.WristMenuHand))
                    loaded.WristMenuHand = WristMenuHand.Left;
                loaded.InterfaceLanguageCode = NormalizeInterfaceLanguage(loaded.InterfaceLanguageCode)
                    ?? DefaultInterfaceLanguageCode;
                string conversationMode = (loaded.ConversationLanguageMode ?? string.Empty).Trim().ToLowerInvariant();
                string conversationCode = NormalizeConversationLanguage(loaded.ConversationLanguageCode);
                if (conversationMode != "explicit" || string.IsNullOrEmpty(conversationCode))
                {
                    loaded.ConversationLanguageMode = DefaultConversationLanguageMode;
                    loaded.ConversationLanguageCode = string.Empty;
                }
                else
                {
                    loaded.ConversationLanguageMode = "explicit";
                    loaded.ConversationLanguageCode = conversationCode;
                }
                loaded.VoiceProfileId = NormalizeVoiceProfile(loaded.VoiceProfileId) ?? DefaultVoiceProfileId;
                // Version 1 predates subtitle preferences. Preserve the intended opt-out
                // behavior instead of treating the absent JSON field as false.
                if (loaded.SchemaVersion == 1) loaded.SubtitlesEnabled = true;
                loaded.StartupBehavior = StartupBehavior.PreserveCurrentBehavior;
                loaded.SchemaVersion = CurrentSchemaVersion;
                Data = loaded;
                LastError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                LastError = "Preferences load failed: " + exception.Message;
                Debug.LogWarning(LastError);
                return false;
            }
        }
    }
}
