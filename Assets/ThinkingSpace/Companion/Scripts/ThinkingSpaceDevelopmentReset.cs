#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceDevelopmentReset : MonoBehaviour
    {
        private enum ResetScope{None,Onboarding,Sessions,Full}
        private ResetScope pending;private float confirmationDeadline;
        public string LastStatus { get; private set; }="No reset requested.";
        public bool ConfirmationPending=>pending!=ResetScope.None&&Time.realtimeSinceStartup<=confirmationDeadline;

        [ContextMenu("Development Reset/1 - Request Onboarding Reset")]
        public void RequestOnboardingReset()=>Request(ResetScope.Onboarding);
        [ContextMenu("Development Reset/1 - Request Sessions and Ideas Reset")]
        public void RequestSessionsReset()=>Request(ResetScope.Sessions);
        [ContextMenu("Development Reset/1 - Request Full Reset")]
        public void RequestFullReset()=>Request(ResetScope.Full);
        [ContextMenu("Development Reset/2 - Confirm Requested Reset")]
        public void ConfirmRequestedReset()
        {
            if(!ConfirmationPending){pending=ResetScope.None;LastStatus="No current confirmation. Request a reset again.";return;}
            ResetScope scope=pending;pending=ResetScope.None;
            BrainstormMenuController menu=FindFirstObjectByType<BrainstormMenuController>();
            if(menu==null||menu.SessionStore==null||menu.PreferencesStore==null){LastStatus="Reset failed: menu stores are unavailable.";return;}
            StopRuntimeWork();
            if(!CreateBackup(menu,scope,out string backup)){LastStatus="Reset aborted: backup creation failed.";return;}
            bool success=scope==ResetScope.Onboarding?menu.PreferencesStore.ResetOnboardingForDevelopment():
                scope==ResetScope.Sessions?menu.SessionStore.ResetAllSessionsForDevelopment():
                menu.SessionStore.ResetAllSessionsForDevelopment()&&menu.PreferencesStore.ResetAllForDevelopment();
            if(!success){LastStatus="Reset failed; persisted stores retained their prior state. Backup: "+backup;return;}
            if(scope==ResetScope.Full)OrbyVoiceDirector.Instance?.ClearVoiceCache();
            LastStatus=scope+" reset complete. Backup: "+backup;
            if(scope!=ResetScope.Sessions)FindFirstObjectByType<OrbyOnboardingController>()?.BeginTutorial();
        }
        public void CancelRequestedReset(){pending=ResetScope.None;LastStatus="Reset cancelled.";}

        private void Request(ResetScope scope){pending=scope;confirmationDeadline=Time.realtimeSinceStartup+15f;LastStatus=scope+" reset requested. Confirm within 15 seconds.";}
        private static void StopRuntimeWork()
        {
            OrbyVoiceDirector.Instance?.CancelAllSpeech();
            ThumbRecordingGestureController gesture=FindFirstObjectByType<ThumbRecordingGestureController>();gesture?.SetSessionActive(false);
            SpeechTranscriptionCoordinator coordinator=FindFirstObjectByType<SpeechTranscriptionCoordinator>();coordinator?.SetSessionActive(false);
        }
        private static bool CreateBackup(BrainstormMenuController menu,ResetScope scope,out string location)
        {
            location=Path.Combine(Application.persistentDataPath,"ThinkingSpace","DevelopmentResetBackups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
            try
            {
                if(!CreateBackupForPaths(location,menu.SessionStore.PersistencePath,menu.SessionStore.PersistencePath+".backup",menu.PreferencesStore.PersistencePath,menu.PreferencesStore.PersistencePath+".backup"))return false;
                File.WriteAllText(Path.Combine(location,"reset-scope.txt"),scope+" · "+DateTime.UtcNow.ToString("O"));
                return true;
            }
            catch(Exception exception){Debug.LogWarning("Development reset backup failed: "+exception.Message);return false;}
        }
        private static void CopyIfPresent(string source,string destination)
        {if(File.Exists(source))File.Copy(source,Path.Combine(destination,Path.GetFileName(source)),true);}
        public static bool CreateBackupForPaths(string destination,params string[] sources)
        {try{Directory.CreateDirectory(destination);foreach(string source in sources)CopyIfPresent(source,destination);return true;}catch{return false;}}
    }
}
#endif
