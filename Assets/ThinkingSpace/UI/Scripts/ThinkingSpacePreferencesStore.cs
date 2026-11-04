using System;
using System.IO;
using UnityEngine;
using ThinkingSpace.Speech;

namespace ThinkingSpace.UI
{
    public enum StartupBehavior { PreserveCurrentBehavior }

    [Serializable]
    public sealed class ThinkingSpacePreferencesData
    {
        public int SchemaVersion = ThinkingSpacePreferencesStore.CurrentSchemaVersion;
        public float SilenceDurationSeconds = 2f;
        public ThumbRecordingGestureController.RecordingMode RecordingMode =
            ThumbRecordingGestureController.RecordingMode.Conversational;
        public StartupBehavior StartupBehavior = StartupBehavior.PreserveCurrentBehavior;
    }

    public sealed class ThinkingSpacePreferencesStore
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "preferences-v1.json";
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
                loaded.StartupBehavior = StartupBehavior.PreserveCurrentBehavior;
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
