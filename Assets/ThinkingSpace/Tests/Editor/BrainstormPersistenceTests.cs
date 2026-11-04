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
