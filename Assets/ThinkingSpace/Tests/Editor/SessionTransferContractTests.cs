using System;
using System.IO;
using NUnit.Framework;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class SessionTransferContractTests
    {
        private string temporaryDirectory;

        private static string FixtureRoot => Path.Combine(Application.dataPath,
            "ThinkingSpace", "Tests", "Fixtures", "SessionTransfer", "v1");

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(),
                "thinking-space-session-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }

        [TestCase("v1-empty.json")]
        [TestCase("v1-populated.json")]
        [TestCase("v1-deleted-idea-history.json")]
        [TestCase("v1-spatial-transforms.json")]
        public void ValidFixturesMatchTheCanonicalIdentifierAndVersion(string fileName)
        {
            ThinkingSpaceSessionDocument document = ReadFixture(fileName);
            Assert.That(document.FormatIdentifier,
                Is.EqualTo(ThinkingSpaceSessionTransferService.FormatIdentifier));
            Assert.That(document.FormatVersion,
                Is.EqualTo(ThinkingSpaceSessionTransferService.CurrentFormatVersion));
            Assert.That(ThinkingSpaceSessionTransferService.Validate(document), Is.Empty);
        }

        [Test]
        public void PopulatedFixturePreservesOrderingIdentifiersColorsTranscriptsAndConversation()
        {
            ThinkingSpaceSessionDocument document = ReadFixture("v1-populated.json");
            Assert.That(document.Ideas, Has.Count.EqualTo(2));
            Assert.That(document.Ideas[0].IdeaId, Is.EqualTo("idea-solar-hub"));
            Assert.That(document.Ideas[1].IdeaId, Is.EqualTo("idea-tool-library"));
            Assert.That(document.Ideas[1].VisualColor, Is.EqualTo("Purple"));
            Assert.That(document.Ideas[0].SourceTranscript,
                Is.EqualTo("We could build a solar learning hub for the neighborhood."));
            Assert.That(document.Relationships, Has.Count.EqualTo(1));
            Assert.That(document.Relationships[0].SourceIdeaId, Is.EqualTo("idea-tool-library"));
            Assert.That(document.Relationships[0].TargetIdeaId, Is.EqualTo("idea-solar-hub"));
            Assert.That(document.Relationships[0].RelationshipType, Is.EqualTo("derived_from"));
            Assert.That(document.Conversation, Has.Count.EqualTo(2));
            Assert.That(document.Conversation[0].MessageId, Is.EqualTo("message-user-001"));
            Assert.That(document.Conversation[1].RelatedRequestId, Is.EqualTo("request-idea-002"));
            Assert.That(document.Conversation[1].RelatedIdeaIds,
                Is.EqualTo(new[] { "idea-tool-library", "idea-solar-hub" }));
        }

        [Test]
        public void QuaternionWAndNumericTransformValuesSurviveJsonRoundTrip()
        {
            ThinkingSpaceSessionDocument original = ReadFixture("v1-spatial-transforms.json");
            string serialized = JsonUtility.ToJson(original, true);
            ThinkingSpaceSessionDocument restored =
                JsonUtility.FromJson<ThinkingSpaceSessionDocument>(serialized);
            BubbleTransformRecord expected = original.Ideas[0].Bubble;
            BubbleTransformRecord actual = restored.Ideas[0].Bubble;
            Assert.That(actual.Position, Is.EqualTo(expected.Position));
            Assert.That(actual.Rotation.x, Is.EqualTo(expected.Rotation.x));
            Assert.That(actual.Rotation.y, Is.EqualTo(expected.Rotation.y));
            Assert.That(actual.Rotation.z, Is.EqualTo(expected.Rotation.z));
            Assert.That(actual.Rotation.w, Is.EqualTo(expected.Rotation.w));
            Assert.That(actual.Scale, Is.EqualTo(expected.Scale));
        }

        [Test]
        public void HistoricalConversationReferenceToDeletedIdeaRemainsValid()
        {
            ThinkingSpaceSessionDocument document = ReadFixture("v1-deleted-idea-history.json");
            Assert.That(ThinkingSpaceSessionTransferService.Validate(document), Is.Empty);
            Assert.That(document.Conversation[0].RelatedIdeaIds,
                Is.EqualTo(new[] { "idea-deleted" }));
        }

        [Test]
        public void InvalidLiveRelationshipAndUnsupportedVersionAreRejected()
        {
            Assert.That(ThinkingSpaceSessionTransferService.Validate(
                ReadFixture("v1-invalid-relationship.json")), Does.Contain("broken"));
            Assert.That(ThinkingSpaceSessionTransferService.Validate(
                ReadFixture("v1-unsupported-version.json")), Does.Contain("unsupported"));
        }

        [Test]
        public void JsonUtilityUnknownFieldLimitationIsExplicitlyDocumented()
        {
            string json = ReadFixtureText("v1-unknown-field.json");
            Assert.That(json, Does.Contain("FutureMetadata"));
            ThinkingSpaceSessionDocument document =
                JsonUtility.FromJson<ThinkingSpaceSessionDocument>(json);
            Assert.That(ThinkingSpaceSessionTransferService.Validate(document), Is.Empty,
                "Unity JsonUtility ignores unknown fields. Strict rejection belongs in the web adapter; " +
                "the original canonical bytes must be retained to avoid silent data loss.");
            Assert.That(JsonUtility.ToJson(document), Does.Not.Contain("FutureMetadata"));
        }

        [Test]
        public void ImportDefaultsToNewSessionIdentityWhilePreservingCanonicalContents()
        {
            string storePath = Path.Combine(temporaryDirectory, "store");
            string exports = Path.Combine(temporaryDirectory, "exports");
            string imports = Path.Combine(temporaryDirectory, "imports");
            Directory.CreateDirectory(imports);
            File.Copy(Path.Combine(FixtureRoot, "v1-populated.json"),
                Path.Combine(imports, "v1-populated.json"));

            var store = new BrainstormSessionStore(storePath);
            var transfer = new ThinkingSpaceSessionTransferService(store, exports, imports);
            SessionTransferResult result = transfer.ImportSession("v1-populated.json", true);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.ImportedSession.SessionId, Is.Not.EqualTo("session-populated"));
            Assert.That(result.ImportedSession.Ideas, Has.Count.EqualTo(2));
            Assert.That(result.ImportedSession.Ideas[0].IdeaId, Is.EqualTo("idea-solar-hub"));
            Assert.That(result.ImportedSession.Ideas[1].IdeaId, Is.EqualTo("idea-tool-library"));
            Assert.That(result.ImportedSession.Relationships[0].RelationshipType,
                Is.EqualTo("derived_from"));
            Assert.That(result.ImportedSession.Conversation[0].MessageId,
                Is.EqualTo("message-user-001"));
            Assert.That(result.ImportedSession.Ideas[1].Bubble.Rotation.w,
                Is.EqualTo(0.9238795f));
        }

        private static ThinkingSpaceSessionDocument ReadFixture(string fileName)
            => JsonUtility.FromJson<ThinkingSpaceSessionDocument>(ReadFixtureText(fileName));

        private static string ReadFixtureText(string fileName)
            => File.ReadAllText(Path.Combine(FixtureRoot, fileName));
    }
}
