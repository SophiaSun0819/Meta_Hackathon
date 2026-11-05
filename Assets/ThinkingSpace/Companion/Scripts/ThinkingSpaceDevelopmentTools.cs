#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class ThinkingSpaceDevelopmentTools : MonoBehaviour
    {
        [Header("Diagnostics")]
        [SerializeField] private bool showDevelopmentDiagnosticsOnStartup;
        [TextArea(6, 16), SerializeField] private string lastInspection = "Not inspected.";
        public string LastInspection => lastInspection;
        public bool ShowDevelopmentDiagnosticsOnStartup => showDevelopmentDiagnosticsOnStartup;

        private void Start() => ApplyDiagnosticVisibility(showDevelopmentDiagnosticsOnStartup);

        [ContextMenu("Diagnostics/Show Development Diagnostics")]
        public void ShowDiagnostics() => ApplyDiagnosticVisibility(true);

        [ContextMenu("Diagnostics/Hide Development Diagnostics")]
        public void HideDiagnostics() => ApplyDiagnosticVisibility(false);

        private static void ApplyDiagnosticVisibility(bool visible)
        {
            FindFirstObjectByType<BrainstormMenuController>()?.SetDevelopmentDiagnosticsVisible(visible);
            FindFirstObjectByType<ThumbGestureDiagnosticsPanel>()?.SetVisible(visible);
            FindFirstObjectByType<ThumbJointDebugVisualizer>()?.SetVisible(visible);
            FindFirstObjectByType<IdeaBubbleManager>()?.SetDevelopmentDiagnosticsVisible(visible);
            FindFirstObjectByType<OrbyVoiceDiagnosticsPanel>()?.SetVisible(visible);
        }

        [ContextMenu("Development Reset/1 - Request Onboarding Reset")]
        public void RequestOnboardingReset() => GetComponent<ThinkingSpaceDevelopmentReset>()?.RequestOnboardingReset();

        [ContextMenu("Development Reset/1 - Request Sessions Reset")]
        public void RequestSessionsReset() => GetComponent<ThinkingSpaceDevelopmentReset>()?.RequestSessionsReset();

        [ContextMenu("Development Reset/1 - Request Full Development Reset")]
        public void RequestFullReset() => GetComponent<ThinkingSpaceDevelopmentReset>()?.RequestFullReset();

        [ContextMenu("Development Reset/2 - Confirm Requested Reset")]
        public void ConfirmReset() => GetComponent<ThinkingSpaceDevelopmentReset>()?.ConfirmRequestedReset();

        [ContextMenu("Development Reset/Cancel Pending Reset")]
        public void CancelReset() => GetComponent<ThinkingSpaceDevelopmentReset>()?.CancelRequestedReset();

        [ContextMenu("Palm Menu/Show Full Menu At Headset")]
        public void ShowMenu() => FindFirstObjectByType<BrainstormMenuController>()?.DevelopmentShowMenuAtHeadset();

        [ContextMenu("Palm Menu/Hide Full Menu")]
        public void HideMenu() => FindFirstObjectByType<BrainstormMenuController>()?.DevelopmentHideMenu();

        [ContextMenu("Palm Menu/Simulate One Callback")]
        public void SimulateMenuCallback() => FindFirstObjectByType<BrainstormMenuController>()?.DevelopmentSimulateMenuButtonCallback();

        [ContextMenu("Palm Menu/Test Toggle State")]
        public void TestMenuToggle() => FindFirstObjectByType<BrainstormMenuController>()?.DevelopmentTestToggle();

        [ContextMenu("Orby/Test Prerecorded Playback")]
        public void TestPrerecordedOrbyPlayback()
        {
            bool accepted = OrbyVoiceDirector.Instance != null && OrbyVoiceDirector.Instance.DebugPlayPrerecorded();
            lastInspection = "Prerecorded Orby request " + (accepted ? "accepted" : "rejected") + ".";
        }

        [ContextMenu("Diagnostics/Inspect Authentication And Voice Routing")]
        public void InspectAuthenticationAndVoiceRouting()
        {
            SpeechTranscriptionService transcription = FindFirstObjectByType<SpeechTranscriptionService>();
            OrbyVoiceDirector voice = OrbyVoiceDirector.Instance;
            lastInspection = "Token configured: " + (transcription != null && transcription.IsDevelopmentTokenConfigured ? "Yes" : "No")
                + "\nToken source: " + (transcription != null ? transcription.DevelopmentTokenSourceType : "Service missing")
                + "\nTranscription endpoint: " + (transcription != null ? transcription.BackendUrl : "Unavailable")
                + "\nConversation endpoint: " + (transcription != null ? transcription.BackendBaseUrl : string.Empty)
                    + ThinkingSpaceAIBackendClient.ConversationEndpointPath.TrimStart('/')
                + "\nVoice manifest: " + (voice != null ? voice.ManifestStatus : "Director missing")
                + "\nLast voice event: " + (voice != null ? voice.LastVoiceEvent : "None")
                + "\nTTS endpoint: " + (voice != null ? voice.LastTtsEndpoint : "Unavailable")
                + "\nTTS failure: " + (voice != null ? voice.LastTtsFailure : "Unavailable")
                + "\nAudio playback: " + (voice != null ? voice.PlaybackState : "Unavailable")
                + "\nCurrent AI route: " + (FindFirstObjectByType<ThinkingSpaceAIBackendClient>()?.LastEndpointPath ?? "Unavailable");
            Debug.Log(lastInspection, this);
        }

        [ContextMenu("Diagnostics/Inspect Session And Recording Eligibility")]
        public void InspectSessionAndRecordingEligibility()
        {
            BrainstormMenuController menu = FindFirstObjectByType<BrainstormMenuController>();
            SpeechTranscriptionCoordinator workflow = FindFirstObjectByType<SpeechTranscriptionCoordinator>();
            BrainstormSession session = menu?.SessionStore?.ActiveSession;
            lastInspection = "Session: " + (session != null ? session.SessionId + " (#" + session.Number + ")" : "none")
                + "\nWorkflow: " + (workflow != null ? workflow.State.ToString() : "missing")
                + "\nSession active: " + (workflow != null && workflow.IsSessionActive)
                + "\nRecording eligible: " + (workflow != null && workflow.CanBeginRecording)
                + "\nPalm menu: " + (menu != null ? menu.DevelopmentMenuStatus : "controller missing")
                + "\nVoice: " + (OrbyVoiceDirector.Instance != null ? OrbyVoiceDirector.Instance.DiagnosticText : "director missing");
            Debug.Log(lastInspection, this);
        }
    }
}
#endif
