using System.IO;
using NUnit.Framework;
using ThinkingSpace.Companion;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class BrainstormPersistenceTests
    {
        private string directory;

        [Test]
        public void PalmMenuToggleConsumesOnlyOneCallbackPerPhysicalPress()
        {
            var gate = new PalmMenuActivationGate();
            Assert.That(gate.BeginInteraction(1f), Is.True);
            Assert.That(gate.TryConsume(1.1f, 0.2f), Is.True);
            Assert.That(gate.TryConsume(1.4f, 0.2f), Is.False,
                "A delayed duplicate callback from the same poke must not close the menu.");
        }

        [Test]
        public void PalmMenuToggleRequiresReleaseAndANewPress()
        {
            var gate = new PalmMenuActivationGate();
            Assert.That(gate.BeginInteraction(1f), Is.True);
            Assert.That(gate.TryConsume(1.1f, 0.2f), Is.True);
            Assert.That(gate.BeginInteraction(1.5f), Is.False, "Continuous contact is not a new click.");
            gate.EndInteraction();
            Assert.That(gate.BeginInteraction(1.6f), Is.True);
            Assert.That(gate.TryConsume(1.61f, 0.2f), Is.True,
                "A separate press after release may perform the opposite transition.");
        }

        [Test]
        public void MicOffDefersOnlyAQualifiedActiveUtterance()
        {
            Assert.That(ConversationalListeningController.ShouldDeferMicrophoneShutdown(true, true, true, false), Is.True);
            Assert.That(ConversationalListeningController.ShouldDeferMicrophoneShutdown(true, true, false, false), Is.False);
            Assert.That(ConversationalListeningController.ShouldDeferMicrophoneShutdown(true, true, true, true), Is.False,
                "An explicit cancellation must remain available.");
            Assert.That(ConversationalListeningController.ShouldDeferMicrophoneShutdown(false, true, true, false), Is.False);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void DevelopmentDiagnosticsAreHiddenByDefault()
        {
            GameObject root = new GameObject("Development Tools Test");
            try
            {
                ThinkingSpaceDevelopmentTools tools = root.AddComponent<ThinkingSpaceDevelopmentTools>();
                Assert.That(tools.ShowDevelopmentDiagnosticsOnStartup, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
#endif

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Application.temporaryCachePath, "ThinkingSpaceTests", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void NewInstallStartsEmptyAndNumbersSessionsMonotonically()
        {
            var store = new BrainstormSessionStore(directory);
            Assert.That(store.Sessions, Is.Empty);
            BrainstormSession first = store.StartNewSession();
            BrainstormSession second = store.StartNewSession();
            Assert.That(first.Number, Is.EqualTo(1));
            Assert.That(second.Number, Is.EqualTo(2));
            Assert.That(first.SessionId, Is.Not.EqualTo(second.SessionId));
        }

        [Test]
        public void FailedIdeaSaveRollsBackIdeaAndRelationshipsInMemory()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession session = store.StartNewSession();
            Assert.That(store.TryAddIdea(session.SessionId,
                new BrainstormIdeaRecord { IdeaId = "source-idea" }), Is.True);

            Directory.Delete(directory, true);
            File.WriteAllText(directory, "Blocks directory recreation for this save attempt.");
            try
            {
                var derived = new BrainstormIdeaRecord { IdeaId = "failed-derived" };
                derived.RelatedIdeaIds.Add("source-idea");
                Assert.That(store.TryAddIdea(session.SessionId, derived), Is.False);
                Assert.That(session.Ideas.Exists(value => value.IdeaId == "failed-derived"), Is.False);
                Assert.That(session.Relationships, Is.Empty);
            }
            finally
            {
                File.Delete(directory);
                Directory.CreateDirectory(directory);
            }
        }

        [Test]
        public void SessionsAndOnboardingRestoreAfterRestart()
        {
            var original = new BrainstormSessionStore(directory);
            original.StartNewSession();
            BrainstormSession active = original.StartNewSession();
            original.SetOnboardingCompleted(true);

            var restored = new BrainstormSessionStore(directory);
            Assert.That(restored.Sessions.Count, Is.EqualTo(2));
            Assert.That(restored.ActiveSession.SessionId, Is.EqualTo(active.SessionId));
            Assert.That(restored.OnboardingCompleted, Is.True);
            Assert.That(restored.StartNewSession().Number, Is.EqualTo(3));
        }

        [Test]
        public void CorruptPrimaryRecoversFromBackup()
        {
            var original = new BrainstormSessionStore(directory);
            original.StartNewSession();
            original.SetOnboardingCompleted(true); // Produces a recoverable backup of the prior valid save.
            File.WriteAllText(original.PersistencePath, "{not valid json");
            var recovered = new BrainstormSessionStore(directory);
            Assert.That(recovered.Sessions.Count, Is.EqualTo(1));
        }

        [Test]
        public void AiIdeaAndConversationRemainOwnedByTheirSessionAfterRestart()
        {
            var original = new BrainstormSessionStore(directory);
            BrainstormSession session = original.StartNewSession();
            Assert.That(original.TryAddIdea(session.SessionId, new BrainstormIdeaRecord
            {
                IdeaId = "idea-1",
                Title = "Solar Hub",
                Summary = "A shared solar workspace.",
                Category = "Energy",
                SourceTranscript = "We could build a solar hub."
            }), Is.True);
            Assert.That(original.AppendConversation(session.SessionId, "orby", "Idea saved.",
                "idea_result", "idea-request-1"), Is.True);

            var restored = new BrainstormSessionStore(directory);
            Assert.That(restored.ActiveSession.SessionId, Is.EqualTo(session.SessionId));
            Assert.That(restored.ActiveSession.Ideas, Has.Count.EqualTo(1));
            Assert.That(restored.ActiveSession.Ideas[0].SourceTranscript,
                Is.EqualTo("We could build a solar hub."));
            Assert.That(restored.ActiveSession.Conversation, Has.Count.EqualTo(1));
            Assert.That(restored.ActiveSession.Conversation[0].RelatedRequestId,
                Is.EqualTo("idea-request-1"));
        }

        [Test]
        public void AiPersistenceRejectsCrossSessionAndDuplicateWrites()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession first = store.StartNewSession();
            BrainstormSession second = store.StartNewSession();
            var idea = new BrainstormIdeaRecord { IdeaId = "idea-1", Title = "One", Summary = "Summary", Category = "Test" };

            Assert.That(store.TryAddIdea(first.SessionId, idea), Is.False);
            Assert.That(store.AppendConversation(first.SessionId, "user", "Cross-session", "approved_transcript", "request-1"), Is.False);
            Assert.That(store.TryAddIdea(second.SessionId, idea), Is.True);
            Assert.That(store.TryAddIdea(second.SessionId, idea), Is.False);
        }

        [Test]
        public void BubbleTransformPersistsWithStableIdeaOwnership()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession session = store.StartNewSession();
            Assert.That(store.TryAddIdea(session.SessionId, new BrainstormIdeaRecord
            {
                IdeaId = "idea-bubble",
                Title = "Bubble",
                Summary = "Persistence test",
                Category = "Testing"
            }), Is.True);
            Vector3 position = new Vector3(0.4f, 1.2f, 0.8f);
            Quaternion rotation = Quaternion.Euler(0f, 35f, 0f);
            Vector3 scale = new Vector3(1.1f, 1.1f, 1.1f);
            Assert.That(store.TryUpdateBubbleTransform(session.SessionId, "idea-bubble",
                position, rotation, scale), Is.True);

            var restored = new BrainstormSessionStore(directory);
            BrainstormIdeaRecord idea = restored.ActiveSession.Ideas.Find(item => item.IdeaId == "idea-bubble");
            Assert.That(idea, Is.Not.Null);
            Assert.That(idea.Bubble.Position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(idea.Bubble.Rotation, rotation), Is.LessThan(0.01f));
            Assert.That(idea.Bubble.Scale, Is.EqualTo(scale));
            Assert.That(restored.TryUpdateBubbleTransform("another-session", "idea-bubble",
                Vector3.zero, Quaternion.identity, Vector3.one), Is.False);
        }

        [Test]
        public void PreferencesPersistClampAndSnapWithoutUsingSessionFile()
        {
            var preferences = new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.SetSilenceDuration(3.26f), Is.True);
            Assert.That(preferences.SetRecordingMode(ThumbRecordingGestureController.RecordingMode.Manual), Is.True);
            var restored = new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.SilenceDurationSeconds, Is.EqualTo(3.5f));
            Assert.That(restored.Data.RecordingMode, Is.EqualTo(ThumbRecordingGestureController.RecordingMode.Manual));
            Assert.That(Path.GetFileName(restored.PersistencePath), Is.EqualTo(ThinkingSpacePreferencesStore.FileName));
            Assert.That(ThinkingSpacePreferencesStore.ClampSilence(-20f), Is.EqualTo(1f));
            Assert.That(ThinkingSpacePreferencesStore.ClampSilence(20f), Is.EqualTo(10f));
        }

        [Test]
        public void VoiceAndOnboardingPreferencesPersistWithoutSecrets()
        {
            var preferences = new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.SetSubtitlesEnabled(false), Is.True);
            Assert.That(preferences.SetPreferredName("Henry-Test"), Is.True);
            Assert.That(preferences.SetPreferredName("invalid/name"), Is.False);
            Assert.That(preferences.SetOnboardingProgress(true, true, false), Is.True);

            var restored = new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.SubtitlesEnabled, Is.False);
            Assert.That(restored.Data.PreferredName, Is.EqualTo("Henry-Test"));
            Assert.That(restored.Data.WelcomeCompleted, Is.True);
            Assert.That(restored.Data.MenuTutorialCompleted, Is.True);
            Assert.That(restored.Data.FirstSessionTutorialCompleted, Is.False);
            string json = File.ReadAllText(restored.PersistencePath);
            StringAssert.DoesNotContain("api", json.ToLowerInvariant());
            StringAssert.DoesNotContain("token", json.ToLowerInvariant());
        }

        [Test]
        public void LegacyPreferencesDefaultSubtitlesOn()
        {
            File.WriteAllText(Path.Combine(directory, ThinkingSpacePreferencesStore.FileName),
                "{\"SchemaVersion\":1,\"SilenceDurationSeconds\":2.0,\"RecordingMode\":1}");
            var restored = new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.SubtitlesEnabled, Is.True);
            Assert.That(restored.Data.SchemaVersion, Is.EqualTo(ThinkingSpacePreferencesStore.CurrentSchemaVersion));
        }

        [Test]
        public void VoiceCachePersistsHitsAndSeparatesSessionsAndVoiceIdentity()
        {
            byte[] audio = { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00 };
            string first = OrbyVoiceCache.CreateKey("Hello", "voice-a", "session-a");
            Assert.That(first, Is.Not.EqualTo(OrbyVoiceCache.CreateKey("Hello", "voice-a", "session-b")));
            Assert.That(first, Is.Not.EqualTo(OrbyVoiceCache.CreateKey("Hello", "voice-b", "session-a")));
            var cache = new OrbyVoiceCache(directory);
            Assert.That(cache.Store(first, "session-a", audio), Is.True);
            var restored = new OrbyVoiceCache(directory);
            Assert.That(restored.TryGetPath(first, out string cachedPath), Is.True);
            Assert.That(File.ReadAllBytes(cachedPath), Is.EqualTo(audio));
            restored.RemoveSession("session-a");
            Assert.That(restored.TryGetPath(first, out _), Is.False);
        }

        [Test]
        public void VoiceCacheEvictsToSizeLimitAndRecoversFromCorruptPrimaryIndex()
        {
            byte[] audio = { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00 };
            var cache = new OrbyVoiceCache(directory, 8, 30);
            string first = OrbyVoiceCache.CreateKey("One", "voice", "session");
            string second = OrbyVoiceCache.CreateKey("Two", "voice", "session");
            Assert.That(cache.Store(first, "session", audio), Is.True);
            Assert.That(cache.Store(second, "session", audio), Is.True);
            Assert.That(cache.Count, Is.EqualTo(1));

            string indexPath = Path.Combine(directory, "VoiceCache", "index-v1.json");
            File.WriteAllText(indexPath, "{corrupt");
            var recovered = new OrbyVoiceCache(directory, 8, 30);
            Assert.That(recovered.Count, Is.GreaterThanOrEqualTo(0));
            Assert.That(recovered.Count, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void PrerecordedManifestDeserializesWithReferencedAudioClips()
        {
            OrbyPrerecordedManifest manifest = Resources.Load<OrbyPrerecordedManifest>(
                "ThinkingSpace/OrbyPrerecordedManifest");
            Assert.That(manifest, Is.Not.Null, "The Resources manifest is missing or has a broken script reference.");
            Assert.That(manifest.Entries, Is.Not.Empty);
            Assert.That(manifest.TryGet("greeting_welcome", out OrbyPrerecordedEntry welcome), Is.True);
            Assert.That(welcome.Clip, Is.Not.Null);
            Assert.That(welcome.Clip.length, Is.GreaterThan(0f));
            Assert.That(manifest.TryGet("session_started", out _), Is.True);
            Assert.That(manifest.TryGet("session_ended", out _), Is.True);
            Assert.That(manifest.TryGet("first_review_options", out _), Is.True);
            Assert.That(manifest.TryGet("review_options", out _), Is.True);
        }

        [Test]
        public void VoiceEndpointConstructionNeverIntroducesDoubleSlashPath()
        {
            Assert.That(OrbyVoiceDirector.BuildVoiceEndpoint("https://thinking-space.example/"),
                Is.EqualTo("https://thinking-space.example/api/voice/synthesize"));
            Assert.That(OrbyVoiceDirector.BuildVoiceEndpoint("https://thinking-space.example"),
                Is.EqualTo("https://thinking-space.example/api/voice/synthesize"));
        }

        [Test]
        public void PersonalizedGreetingIsOptionalAndUsesValidatedNameOnce()
        {
            Assert.That(OrbyOnboardingController.BuildPersonalizedGreeting(""),Is.Empty);
            string greeting=OrbyOnboardingController.BuildPersonalizedGreeting("Alex");
            StringAssert.StartsWith("Alex!",greeting);Assert.That(greeting.Split(new[]{"Alex"},System.StringSplitOptions.None).Length-1,Is.EqualTo(1));
            var preferences=new ThinkingSpacePreferencesStore(directory);Assert.That(preferences.SetPersonalizedGreetingCompleted(true),Is.True);Assert.That(new ThinkingSpacePreferencesStore(directory).Data.PersonalizedGreetingCompleted,Is.True);
        }

        [Test]
        public void ContinuousSubtitlesSplitLongSpeechWithoutPaginationControls()
        {
            var segments=OrbySubtitleView.BuildSubtitleSegments("This is a deliberately long spoken response that should advance as compact readable phrases beneath Orby instead of requiring the user to press previous or next while speech is playing.");
            Assert.That(segments.Count,Is.GreaterThan(1));Assert.That(segments.TrueForAll(value=>value.Length<=100),Is.True);
        }

        [Test]
        public void ExpressiveTtsTagsStayOutOfSubtitlesWithoutRemovingOrdinaryBrackets()
        {
            Assert.That(OrbySubtitleView.StripTtsOnlyTags(
                "[laughs] That's clever. Keep [Phase 2] as the fallback."),
                Is.EqualTo("That's clever. Keep [Phase 2] as the fallback."));
            Assert.That(OrbyVoiceDirector.NormalizeDelivery("excited"),Is.EqualTo("excited"));
            Assert.That(OrbyVoiceDirector.NormalizeDelivery("thinking"),Is.EqualTo("curious"));
            Assert.That(OrbyVoiceDirector.NormalizeDelivery("unsupported"),Is.EqualTo("neutral"));
        }

        [Test]
        public void ReviewPreferencePersistsAndCacheIdentityIsVersioned()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);Assert.That(preferences.Data.FirstReviewOptionsCompleted,Is.False);Assert.That(preferences.SetFirstReviewOptionsCompleted(true),Is.True);
            Assert.That(new ThinkingSpacePreferencesStore(directory).Data.FirstReviewOptionsCompleted,Is.True);
            StringAssert.Contains("v3",OrbyVoiceDirector.CacheIdentity);StringAssert.Contains("eleven_v4_turbo",OrbyVoiceDirector.CacheIdentity);StringAssert.Contains("E584YCmNqnu35GrWlrqy",OrbyVoiceDirector.CacheIdentity);StringAssert.Contains("mp3_44100_128",OrbyVoiceDirector.CacheIdentity);
        }

        [Test]
        public void ReviewAndConversationalVoicePoliciesAvoidFalseConfirmationsAndDuplicates()
        {
            Assert.That(SpeechTranscriptionCoordinator.SelectReviewVoiceKey(true),Is.EqualTo("first_review_options"));
            Assert.That(SpeechTranscriptionCoordinator.SelectReviewVoiceKey(false),Is.EqualTo("review_options"));
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("converse"),Is.True);
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("resize_bubble"),Is.False);
            var idea=new BrainstormIdeaRecord{Title="Solar Hub",Summary="A shared clean-energy workspace."};
            Assert.That(ThinkingSpaceAIWorkflowController.BuildIdeaSpeechText(idea),Is.EqualTo("Solar Hub. A shared clean-energy workspace."));
        }

        [Test]
        public void DevelopmentSessionAndOnboardingResetScopesRemainIndependent()
        {
            var sessions=new BrainstormSessionStore(directory);sessions.StartNewSession();sessions.StartNewSession();
            var preferences=new ThinkingSpacePreferencesStore(directory);preferences.SetSilenceDuration(4f);preferences.SetPreferredName("Tester");preferences.SetOnboardingProgress(true,true,true);
            Assert.That(preferences.ResetOnboardingForDevelopment(),Is.True);
            Assert.That(preferences.Data.PreferredName,Is.Empty);Assert.That(preferences.Data.FirstSessionTutorialCompleted,Is.False);
            Assert.That(preferences.Data.SilenceDurationSeconds,Is.EqualTo(4f));Assert.That(sessions.Sessions,Has.Count.EqualTo(2));
            Assert.That(sessions.ResetAllSessionsForDevelopment(),Is.True);Assert.That(sessions.Sessions,Is.Empty);Assert.That(sessions.ActiveSession,Is.Null);
            Assert.That(preferences.Data.SilenceDurationSeconds,Is.EqualTo(4f));
        }

        [Test]
        public void IndependentOutputToneContainsNonzeroKnownSignal()
        {
            float[] samples=OrbyVoiceDiagnosticsPanel.CreateOutputTestSamples(48000,1.25f,.22f);float peak=0f;double sum=0;
            foreach(float sample in samples){peak=Mathf.Max(peak,Mathf.Abs(sample));sum+=sample*sample;}
            Assert.That(samples.Length,Is.EqualTo(60000));Assert.That(peak,Is.EqualTo(.22f).Within(.001f));Assert.That(System.Math.Sqrt(sum/samples.Length),Is.GreaterThan(.15));
        }

        [Test]
        public void StartupEligibilityDoesNotDependOnRestoredSession()
        {
            var incomplete=new ThinkingSpacePreferencesData{WelcomeCompleted=true,MenuTutorialCompleted=true,FirstSessionTutorialCompleted=false};
            var complete=new ThinkingSpacePreferencesData{WelcomeCompleted=true,MenuTutorialCompleted=true,FirstSessionTutorialCompleted=true};
            Assert.That(OrbyOnboardingController.ShouldBeginTutorial(incomplete),Is.True);
            Assert.That(OrbyOnboardingController.ShouldBeginTutorial(complete),Is.False);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void DevelopmentResetBackupPreservesExistingStoreFiles()
        {
            var sessions=new BrainstormSessionStore(directory);sessions.StartNewSession();var preferences=new ThinkingSpacePreferencesStore(directory);preferences.SetPreferredName("Backup Test");
            string backup=Path.Combine(directory,"reset-backup");
            Assert.That(ThinkingSpaceDevelopmentReset.CreateBackupForPaths(backup,sessions.PersistencePath,preferences.PersistencePath),Is.True);
            Assert.That(File.Exists(Path.Combine(backup,Path.GetFileName(sessions.PersistencePath))),Is.True);
            Assert.That(File.Exists(Path.Combine(backup,Path.GetFileName(preferences.PersistencePath))),Is.True);
        }
#endif

        [Test]
        public void DeleteIdeaRemovesReferencesPreservesDerivedIdeasAndPersists()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession session = store.StartNewSession();
            Assert.That(store.TryAddIdea(session.SessionId, new BrainstormIdeaRecord
            { IdeaId="source", Title="Source", RelatedIdeaIds=new System.Collections.Generic.List<string>() }), Is.True);
            Assert.That(store.TryAddIdea(session.SessionId, new BrainstormIdeaRecord
            { IdeaId="derived", Title="Derived", RelatedIdeaIds=new System.Collections.Generic.List<string>{"source"} }), Is.True);
            Assert.That(store.TryDeleteIdea(session.SessionId, "source"), Is.True);
            Assert.That(session.Ideas.Exists(value => value.IdeaId == "source"), Is.False);
            Assert.That(session.Ideas.Exists(value => value.IdeaId == "derived"), Is.True);
            Assert.That(session.Ideas[0].RelatedIdeaIds, Does.Not.Contain("source"));
            Assert.That(session.Relationships, Is.Empty);
            var restored = new BrainstormSessionStore(directory);
            Assert.That(restored.ActiveSession.Ideas, Has.Count.EqualTo(1));
            Assert.That(restored.ActiveSession.Ideas[0].IdeaId, Is.EqualTo("derived"));
        }

        [Test]
        public void DeleteSessionIsIsolatedPersistentAndDoesNotReuseItsNumber()
        {
            var store=new BrainstormSessionStore(directory);
            BrainstormSession first=store.StartNewSession();
            BrainstormSession second=store.StartNewSession();
            Assert.That(store.TryAddIdea(second.SessionId,new BrainstormIdeaRecord{IdeaId="second-idea"}),Is.True);
            Assert.That(store.TryDeleteSession(first.Number),Is.True);
            Assert.That(store.Sessions,Has.Count.EqualTo(1));
            Assert.That(store.ActiveSession.SessionId,Is.EqualTo(second.SessionId));
            var restored=new BrainstormSessionStore(directory);
            Assert.That(restored.Sessions,Has.Count.EqualTo(1));
            Assert.That(restored.Sessions[0].SessionId,Is.EqualTo(second.SessionId));
            Assert.That(restored.StartNewSession().Number,Is.EqualTo(3));
        }

        [Test]
        public void RouterInventoryMarksNewestCombinedIdeaWithoutChangingStoredSummary()
        {
            var store=new BrainstormSessionStore(directory);BrainstormSession session=store.StartNewSession();
            store.TryAddIdea(session.SessionId,new BrainstormIdeaRecord{IdeaId="one",Summary="Original"});
            var combined=new BrainstormIdeaRecord{IdeaId="combined",Summary="Combined summary",
                RelatedIdeaIds=new System.Collections.Generic.List<string>{"one","two"}};
            store.TryAddIdea(session.SessionId,combined);
            AIIdeaContext[] contexts=ThinkingSpaceAIWorkflowController.BuildIdeaContexts(session);
            StringAssert.Contains("Most recently created idea",contexts[1].summary);
            StringAssert.Contains("Combined idea",contexts[1].summary);
            Assert.That(combined.Summary,Is.EqualTo("Combined summary"));
        }

        [Test]
        public void VadSilenceDurationCanChangeWithoutRecreatingDetector()
        {
            var vad = NewVad(); VoiceActivityUpdate update=default;
            for(int i=0;i<8;i++) update=vad.Process(.025f,.1f);
            vad.SetSilenceDuration(1f);
            for(int i=0;i<9;i++) update=vad.Process(.0005f,.1f);
            Assert.That(update.ShouldFinalize, Is.False);
            update=vad.Process(.0005f,.11f);
            Assert.That(update.ShouldFinalize, Is.True);
        }

        [Test]
        public void VadRejectsBackgroundNoiseAndShortSpike()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 50; i++) update = vad.Process(0.0012f, 0.1f);
            Assert.That(update.SpeechActive, Is.False);
            update = vad.Process(0.02f, 0.1f);
            Assert.That(update.SpeechActive, Is.False);
            for (int i = 0; i < 10; i++) update = vad.Process(0.0012f, 0.1f);
            Assert.That(update.SpeechActive, Is.False);
            Assert.That(update.ShouldFinalize, Is.False);
        }

        [Test]
        public void VadFinalizesAfterTwoSecondsOfSilence()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 8; i++) update = vad.Process(0.025f, 0.1f);
            Assert.That(update.SpeechActive, Is.True);
            for (int i = 0; i < 19; i++) update = vad.Process(0.0005f, 0.1f);
            Assert.That(update.ShouldFinalize, Is.False);
            update = vad.Process(0.0005f, 0.11f);
            Assert.That(update.ShouldFinalize, Is.True);
        }

        [Test]
        public void VadSpeechResumptionResetsSilenceCountdown()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 8; i++) update = vad.Process(0.025f, 0.1f);
            for (int i = 0; i < 12; i++) update = vad.Process(0.0005f, 0.1f);
            Assert.That(update.SilenceElapsed, Is.GreaterThan(1f));
            update = vad.Process(0.025f, 0.1f);
            Assert.That(update.SilenceElapsed, Is.EqualTo(0f));
            for (int i = 0; i < 19; i++) update = vad.Process(0.0005f, 0.1f);
            Assert.That(update.ShouldFinalize, Is.False);
            update = vad.Process(0.0005f, 0.11f);
            Assert.That(update.ShouldFinalize, Is.True);
        }

        [Test]
        public void VadCalibratesAmbientNoiseBeforeFirstSpeech()
        {
            var vad = NewVad();
            for (int i = 0; i < 6; i++) vad.Calibrate(0.004f, 0.1f);
            Assert.That(vad.NoiseFloor, Is.GreaterThan(0.003f));
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 8; i++) update = vad.Process(0.025f, 0.1f);
            Assert.That(update.UtteranceQualified, Is.True);
        }

        [Test]
        public void VadReleaseBandAmbientCannotHoldQualifiedUtteranceForever()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 8; i++) update = vad.Process(0.025f, 0.1f);
            float releaseBandRms = (update.SpeechThreshold + update.ReleaseThreshold) * 0.5f;
            for (int i = 0; i < 24; i++) update = vad.Process(releaseBandRms, 0.1f);
            Assert.That(update.ShouldFinalize, Is.True,
                "A steady level below the activation threshold must not reset silence indefinitely.");
        }

        [Test]
        public void VoicePlaybackDefersOnlyForQualifiedOwnedCapture()
        {
            Assert.That(OrbyVoiceDirector.ShouldDeferForQualifiedCapture(true, true), Is.True);
            Assert.That(OrbyVoiceDirector.ShouldDeferForQualifiedCapture(true, false), Is.False);
            Assert.That(OrbyVoiceDirector.ShouldDeferForQualifiedCapture(false, true), Is.False);
        }

        [Test]
        public void RecorderMaximumDurationRemainsAHardSafetyFinalization()
        {
            Assert.That(QuestMicrophoneRecorder.ShouldFinalizeForMaximumDuration(59.9f, 60f), Is.False);
            Assert.That(QuestMicrophoneRecorder.ShouldFinalizeForMaximumDuration(60f, 60f), Is.True);
        }

        [Test]
        public void AutomaticConversationSubmissionRequiresConversationalCaptureOwnership()
        {
            Assert.That(SpeechTranscriptionCoordinator.ShouldAutomaticallyProcess(true,
                ThumbRecordingGestureController.RecordingMode.Conversational), Is.True);
            Assert.That(SpeechTranscriptionCoordinator.ShouldAutomaticallyProcess(false,
                ThumbRecordingGestureController.RecordingMode.Conversational), Is.False);
            Assert.That(SpeechTranscriptionCoordinator.ShouldAutomaticallyProcess(true,
                ThumbRecordingGestureController.RecordingMode.Manual), Is.False);
        }

        [Test]
        public void ManualAndAutomaticTranscriptionUseTheSameValidatedRequestContract()
        {
            const int rate = 48000;
            AudioClip clip = AudioClip.Create("Request contract", 4800, 1, rate, false);
            float[] samples = new float[4800];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.1f;
            clip.SetData(samples, 0);
            var recording = new CompletedMicrophoneRecording(clip, 4800, rate, 1,
                0.1f, 0.1f, 0.1f, true, "session-contract");
            byte[] wav = WavEncoder.Encode16BitPcm(recording);
            SpeechTranscriptionRequest request = SpeechTranscriptionCoordinator.CreateTranscriptionRequest(
                recording, wav, "recording-contract");

            Assert.That(WavEncoder.Validate16BitPcm(request.WavData, recording).IsValid, Is.True);
            Assert.That(request.SessionIdentifier, Is.EqualTo("session-contract"));
            Assert.That(request.RecordingIdentifier, Is.EqualTo("recording-contract"));
            Assert.That(request.RecordingDuration, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(request.SampleRate, Is.EqualTo(rate));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void TranscriptionRequestOwnershipRejectsCancellationAndSessionSwitch()
        {
            Assert.That(SpeechTranscriptionCoordinator.IsCurrentRequest(4, 4, true), Is.True);
            Assert.That(SpeechTranscriptionCoordinator.IsCurrentRequest(4, 5, true), Is.False,
                "Incrementing the generation must invalidate a cancelled request.");
            Assert.That(SpeechTranscriptionCoordinator.IsCurrentRequest(4, 4, false), Is.False,
                "An ended or switched session must reject the old response.");
        }

        [Test]
        public void MicrophoneHudMapsLifecycleStatesWithoutHidingMicOff()
        {
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.Off, OrbyConversationState.Idle, false,
                RecordingWorkflowState.Ready), Is.EqualTo(MicrophoneStatusDisplayState.MicOff));
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.WaitingForSpeech, OrbyConversationState.Listening, true,
                RecordingWorkflowState.Ready), Is.EqualTo(MicrophoneStatusDisplayState.Idle));
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.SpeechActive, OrbyConversationState.Listening, true,
                RecordingWorkflowState.Ready), Is.EqualTo(MicrophoneStatusDisplayState.SpeechDetected));
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.SilenceCountdown, OrbyConversationState.Listening, true,
                RecordingWorkflowState.Ready), Is.EqualTo(MicrophoneStatusDisplayState.FinishingThought));
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.Suspended, OrbyConversationState.Thinking, true,
                RecordingWorkflowState.Transcribing), Is.EqualTo(MicrophoneStatusDisplayState.Thinking));
            Assert.That(ConversationalListeningHud.ResolveDisplayState(
                ConversationalListeningState.Suspended, OrbyConversationState.Speaking, true,
                RecordingWorkflowState.Reviewing), Is.EqualTo(MicrophoneStatusDisplayState.Speaking));
        }

        private static AdaptiveVoiceActivityDetector NewVad()
        {
            return new AdaptiveVoiceActivityDetector(0.001f, 2.4f, 0.0015f, 0.003f,
                0.18f, 0.65f, 2f, 0.65f);
        }

        [Test]
        public void VadDetectsSpeechAfterSixtySecondsOfQuietMonitoring()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 600; i++) update = vad.Process(0.0011f, 0.1f);
            Assert.That(update.UtteranceQualified, Is.False);
            for (int i = 0; i < 8; i++) update = vad.Process(0.025f, 0.1f);
            Assert.That(update.UtteranceQualified, Is.True);
            Assert.That(update.VoicedDuration, Is.GreaterThanOrEqualTo(0.65f));
        }

        [Test]
        public void VadRejectsSneezeLikeImpulseBeforeUtteranceQualification()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            bool rejected = false;
            for (int i = 0; i < 3; i++) update = vad.Process(0.06f, 0.1f);
            for (int i = 0; i < 7; i++)
            { update = vad.Process(0.0006f, 0.1f); rejected |= update.ImpulseRejected; }
            Assert.That(rejected, Is.True);
            Assert.That(update.UtteranceQualified, Is.False);
            Assert.That(update.ShouldFinalize, Is.False);
        }

        [Test]
        public void VadTreatsStableLowAmbientNoiseAsBackground()
        {
            var vad = NewVad();
            VoiceActivityUpdate update = default;
            for (int i = 0; i < 300; i++) update = vad.Process(0.0022f, 0.1f);
            Assert.That(update.UtteranceQualified, Is.False);
            Assert.That(update.ShouldFinalize, Is.False);
        }

        [Test]
        public void RingBufferWindowKeepsOnlyRecentUtteranceAcrossWrap()
        {
            PcmRingBufferWindow window = PcmRingBufferMath.Calculate(119500, 122000, 48000);
            Assert.That(window.FrameCount, Is.EqualTo(2500));
            Assert.That(window.SourceStartFrame, Is.EqualTo(23500));
            Assert.That(window.AbsoluteStartFrame, Is.EqualTo(119500));

            PcmRingBufferWindow capacityLimited = PcmRingBufferMath.Calculate(1000, 100000, 48000);
            Assert.That(capacityLimited.FrameCount, Is.EqualTo(48000));
            Assert.That(capacityLimited.AbsoluteStartFrame, Is.EqualTo(52000));
            Assert.That(capacityLimited.SourceStartFrame, Is.EqualTo(4000));
        }

        [Test]
        public void TrimmedRecordingWavPreservesFramesRateChannelsAndDuration()
        {
            const int rate = 48000;
            const int frames = 4800;
            AudioClip clip = AudioClip.Create("Synthetic trimmed utterance", frames, 1, rate, false);
            float[] samples = new float[frames];
            double sum = 0d;
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Sin(2f * Mathf.PI * 440f * i / rate) * 0.15f;
                sum += samples[i] * samples[i];
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            clip.SetData(samples, 0);
            var recording = new CompletedMicrophoneRecording(clip, frames, rate, 1,
                frames / (float)rate, Mathf.Sqrt((float)(sum / frames)), peak, true, "Synthetic Session");

            byte[] wav = WavEncoder.Encode16BitPcm(recording);
            WavValidationResult validation = WavEncoder.Validate16BitPcm(wav, recording);

            Assert.That(validation.IsValid, Is.True, validation.Error);
            Assert.That(validation.MetadataMatchesRecording, Is.True);
            Assert.That(validation.SampleRate, Is.EqualTo(rate));
            Assert.That(validation.Channels, Is.EqualTo(1));
            Assert.That(validation.SampleFrames, Is.EqualTo(frames));
            Assert.That(validation.DurationSeconds, Is.EqualTo(0.1f).Within(0.001f));
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void BackendContractsDeserializeVersionedClarificationAndIdeaResponses()
        {
            IntentRouteResponse clarification = JsonUtility.FromJson<IntentRouteResponse>(
                "{\"contract_version\":\"1.0\",\"request_id\":\"route-1\",\"session_id\":\"session-1\",\"intent\":\"clarification_required\",\"action\":\"clarification_required\",\"candidate_idea_ids\":[\"idea-1\",\"idea-2\"],\"clarification_id\":\"clarify-1\",\"status\":\"completed\"}");
            IdeaProcessResponse idea = JsonUtility.FromJson<IdeaProcessResponse>(
                "{\"contract_version\":\"1.0\",\"request_id\":\"idea-1\",\"session_id\":\"session-1\",\"action\":\"create_idea\",\"idea_id\":\"created-1\",\"title\":\"Solar Hub\",\"summary\":\"Summary\",\"category\":\"Energy\",\"status\":\"completed\"}");

            Assert.That(clarification.clarification_id, Is.EqualTo("clarify-1"));
            Assert.That(clarification.candidate_idea_ids, Is.EqualTo(new[] { "idea-1", "idea-2" }));
            Assert.That(idea.idea_id, Is.EqualTo("created-1"));
            Assert.That(idea.title, Is.EqualTo("Solar Hub"));
        }

        [Test]
        public void InitialIntentJsonOmitsAbsentClarificationAndKeepsEmptyIdeaArray()
        {
            string json = AIRequestJsonSerializer.Serialize(new IntentRouteRequest
            {
                request_id = "route-synthetic",
                session_id = "session-restored-0123456789abcdef",
                raw_transcript = "A café for multilingual study groups.",
                transcript_source = "raw",
                available_ideas = System.Array.Empty<AIIdeaContext>(),
                clarification_id = null
            });

            StringAssert.DoesNotContain("clarification_id", json);
            StringAssert.Contains("\"available_ideas\":[]", json);
            StringAssert.Contains("multilingual study groups", json);
        }

        [Test]
        public void FollowUpIntentJsonIncludesValidClarificationAndIdeaInventory()
        {
            string json = AIRequestJsonSerializer.Serialize(new IntentRouteRequest
            {
                request_id = "route-follow-up",
                session_id = "session-1",
                raw_transcript = "The second one",
                transcript_source = "raw",
                clarification_id = "clar_0123456789abcdef",
                available_ideas = new[]
                {
                    new AIIdeaContext { idea_id = "idea_one", title = "Solar Hub", summary = "Solar study space." },
                    new AIIdeaContext { idea_id = "idea_two", title = "Workshop", summary = "Peer workshops." }
                }
            });

            StringAssert.Contains("\"clarification_id\":\"clar_0123456789abcdef\"", json);
            StringAssert.Contains("\"idea_id\":\"idea_one\"", json);
            StringAssert.Contains("\"idea_id\":\"idea_two\"", json);
        }

        [Test]
        public void ConversationJsonMatchesVersionOneRequestAndOmitsAbsentOptionals()
        {
            string json = AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {
                request_id = "conversation-1", session_id = "session-1",
                raw_transcript = "Challenge my anatomy idea.", transcript_source = "raw",
                available_ideas = new[] { new AIIdeaContext
                    { idea_id = "idea-anatomy", title = "Anatomy Lab", summary = "A VR anatomy lab." } },
                context = new ConversationContextPayload
                {
                    current_mode = "conversational", most_recent_idea_id = "idea-anatomy",
                    recent_turns = new[] { new ConversationTurnPayload
                        { role = "orby", text = "What would you like to explore?",
                          related_idea_ids = System.Array.Empty<string>() } }
                }
            });

            StringAssert.Contains("\"context\":", json);
            StringAssert.Contains("\"role\":\"orby\"", json);
            StringAssert.DoesNotContain("speaker", json);
            StringAssert.DoesNotContain("clarification_id", json);
            StringAssert.DoesNotContain("cleaned_transcript", json);
        }

        [Test]
        public void ConversationJsonKeepsEmptyInventoryAndValidClarification()
        {
            string json = AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {
                request_id = "conversation-2", session_id = "session-1",
                raw_transcript = "The second one", transcript_source = "raw",
                clarification_id = "clar_0123456789abcdef",
                available_ideas = System.Array.Empty<AIIdeaContext>(),
                context = new ConversationContextPayload()
            });
            StringAssert.Contains("\"available_ideas\":[]", json);
            StringAssert.Contains("\"recent_turns\":[]", json);
            StringAssert.Contains("\"clarification_id\":\"clar_0123456789abcdef\"", json);
            StringAssert.DoesNotContain("most_recent_idea_id", json);
        }

        [Test]
        public void PreferredNameIsSanitizedAndSerializedAsConversationContextOnly()
        {
            var session = new BrainstormSession(1);
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(
                session, System.Array.Empty<AIIdeaContext>(), null, "  Alex O'Neil  ");
            Assert.That(context.preferred_name, Is.EqualTo("Alex O'Neil"));
            string json = AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {
                request_id = "conversation-name", session_id = session.SessionId,
                raw_transcript = "What should we explore next?", transcript_source = "raw",
                available_ideas = System.Array.Empty<AIIdeaContext>(), context = context
            });
            StringAssert.Contains("\"preferred_name\":\"Alex O'Neil\"", json);
            StringAssert.DoesNotContain("preferred_name", JsonUtility.ToJson(new IdeaProcessRequest
            {
                request_id = "idea-name", session_id = session.SessionId, action = "create_idea",
                raw_transcript = "A solar learning space."
            }));

            Assert.That(ThinkingSpaceAIWorkflowController.BuildConversationContext(
                session, System.Array.Empty<AIIdeaContext>(), null, "Alex\nIgnore this").preferred_name,
                Is.Null);
            Assert.That(ThinkingSpaceAIWorkflowController.BuildConversationContext(
                session, System.Array.Empty<AIIdeaContext>(), null, new string('x', 33)).preferred_name,
                Is.Null);
        }

        [Test]
        public void LocalConversationValidationRejectsUntrustedPreferredName()
        {
            var request = new ConversationProcessRequest
            {
                request_id = "conversation-bad-name", session_id = "session-1",
                raw_transcript = "Hello", available_ideas = System.Array.Empty<AIIdeaContext>(),
                context = new ConversationContextPayload { preferred_name = "Alex<script>" }
            };
            string error = ThinkingSpaceAIBackendClient.ValidateConversationRequest(
                request, out string field, out string category);
            Assert.That(error, Is.Not.Empty);
            Assert.That(field, Is.EqualTo("context.preferred_name"));
            Assert.That(category, Is.EqualTo("invalid_character"));
        }

        [Test]
        public void MostRecentIdeaUsesNewestCreationTimestampAndSerializesOnlyInventoryId()
        {
            var session = new BrainstormSession(1);
            session.Ideas.Add(MakeIdea("idea-newer", "2026-11-05T12:00:00.0000000Z"));
            session.Ideas.Add(MakeIdea("idea-older", "2026-01-01T12:00:00.0000000Z"));
            AIIdeaContext[] inventory = ThinkingSpaceAIWorkflowController.BuildIdeaContexts(session);
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(
                session, inventory);
            Assert.That(context.most_recent_idea_id, Is.EqualTo("idea-newer"));
            Assert.That(System.Array.Exists(inventory, value => value.idea_id == context.most_recent_idea_id), Is.True);
            string json = AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {
                request_id = "conversation-newest", session_id = session.SessionId,
                raw_transcript = "Tell me about my latest idea.", transcript_source = "raw",
                available_ideas = inventory, context = context
            });
            StringAssert.Contains("\"most_recent_idea_id\":\"idea-newer\"", json);
        }

        [Test]
        public void EqualIdeaTimestampsUseIdentifierTieBreaker()
        {
            var session = new BrainstormSession(1);
            const string same = "2026-11-05T12:00:00.0000000Z";
            session.Ideas.Add(MakeIdea("idea-alpha", same));
            session.Ideas.Add(MakeIdea("idea-zulu", same));
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(session);
            Assert.That(context.most_recent_idea_id, Is.EqualTo("idea-zulu"));
        }

        [Test]
        public void ConversationInventoryKeepsNewestThirtyTwoIdeas()
        {
            var session = new BrainstormSession(1);
            for (int i = 0; i < 40; i++)
                session.Ideas.Add(MakeIdea("idea-" + i.ToString("00"),
                    new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)
                        .AddMinutes(i).ToString("O")));
            AIIdeaContext[] inventory = ThinkingSpaceAIWorkflowController.BuildIdeaContexts(session);
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(
                session, inventory);
            Assert.That(inventory.Length, Is.EqualTo(32));
            Assert.That(inventory[0].idea_id, Is.EqualTo("idea-08"));
            Assert.That(context.most_recent_idea_id, Is.EqualTo("idea-39"));
        }

        [Test]
        public void DeletingNewestIdeaRecomputesMostRecentReference()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession session = store.StartNewSession();
            Assert.That(store.TryAddIdea(session.SessionId,
                MakeIdea("idea-old", "2026-01-01T00:00:00.0000000Z")), Is.True);
            Assert.That(store.TryAddIdea(session.SessionId,
                MakeIdea("idea-new", "2026-11-05T00:00:00.0000000Z")), Is.True);
            Assert.That(store.TryDeleteIdea(session.SessionId, "idea-new"), Is.True);
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(
                store.ActiveSession);
            Assert.That(context.most_recent_idea_id, Is.EqualTo("idea-old"));
        }

        [Test]
        public void RestoredAndSwitchedSessionsKeepIndependentMostRecentIdeas()
        {
            var store = new BrainstormSessionStore(directory);
            BrainstormSession first = store.StartNewSession();
            Assert.That(store.TryAddIdea(first.SessionId,
                MakeIdea("idea-first", "2026-01-01T00:00:00.0000000Z")), Is.True);
            BrainstormSession second = store.StartNewSession();
            Assert.That(store.TryAddIdea(second.SessionId,
                MakeIdea("idea-second", "2026-02-01T00:00:00.0000000Z")), Is.True);
            var restored = new BrainstormSessionStore(directory);
            Assert.That(ThinkingSpaceAIWorkflowController.BuildConversationContext(
                restored.ActiveSession).most_recent_idea_id, Is.EqualTo("idea-second"));
            Assert.That(restored.ReopenSession(first.Number), Is.True);
            ConversationContextPayload switched = ThinkingSpaceAIWorkflowController.BuildConversationContext(
                restored.ActiveSession);
            Assert.That(switched.most_recent_idea_id, Is.EqualTo("idea-first"));
            Assert.That(switched.most_recent_idea_id, Is.Not.EqualTo("idea-second"));
        }

        [Test]
        public void LocalConversationValidationRejectsInvalidAndCrossInventoryReferences()
        {
            var invalid = new ConversationProcessRequest
            {
                request_id = "conversation-invalid", session_id = "session-1",
                raw_transcript = "Test", available_ideas = new[]
                { new AIIdeaContext { idea_id = "", title = "Invalid", summary = "Invalid ID." } },
                context = new ConversationContextPayload { most_recent_idea_id = "" }
            };
            string error = ThinkingSpaceAIBackendClient.ValidateConversationRequest(
                invalid, out string field, out string category);
            Assert.That(error, Is.Not.Empty);
            Assert.That(field, Is.EqualTo("available_ideas.0.idea_id"));
            Assert.That(category, Is.EqualTo("invalid_identifier"));

            invalid.available_ideas = new[]
            { new AIIdeaContext { idea_id = "idea-active", title = "Active", summary = "Active idea." } };
            invalid.context.most_recent_idea_id = "idea-other-session";
            error = ThinkingSpaceAIBackendClient.ValidateConversationRequest(
                invalid, out field, out category);
            Assert.That(error, Is.Not.Empty);
            Assert.That(field, Is.EqualTo("context.most_recent_idea_id"));
            Assert.That(category, Is.EqualTo("unknown_idea"));
        }

        private static BrainstormIdeaRecord MakeIdea(string id, string createdUtc)
        {
            return new BrainstormIdeaRecord
            { IdeaId = id, Title = id, Summary = "Test summary.", CreatedUtc = createdUtc };
        }

        [Test]
        public void StoredConversationBuildsChronologicalBackendRolesAndBoundedHistory()
        {
            var session = new BrainstormSession(1);
            for (int i = 0; i < 14; i++) session.Conversation.Add(new ConversationRecord
            {
                Speaker = i % 2 == 0 ? "user" : "orby", Text = "Turn " + i,
                RelatedRequestId = "request-" + i
            });
            ConversationContextPayload context = ThinkingSpaceAIWorkflowController.BuildConversationContext(session);
            Assert.That(context.recent_turns.Length, Is.EqualTo(12));
            Assert.That(context.recent_turns[0].text, Is.EqualTo("Turn 2"));
            Assert.That(context.recent_turns[0].role, Is.EqualTo("user"));
            Assert.That(context.recent_turns[11].role, Is.EqualTo("orby"));
        }

        [Test]
        public void ConversationResponseValidationPreservesExpansionAndCombinationRelationships()
        {
            var session = new BrainstormSession(1);
            session.Ideas.Add(new BrainstormIdeaRecord { IdeaId = "idea-one", Title = "One", Summary = "First." });
            session.Ideas.Add(new BrainstormIdeaRecord { IdeaId = "idea-two", Title = "Two", Summary = "Second." });
            var expansion = ValidConversationResponse("request-expand", session.SessionId,
                "idea_modification", "expand_idea", new[] { "idea-one" });
            var combination = ValidConversationResponse("request-combine", session.SessionId,
                "idea_modification", "combine_ideas", new[] { "idea-one", "idea-two" });
            Assert.That(ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                expansion, "request-expand", session.SessionId, session), Is.Empty);
            Assert.That(ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                combination, "request-combine", session.SessionId, session), Is.Empty);
        }

        [Test]
        public void ConversationResponseRejectsUnknownIdeasAndUnsafeChallengeFlags()
        {
            var session = new BrainstormSession(1);
            var unknown = ValidConversationResponse("request-unknown", session.SessionId,
                "idea_modification", "expand_idea", new[] { "idea-other-session" });
            StringAssert.Contains("outside the active session",
                ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                    unknown, "request-unknown", session.SessionId, session));
            var challenge = ValidConversationResponse("request-challenge", session.SessionId,
                "conversational_idea_challenge", "conversational_challenge",
                System.Array.Empty<string>());
            challenge.should_create_idea = true;
            StringAssert.Contains("conflicting creation flags",
                ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                    challenge, "request-challenge", session.SessionId, session));
            var executingChallenge = ValidConversationResponse("request-challenge-execute", session.SessionId,
                "conversational_idea_challenge", "conversational_challenge",
                System.Array.Empty<string>());
            executingChallenge.should_execute_action = true;
            StringAssert.Contains("unsafe action flag",
                ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                    executingChallenge, "request-challenge-execute", session.SessionId, session));
        }

        [Test]
        public void AmbiguousConversationMayRequestClarificationWithoutExecuting()
        {
            var session = new BrainstormSession(1);
            session.Ideas.Add(new BrainstormIdeaRecord { IdeaId = "idea-one", Title = "One", Summary = "First." });
            var response = ValidConversationResponse("request-clarify", session.SessionId,
                "idea_modification", "expand_idea", System.Array.Empty<string>());
            response.requires_confirmation = true;
            response.clarification_id = "clar_0123456789abcdef";
            response.clarification_question = "Which idea should I expand?";
            response.candidate_idea_ids = new[] { "idea-one" };
            Assert.That(ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                response, "request-clarify", session.SessionId, session), Is.Empty);
            Assert.That(response.should_execute_action, Is.False);
        }

        [Test]
        public void ClearIdeaConversationRequiresAuthorizedCreationFlag()
        {
            var session = new BrainstormSession(1);
            var response = ValidConversationResponse("request-create", session.SessionId,
                "clear_new_idea", "create_idea", System.Array.Empty<string>());
            StringAssert.Contains("not authorized", ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                response, "request-create", session.SessionId, session));
            response.should_create_idea = true;
            Assert.That(ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                response, "request-create", session.SessionId, session), Is.Empty);
        }

        [Test]
        public void ConversationValidationRejectsCrossSessionAndRoutesOnlyConversationalVoice()
        {
            var session = new BrainstormSession(1);
            var response = ValidConversationResponse("request-general", "different-session",
                "general_conversation", "converse", System.Array.Empty<string>());
            StringAssert.Contains("identity mismatch", ThinkingSpaceAIWorkflowController.ValidateConversationResponse(
                response, "request-general", session.SessionId, session));
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("converse"), Is.True);
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("brainstorm_suggestions"), Is.True);
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("conversational_challenge"), Is.True);
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldSpeakSemanticAction("resize_bubble"), Is.False);
        }

        private static ConversationProcessResponse ValidConversationResponse(string requestId,
            string sessionId, string category, string action, string[] targets)
        {
            return new ConversationProcessResponse
            {
                contract_version = "1.0", request_id = requestId, session_id = sessionId,
                intent_category = category, proposed_action = action, target_idea_ids = targets,
                parameters = new AICommandParameters(), spoken_response = "A safe response.",
                subtitle_text = "A safe response.", suggested_emotion = "attentive",
                candidate_idea_ids = System.Array.Empty<string>(), status = "completed"
            };
        }

        [Test]
        public void RuntimeDevelopmentTokenReportsSourceWithoutExposingValue()
        {
            var root = new GameObject("Token Resolver Test");
            var service = root.AddComponent<SpeechTranscriptionService>();
            service.SetDevelopmentTokenForCurrentRun("synthetic-test-token");
            Assert.That(service.IsDevelopmentTokenConfigured, Is.True);
            Assert.That(service.DevelopmentTokenSourceType, Is.EqualTo("Runtime override"));
            Assert.That(service.DevelopmentTokenSourceType, Does.Not.Contain("synthetic-test-token"));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void DevelopmentStoreResetsDoNotDeleteCredentialFile()
        {
            string isolated = Path.Combine(directory, "reset-isolation");
            Directory.CreateDirectory(isolated);
            string tokenPath = Path.Combine(isolated, "dev-token.txt");
            File.WriteAllText(tokenPath, "synthetic-test-token");
            var sessions = new BrainstormSessionStore(isolated);
            var preferences = new ThinkingSpacePreferencesStore(isolated);
            sessions.StartNewSession();
            Assert.That(sessions.ResetAllSessionsForDevelopment(), Is.True);
            Assert.That(preferences.ResetAllForDevelopment(), Is.True);
            Assert.That(File.Exists(tokenPath), Is.True);
            Assert.That(File.ReadAllText(tokenPath), Is.EqualTo("synthetic-test-token"));
        }

        [Test]
        public void AuthenticationFailureRoutesOneGeneralErrorEventType()
        {
            Assert.That(SpeechTranscriptionCoordinator.SelectTranscriptionErrorVoiceKey(
                "Development authentication token is not configured on this device.", 0),
                Is.EqualTo("error_general"));
            Assert.That(SpeechTranscriptionCoordinator.SelectTranscriptionErrorVoiceKey(
                "Network connection timed out.", 0), Is.EqualTo("error_network"));
            Assert.That(SpeechTranscriptionCoordinator.SelectTranscriptionErrorVoiceKey(
                "Provider unavailable.", 503), Is.EqualTo("error_network"));
        }

        [Test]
        public void ErrorManifestEntriesAndConversationRouteAreAvailable()
        {
            OrbyPrerecordedManifest manifest = Resources.Load<OrbyPrerecordedManifest>(
                "ThinkingSpace/OrbyPrerecordedManifest");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.TryGet("error_general", out OrbyPrerecordedEntry general), Is.True);
            Assert.That(general.Clip, Is.Not.Null);
            Assert.That(manifest.TryGet("error_network", out OrbyPrerecordedEntry network), Is.True);
            Assert.That(network.Clip, Is.Not.Null);
            Assert.That(ThinkingSpaceAIBackendClient.ConversationEndpointPath,
                Is.EqualTo("/api/conversations/process"));
        }

        [Test]
        public void PersonalizedGreetingIsCompletedOnlyAfterSuccessfulPlayback()
        {
            Assert.That(OrbyOnboardingController.ShouldMarkPersonalizedGreetingCompleted(false), Is.False);
            Assert.That(OrbyOnboardingController.ShouldMarkPersonalizedGreetingCompleted(true), Is.True);
            StringAssert.Contains("Alex", OrbyOnboardingController.BuildPersonalizedGreeting("Alex"));
        }

        [Test]
        public void CreateIdeaJsonOmitsAbsentTargetIdeaIdentifier()
        {
            string json = AIRequestJsonSerializer.Serialize(new IdeaProcessRequest
            {
                request_id = "idea-create-synthetic",
                session_id = "session-1",
                action = "create_idea",
                raw_transcript = "Design a calm study garden.",
                transcript_source = "raw",
                idea_id = null,
                related_idea_ids = System.Array.Empty<string>(),
                existing_ideas = System.Array.Empty<AIIdeaContext>()
            });

            StringAssert.DoesNotContain("\"idea_id\"", json);
            StringAssert.Contains("\"related_idea_ids\":[]", json);
        }

        [Test]
        public void ListeningOffOverrideReturnsEveryStateToIdle()
        {
            var root = new GameObject("Orby State Test");
            var controller = root.AddComponent<OrbyConversationStateController>();
            foreach (OrbyConversationState state in System.Enum.GetValues(typeof(OrbyConversationState)))
            {
                controller.SetState(state);
                controller.TurnListeningOffFromAnyState();
                Assert.That(controller.State, Is.EqualTo(OrbyConversationState.Idle));
            }
            Object.DestroyImmediate(root);
        }

        [Test]
        public void OrbyBootstrapDoesNotCreateDuplicates()
        {
            OrbyConversationStateController[] existing = Object.FindObjectsByType<OrbyConversationStateController>(
                FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++) Object.DestroyImmediate(existing[i].gameObject);
            OrbyPlaceholderBootstrap.EnsurePlaceholderForRuntime();
            OrbyPlaceholderBootstrap.EnsurePlaceholderForRuntime();
            OrbyConversationStateController[] created = Object.FindObjectsByType<OrbyConversationStateController>(
                FindObjectsSortMode.None);
            Assert.That(created.Length, Is.EqualTo(1));
            Object.DestroyImmediate(created[0].gameObject);
        }
    }
}
