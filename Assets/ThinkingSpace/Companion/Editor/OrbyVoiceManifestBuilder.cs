using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ThinkingSpace.Companion.Editor
{
    public static class OrbyVoiceManifestBuilder
    {
        private const string AudioRoot="Assets/ThinkingSpace/Companion/Audio/Prerecorded/";
        private const string AssetPath="Assets/Resources/ThinkingSpace/OrbyPrerecordedManifest.asset";
        [InitializeOnLoadMethod] private static void EnsureManifest(){EditorApplication.delayCall+=()=>{OrbyPrerecordedManifest manifest=AssetDatabase.LoadAssetAtPath<OrbyPrerecordedManifest>(AssetPath);if(manifest==null||manifest.Entries==null||manifest.Entries.Count==0||!manifest.TryGet("first_review_options",out _)||!manifest.TryGet("review_options",out _))Build();};}
        [MenuItem("Thinking Space/Rebuild Orby Prerecorded Manifest")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));var manifest=AssetDatabase.LoadAssetAtPath<OrbyPrerecordedManifest>(AssetPath);
            // An interrupted domain reload can leave a YAML asset with m_Script fileID 0.
            // LoadAssetAtPath then returns null even though the path exists. Replace only
            // that generated manifest; never touch the source MP3 files.
            if(manifest==null&&AssetDatabase.LoadMainAssetAtPath(AssetPath)!=null)
                AssetDatabase.DeleteAsset(AssetPath);
            if(manifest==null){manifest=ScriptableObject.CreateInstance<OrbyPrerecordedManifest>();AssetDatabase.CreateAsset(manifest,AssetPath);}
            manifest.Entries=new List<OrbyPrerecordedEntry>();
            Add(manifest,"greeting_welcome",OrbyVoicePriority.Tutorial,"First launch only");Add(manifest,"greeting_return",OrbyVoicePriority.Acknowledgement,"Returning launch, no tutorial active");
            Add(manifest,"tutorial_menu",OrbyVoicePriority.Tutorial,"Onboarding menu instruction");Add(manifest,"tutorial_menu_opened",OrbyVoicePriority.Tutorial,"Palm menu actually opened");Add(manifest,"tutorial_first_session",OrbyVoicePriority.Tutorial,"First session actually activated");
            Add(manifest,"mic_on",OrbyVoicePriority.Acknowledgement,"Listening manually enabled");Add(manifest,"mic_off",OrbyVoicePriority.Acknowledgement,"Listening manually disabled");Add(manifest,"listening_ready",OrbyVoicePriority.IdleReminder,"Listening enabled, 20 seconds without speech, 60 second cooldown");
            Add(manifest,"idea_created",OrbyVoicePriority.Acknowledgement,"Optional acknowledgement; never alongside dynamic idea summary");Add(manifest,"idea_saved",OrbyVoicePriority.Acknowledgement,"Idea saved without dynamic response");Add(manifest,"idea_selected",OrbyVoicePriority.Acknowledgement,"Manual selection only");Add(manifest,"idea_expand",OrbyVoicePriority.Acknowledgement,"Expand action acknowledgement only");Add(manifest,"idea_challenge",OrbyVoicePriority.Acknowledgement,"Challenge action acknowledgement only");Add(manifest,"idea_combine",OrbyVoicePriority.Acknowledgement,"Combine action acknowledgement only");
            Add(manifest,"clarify_which",OrbyVoicePriority.AIResponse,"Clarification question when no dynamic voice is queued");Add(manifest,"clarify_again",OrbyVoicePriority.AIResponse,"Repeated clarification when no dynamic voice is queued");Add(manifest,"action_cancelled",OrbyVoicePriority.Acknowledgement,"Cancellation");Add(manifest,"error_general",OrbyVoicePriority.Critical,"Recoverable application error");Add(manifest,"error_network",OrbyVoicePriority.Critical,"Recoverable network error");
            Add(manifest,"confirm_got_it",OrbyVoicePriority.Acknowledgement,"Short explicit confirmation");Add(manifest,"confirm_understood",OrbyVoicePriority.Acknowledgement,"Short explicit confirmation");Add(manifest,"recording_retry",OrbyVoicePriority.Acknowledgement,"Recording retry prompt");Add(manifest,"session_started",OrbyVoicePriority.Acknowledgement,"Session activation outside tutorial");Add(manifest,"session_ended",OrbyVoicePriority.Acknowledgement,"Session end");Add(manifest,"goodbye",OrbyVoicePriority.Acknowledgement,"Application/session farewell");
            Add(manifest,"first_review_options",OrbyVoicePriority.Acknowledgement,"First completed recording review only");Add(manifest,"review_options",OrbyVoicePriority.Acknowledgement,"Subsequent completed recording reviews");
            EditorUtility.SetDirty(manifest);AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        private static void Add(OrbyPrerecordedManifest manifest,string key,OrbyVoicePriority priority,string condition)
        {string path=AudioRoot+key+".mp3";AudioClip clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);if(clip==null)return;
            if(AssetImporter.GetAtPath(path) is AudioImporter importer){AudioImporterSampleSettings settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.loadInBackground=false;importer.SaveAndReimport();clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);}
            manifest.Entries.Add(new OrbyPrerecordedEntry{EventKey=key,Clip=clip,ExactSpokenText="",SubtitleText="",TranscriptVerified=false,Priority=priority,AllowedTriggerConditions=condition+". Transcript requires manual verification."});}
    }
}
