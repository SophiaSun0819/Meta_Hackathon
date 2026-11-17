using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThinkingSpace.Pairing;
using ThinkingSpace.UI;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class QuestPairingLocalIntegrationTests
    {
        private string root;

        [SetUp] public void SetUp()=>root=Path.Combine(Path.GetTempPath(),"thinking-space-pairing-"+Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown(){if(Directory.Exists(root))Directory.Delete(root,true);}

        [Test]
        public void ContractIsPinnedToBackend019Document()
        {
            Assert.That(JudgeAccountSyncContract.Identifier,Is.EqualTo("thinking-space-account-sync"));
            Assert.That(JudgeAccountSyncContract.PinnedDocumentSha256,
                Is.EqualTo("1342b9079b41e834d30439f7f9724f0f75aeb0257f49843e7965a1b66a44267b"));
            Assert.That(JudgeAccountSyncContract.PairingVersion,Is.EqualTo(1));
            Assert.That(JudgeAccountSyncContract.SyncVersion,Is.EqualTo(2));
        }

        [Test]
        public void PairingStartSerializesApprovedLowerCamelCaseFieldsOnly()
        {
            var dto=new PairingChallengeStartV1{DeviceLocalId="quest-install-id",DevicePublicKey="{\"kty\":\"EC\"}"};
            JObject json=JObject.Parse(JsonConvert.SerializeObject(dto));
            CollectionAssert.AreEquivalent(new[]{"contractVersion","deviceLocalId","devicePublicKey"},
                ((JObject)json).Properties().SelectNames());
            Assert.That((int)json["contractVersion"],Is.EqualTo(1));
        }

        [Test]
        public void LocalPairingIsExplicitlySimulatedAndCredentialsAreMemoryOnly()
        {
            using(var vault=new InMemoryQuestCredentialVault())
            using(var transport=new LocalQuestPairingTransport(vault))
            {
                PairingCodeResult result=default;
                transport.RequestPairingCode(value=>result=value);
                Assert.That(transport.IsLocalSimulation,Is.True);
                Assert.That(QuestPairingStateMachine.IsSixDigitCode(result.PairingCode),Is.True);
                Assert.That(transport.SimulateWebsiteClaim(result.PairingCode,"Local Tester"),Is.True);
                Assert.That(transport.TryGetStatus(result.RequestId,DateTime.UtcNow,out LocalPairingStatus status),Is.True);
                Assert.That(status.Status,Is.EqualTo(PairingChallengeStatus.CLAIMED));
                Assert.That(transport.ConfirmAndActivate(result.RequestId,true),Is.True);
                Assert.That(vault.HasCredential,Is.True);
                Assert.That(vault.TryLoad(out DeviceCredential credential),Is.True);
                Assert.That(credential.Token,Is.Not.Empty);
                transport.Unpair();
                Assert.That(vault.HasCredential,Is.False);
            }
        }

        [Test]
        public void DefaultControllerStillFailsClosedToUnavailableTransport()
        {
            var transport=new UnavailableQuestPairingTransport();
            Assert.That(transport.IsAvailable,Is.False);
            PairingCodeResult result=default;
            transport.RequestPairingCode(value=>result=value);
            Assert.That(result.Success,Is.False);
            Assert.That(result.PairingCode,Is.Empty);
        }

        [Test]
        public void UploadUsesStableOperationAndCanonicalFingerprint()
        {
            var adapter=CreateAdapter(out _);
            string json=ValidPortableJson("portable-1","Cloud title");
            string operationId="11111111-1111-4111-8111-111111111111";
            SessionSyncMutationV2 mutation=adapter.CreateUpload(json,operationId,"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","portable-1","local-1",0);
            Assert.That(mutation.OperationId,Is.EqualTo(operationId));
            Assert.That(mutation.Kind,Is.EqualTo("UPSERT"));
            Assert.That(mutation.Snapshot.Revision,Is.EqualTo(1));
            Assert.That(mutation.Snapshot.CanonicalFingerprint,
                Is.EqualTo(QuestSessionSyncAdapter.Sha256Hex(new UTF8Encoding(false).GetBytes(
                    QuestSessionSyncAdapter.CanonicalJson(JObject.Parse(json))))));
            Assert.That(mutation.Snapshot.CanonicalFingerprint,Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void LocalStoreIsIdempotentAndRejectsRevisionConflictWithoutOverwrite()
        {
            var adapter=CreateAdapter(out _);var store=new LocalSessionSyncStore();
            SessionSyncMutationV2 first=adapter.CreateUpload(ValidPortableJson("portable-2","One"),
                "22222222-2222-4222-8222-222222222222","bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb","portable-2","local-2",0);
            Assert.That(store.Apply(first),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            Assert.That(store.Apply(first),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            SessionSyncMutationV2 conflict=adapter.CreateUpload(ValidPortableJson("portable-2","Two"),
                "33333333-3333-4333-8333-333333333333","bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb","portable-2","local-2",0);
            Assert.That(store.Apply(conflict),Is.EqualTo(SessionMutationResultStatus.CONFLICT));
            Assert.That(store.TryDownload("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",out string downloaded,out int revision),Is.True);
            Assert.That(revision,Is.EqualTo(1));
            Assert.That((string)JObject.Parse(downloaded)["SessionTitle"],Is.EqualTo("One"));
        }

        [Test]
        public void LocalStorePreservesContentAcrossRenameDeleteAndRestore()
        {
            var adapter=CreateAdapter(out _);var store=new LocalSessionSyncStore();
            const string cloud="dddddddd-dddd-4ddd-8ddd-dddddddddddd";
            Assert.That(store.Apply(adapter.CreateUpload(ValidPortableJson("portable-5","Original"),
                "55555555-5555-4555-8555-555555555555",cloud,"portable-5","local-5",0)),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            Assert.That(store.Apply(adapter.CreateRename("66666666-6666-4666-8666-666666666666",cloud,
                "portable-5","local-5",1,"Renamed")),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            Assert.That(store.Apply(adapter.CreateDelete("77777777-7777-4777-8777-777777777777",cloud,
                "portable-5","local-5",2)),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            Assert.That(store.TryDownload(cloud,out _,out _),Is.False);
            Assert.That(store.TryGetTombstone(cloud,out SessionTombstoneV2 tombstone),Is.True);
            Assert.That(tombstone.DeletedRevision,Is.EqualTo(3));
            Assert.That(store.Apply(adapter.CreateRestore("88888888-8888-4888-8888-888888888888",cloud,
                "portable-5","local-5",3)),Is.EqualTo(SessionMutationResultStatus.APPLIED));
            Assert.That(store.TryDownload(cloud,out string restored,out int revision),Is.True);
            Assert.That(revision,Is.EqualTo(4));
            Assert.That((string)JObject.Parse(restored)["SessionTitle"],Is.EqualTo("Renamed"));
        }

        [Test]
        public void OfflineQueueIsAccountBoundIdempotentAndHoldsConflicts()
        {
            var adapter=CreateAdapter(out _);
            const string account="aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa";
            var queue=new QuestOfflineSyncQueue(account);
            SessionSyncMutationV2 mutation=adapter.CreateUpload(ValidPortableJson("portable-6","Offline"),
                "99999999-9999-4999-8999-999999999999","eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee","portable-6","local-6",0);
            queue.Enqueue(account,mutation);queue.Enqueue(account,mutation);
            Assert.That(queue.Count,Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(()=>queue.NextBatch("bbbbbbbb-1111-4111-8111-bbbbbbbbbbbb"));
            queue.ApplyResult(account,mutation.OperationId,SessionMutationResultStatus.CONFLICT);
            Assert.That(queue.Count,Is.EqualTo(1));
            queue.DiscardConflict(account,mutation.OperationId);
            Assert.That(queue.Count,Is.Zero);
        }

        [Test]
        public void DownloadRequiresConfirmationAndImportsAsNewSession()
        {
            QuestSessionSyncAdapter adapter=CreateAdapter(out BrainstormSessionStore sessions);
            BrainstormSession original=sessions.StartNewSession();
            SessionTransferResult denied=adapter.ImportDownloadedAsNew(ValidPortableJson("portable-3","Downloaded"),false);
            Assert.That(denied.Success,Is.False);
            Assert.That(sessions.Sessions.Count,Is.EqualTo(1));
            Assert.That(sessions.ActiveSession,Is.SameAs(original));

            SessionTransferResult imported=adapter.ImportDownloadedAsNew(ValidPortableJson("portable-3","Downloaded"),true);
            Assert.That(imported.Success,Is.True,imported.Message);
            Assert.That(sessions.Sessions.Count,Is.EqualTo(2));
            Assert.That(imported.ImportedSession.SessionId,Is.Not.EqualTo("portable-3"));
            Assert.That(File.Exists(imported.FilePath),Is.False,"staged download must be removed");
        }

        [Test]
        public void SyncAdapterRejectsPayloadOverOneMiB()
        {
            var adapter=CreateAdapter(out _);
            string oversized=ValidPortableJson("portable-4",new string('x',JudgeAccountSyncContract.MaximumRequestBytes));
            Assert.Throws<InvalidDataException>(()=>adapter.CreateUpload(oversized,"44444444-4444-4444-8444-444444444444","cccccccc-cccc-4ccc-8ccc-cccccccccccc","portable-4","local-4",0));
        }

        private QuestSessionSyncAdapter CreateAdapter(out BrainstormSessionStore sessions)
        {
            sessions=new BrainstormSessionStore(Path.Combine(root,"store"));
            var transfer=new ThinkingSpaceSessionTransferService(sessions,Path.Combine(root,"exports"),Path.Combine(root,"imports"));
            return new QuestSessionSyncAdapter(transfer);
        }

        private static string ValidPortableJson(string sessionId,string title)
        {
            var document=new ThinkingSpaceSessionDocument{SessionId=sessionId,SessionTitle=title,SessionNumber=1,
                ExportedUtc="2026-11-14T12:00:00.0000000Z",CreatedUtc="2026-11-14T11:00:00.0000000Z",
                UpdatedUtc="2026-11-14T11:30:00.0000000Z"};
            return UnityEngine.JsonUtility.ToJson(document,false);
        }
    }

    internal static class JsonPropertyTestExtensions
    {
        public static string[] SelectNames(this System.Collections.Generic.IEnumerable<JProperty> properties)
        {var names=new System.Collections.Generic.List<string>();foreach(JProperty property in properties)names.Add(property.Name);return names.ToArray();}
    }
}
