using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class MoleculeCombineTests
    {
        private string directory;
        private BrainstormSessionStore store;
        private BrainstormSession session;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ThinkingSpaceMoleculeTests", System.Guid.NewGuid().ToString("N"));
            store = new BrainstormSessionStore(directory);
            session = store.StartNewSession();
            foreach (string id in new[] { "a", "b", "c", "d", "e" })
                Assert.That(store.TryAddIdea(session.SessionId, Idea(id)), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static BrainstormIdeaRecord Idea(string id, params string[] sources) => new BrainstormIdeaRecord
        {
            IdeaId = id, Title = "Title " + id, Summary = "Summary " + id, Category = "General",
            RelatedIdeaIds = new List<string>(sources)
        };

        private void AddCenter(string id, params string[] sources)
            => Assert.That(store.TryAddIdea(session.SessionId, Idea(id, sources)), Is.True);

        [Test]
        public void TwoStandaloneIdeasFormANewMolecule()
        {
            MoleculeRules.FuseDecision decision = MoleculeRules.EvaluateFuse(session, "a", "b");
            Assert.That(decision.Outcome, Is.EqualTo(MoleculeRules.FuseOutcome.NewMolecule));
            Assert.That(decision.CenterIdeaId, Is.Null);
            Assert.That(decision.SourceIdeaIds, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void ExistingCombinedIdeaIsAMoleculeAndItsSourcesAreLocked()
        {
            AddCenter("m", "a", "b");
            Assert.That(MoleculeRules.IsCenter(session, "m"), Is.True);
            Assert.That(MoleculeRules.FindCenterOf(session, "a"), Is.EqualTo("m"));
            Assert.That(MoleculeRules.IsLockedSource(session, "b"), Is.True);
            Assert.That(MoleculeRules.IsLockedSource(session, "c"), Is.False);
            Assert.That(MoleculeRules.IsLockedSource(session, "m"), Is.False);
        }

        [Test]
        public void TouchingAMoleculeOrItsSourceAddsTheStandaloneIdea()
        {
            AddCenter("m", "a", "b");
            foreach (string member in new[] { "m", "a" })
            {
                MoleculeRules.FuseDecision decision = MoleculeRules.EvaluateFuse(session, "c", member);
                Assert.That(decision.Outcome, Is.EqualTo(MoleculeRules.FuseOutcome.AddToMolecule), member);
                Assert.That(decision.CenterIdeaId, Is.EqualTo("m"));
                Assert.That(decision.SourceIdeaIds, Is.EqualTo(new[] { "a", "b", "c" }));
            }
        }

        [Test]
        public void FullMoleculeAndMoleculeToMoleculeAreRejectedWithASpokenReason()
        {
            AddCenter("m", "a", "b", "c");
            MoleculeRules.FuseDecision full = MoleculeRules.EvaluateFuse(session, "d", "m");
            Assert.That(full.Outcome, Is.EqualTo(MoleculeRules.FuseOutcome.RejectedMoleculeFull));
            Assert.That(MoleculeRules.RejectionLine(full.Outcome), Does.Contain("three"));

            AddCenter("n", "d", "e");
            MoleculeRules.FuseDecision pair = MoleculeRules.EvaluateFuse(session, "a", "n");
            Assert.That(pair.Outcome, Is.EqualTo(MoleculeRules.FuseOutcome.RejectedMoleculeToMolecule));
            Assert.That(MoleculeRules.EvaluateFuse(session, "a", "b").Outcome,
                Is.EqualTo(MoleculeRules.FuseOutcome.RejectedMoleculeToMolecule), "Same molecule cannot fuse with itself.");
            Assert.That(MoleculeRules.EvaluateFuse(session, "a", "a").Outcome,
                Is.EqualTo(MoleculeRules.FuseOutcome.RejectedSameIdea));
        }

        [Test]
        public void PullingOutKeepsTwoSourcesOrDissolves()
        {
            AddCenter("m", "a", "b", "c");
            MoleculeRules.PullOutDecision keep = MoleculeRules.EvaluatePullOut(session, "m", "b");
            Assert.That(keep.Valid && !keep.Dissolve, Is.True);
            Assert.That(keep.RemainingSourceIdeaIds, Is.EqualTo(new[] { "a", "c" }));

            Assert.That(store.TryUpdateCombinedIdea(session.SessionId, "m", "Pair", "Two left.", "General",
                new[] { "a", "c" }), Is.True);
            MoleculeRules.PullOutDecision dissolve = MoleculeRules.EvaluatePullOut(session, "m", "a");
            Assert.That(dissolve.Valid && dissolve.Dissolve, Is.True);
            Assert.That(MoleculeRules.EvaluatePullOut(session, "m", "d").Valid, Is.False);
        }

        [Test]
        public void UpdatingACenterKeepsItsPreviousVersionAndRebuildsBonds()
        {
            AddCenter("m", "a", "b");
            Assert.That(store.TryUpdateCombinedIdea(session.SessionId, "m", "Trio", "Three ideas.", "Science",
                new[] { "a", "b", "c" }), Is.True);
            BrainstormIdeaRecord center = session.Ideas.Find(idea => idea.IdeaId == "m");
            Assert.That(center.Title, Is.EqualTo("Trio"));
            Assert.That(center.PreviousVersions.Count, Is.EqualTo(1));
            Assert.That(center.PreviousVersions[0].Title, Is.EqualTo("Title m"));
            Assert.That(center.PreviousVersions[0].RelatedIdeaIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(MoleculeRules.GetSources(session, "m"), Is.EqualTo(new[] { "a", "b", "c" }));

            var reloaded = new BrainstormSessionStore(directory).ActiveSession;
            BrainstormIdeaRecord persisted = reloaded.Ideas.Find(idea => idea.IdeaId == "m");
            Assert.That(persisted.PreviousVersions.Count, Is.EqualTo(1), "Versions survive a reload.");
            Assert.That(MoleculeRules.GetSources(reloaded, "m"), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void RestoringThePreviousVersionUndoesTheLastChange()
        {
            AddCenter("m", "a", "b");
            store.TryUpdateCombinedIdea(session.SessionId, "m", "Trio", "Three ideas.", "Science", new[] { "a", "b", "c" });
            Assert.That(store.TryRestorePreviousVersion(session.SessionId, "m"), Is.True);
            BrainstormIdeaRecord center = session.Ideas.Find(idea => idea.IdeaId == "m");
            Assert.That(center.Title, Is.EqualTo("Title m"));
            Assert.That(center.PreviousVersions, Is.Empty);
            Assert.That(MoleculeRules.GetSources(session, "m"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(store.TryRestorePreviousVersion(session.SessionId, "m"), Is.False);
        }

        [Test]
        public void DissolvingFreesEverySourceButKeepsTheCenterIdea()
        {
            AddCenter("m", "a", "b");
            Assert.That(store.TryDissolveMolecule(session.SessionId, "m"), Is.True);
            Assert.That(MoleculeRules.IsCenter(session, "m"), Is.False);
            Assert.That(MoleculeRules.IsLockedSource(session, "a"), Is.False);
            Assert.That(session.Ideas.Exists(idea => idea.IdeaId == "m"), Is.True);
        }

        [Test]
        public void UpdateRejectsInvalidSources()
        {
            AddCenter("m", "a", "b");
            Assert.That(store.TryUpdateCombinedIdea(session.SessionId, "m", "T", "S", "G", new[] { "a" }), Is.False);
            Assert.That(store.TryUpdateCombinedIdea(session.SessionId, "m", "T", "S", "G", new[] { "a", "m" }), Is.False);
            Assert.That(store.TryUpdateCombinedIdea(session.SessionId, "m", "T", "S", "G", new[] { "a", "zz" }), Is.False);
            Assert.That(session.Ideas.Find(idea => idea.IdeaId == "m").PreviousVersions, Is.Empty);
        }

        [Test]
        public void RequestCarriesCenterContextAndSerializesCenterOnlyWhenSet()
        {
            AddCenter("m", "a", "b");
            IdeaProcessRequest update = MoleculeCombineCoordinator.BuildRequest(session, "req-1", "m",
                new[] { "a", "b", "c" });
            Assert.That(update.raw_transcript, Is.EqualTo(MoleculeCombineCoordinator.GestureTranscript));
            Assert.That(update.existing_ideas.Length, Is.EqualTo(4));
            StringAssert.Contains("\"center_idea_id\":\"m\"", AIRequestJsonSerializer.Serialize(update));

            IdeaProcessRequest fresh = MoleculeCombineCoordinator.BuildRequest(session, "req-2", null, new[] { "c", "d" });
            string json = AIRequestJsonSerializer.Serialize(fresh);
            Assert.That(json, Does.Not.Contain("center_idea_id"));
            Assert.That(json, Does.Not.Contain("\"idea_id\":\"\""));
        }

        [Test]
        public void TouchingBubblesHaveNoSurfaceGap()
        {
            Assert.That(IdeaBubbleManager.SurfaceGap(Vector3.zero, .1f, new Vector3(.2f, 0f, 0f), .1f),
                Is.EqualTo(0f).Within(1e-5f));
            Assert.That(IdeaBubbleManager.SurfaceGap(Vector3.zero, .1f, new Vector3(.3f, 0f, 0f), .1f),
                Is.EqualTo(.1f).Within(1e-5f));
            Assert.That(IdeaBubbleManager.SurfaceGap(Vector3.zero, .1f, new Vector3(.15f, 0f, 0f), .1f), Is.LessThan(0f),
                "Overlapping bubbles count as touching.");
        }

        [Test]
        public void BondBreaksOnlyWhenStretchedPastTheThreshold()
        {
            Assert.That(IdeaBubbleManager.ShouldBreakBond(.02f, .02f, .12f), Is.False);
            Assert.That(IdeaBubbleManager.ShouldBreakBond(.13f, .02f, .12f), Is.False);
            Assert.That(IdeaBubbleManager.ShouldBreakBond(.15f, .02f, .12f), Is.True);
        }

        [Test]
        public void SourcesAreSpacedEvenlyAroundTheCenter()
        {
            Vector3[] pair = IdeaBubbleManager.ComputeSourceOffsets(2, .1f, .05f, .02f, Vector3.right, Vector3.up);
            Assert.That(pair[0].x, Is.EqualTo(.17f).Within(1e-4f), "Two sources sit left and right.");
            Assert.That(pair[1].x, Is.EqualTo(-.17f).Within(1e-4f));
            Assert.That(pair[0].y, Is.EqualTo(0f).Within(1e-4f));

            Vector3[] trio = IdeaBubbleManager.ComputeSourceOffsets(3, .1f, .05f, .02f, Vector3.right, Vector3.up);
            Assert.That(trio[0].y, Is.EqualTo(.17f).Within(1e-4f), "Three sources start at the top.");
            foreach (Vector3 offset in trio) Assert.That(offset.magnitude, Is.EqualTo(.17f).Within(1e-4f));
            Assert.That(Vector3.Distance(trio[0], trio[1]), Is.EqualTo(Vector3.Distance(trio[1], trio[2])).Within(1e-4f));
        }

        [Test]
        public void ResponseValidationRequiresTheRightCenter()
        {
            AddCenter("m", "a", "b");
            IdeaProcessRequest request = MoleculeCombineCoordinator.BuildRequest(session, "req-1", "m", new[] { "a", "b", "c" });
            var response = new MoleculeCombineResponse
            {
                status = "completed", request_id = "req-1", session_id = session.SessionId, idea_id = "m",
                title = "Trio", summary = "Three ideas.", updated_existing_idea = true
            };
            Assert.That(MoleculeCombineCoordinator.ValidateResponse(response, request, "m"), Is.Empty);
            response.idea_id = "idea_new";
            Assert.That(MoleculeCombineCoordinator.ValidateResponse(response, request, "m"), Is.Not.Empty);
            response.idea_id = "m"; response.updated_existing_idea = false;
            Assert.That(MoleculeCombineCoordinator.ValidateResponse(response, request, "m"), Is.Not.Empty);
            response.request_id = "other"; response.updated_existing_idea = true;
            Assert.That(MoleculeCombineCoordinator.ValidateResponse(response, request, "m"), Is.Not.Empty);
        }
    }
}
