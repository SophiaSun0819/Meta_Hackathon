using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ThinkingSpace.Pairing;
using ThinkingSpace.UI;
using UnityEditor;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class QuestPairingPreparationTests
    {
        private sealed class FakeTransport : IQuestPairingTransport
        {
            public bool IsAvailable { get; set; } = true;
            public string UnavailableMessageKey => "pairing.unavailable.body";
            public int RequestCount { get; private set; }
            public string CancelledRequestId { get; private set; } = string.Empty;
            public PairingCodeResult Result { get; set; }

            public void RequestPairingCode(Action<PairingCodeResult> completed)
            {
                RequestCount++;
                completed?.Invoke(Result);
            }

            public void CancelPairing(string requestId) => CancelledRequestId = requestId;
        }

        [Test]
        public void PairingStateEnumContainsEveryApprovedPresentationState()
        {
            string[] expected={"UnavailableOffline","Disconnected","RequestingCode","CodeReady",
                "WaitingForWebsite","AwaitingQuestConfirmation","Paired","Expired","Failed",
                "UnpairConfirmation"};
            CollectionAssert.AreEquivalent(expected,Enum.GetNames(typeof(QuestPairingUiState)));
        }

        [Test]
        public void UnavailableTransportNeverRequestsOrGeneratesACode()
        {
            DateTime now=new DateTime(2026,11,14,12,0,0,DateTimeKind.Utc);
            var machine=new QuestPairingStateMachine();
            var transport=new FakeTransport{IsAvailable=false};

            machine.BeginRequest(transport,now);

            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.UnavailableOffline));
            Assert.That(machine.Snapshot(now).PairingCode,Is.Empty);
            Assert.That(transport.RequestCount,Is.Zero);
        }

        [Test]
        public void ValidCodeCanAdvanceThroughWaitingConfirmationAndPairedStates()
        {
            DateTime now=DateTime.UtcNow;
            var machine=new QuestPairingStateMachine();
            var transport=new FakeTransport
            {
                Result=PairingCodeResult.Ready("request-1","123456",now.AddMinutes(2))
            };

            machine.BeginRequest(transport,now);
            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.CodeReady));
            Assert.That(machine.Snapshot(now).PairingCode,Is.EqualTo("123456"));
            machine.BeginWaiting(now);
            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.WaitingForWebsite));
            machine.ReceiveWebsiteConfirmation("Alex",now);
            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.AwaitingQuestConfirmation));
            machine.ConfirmAccount(true,now);
            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.Paired));
        }

        [Test]
        public void ExpirationClearsCodeAndMovesToExpiredState()
        {
            DateTime now=DateTime.UtcNow;
            var machine=new QuestPairingStateMachine();
            machine.BeginRequest(new FakeTransport
            {
                Result=PairingCodeResult.Ready("request-2","654321",now.AddSeconds(3))
            },now);

            machine.Tick(now.AddSeconds(4));

            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.Expired));
            Assert.That(machine.Snapshot(now.AddSeconds(4)).PairingCode,Is.Empty);
            Assert.That(machine.Snapshot(now.AddSeconds(4)).RequestId,Is.Empty);
        }

        [Test]
        public void CancellationNotifiesTransportAndReturnsToDisconnected()
        {
            DateTime now=DateTime.UtcNow;
            var transport=new FakeTransport
            {
                Result=PairingCodeResult.Ready("request-3","111222",now.AddMinutes(1))
            };
            var machine=new QuestPairingStateMachine();
            machine.BeginRequest(transport,now);

            machine.Cancel(transport,now);

            Assert.That(transport.CancelledRequestId,Is.EqualTo("request-3"));
            Assert.That(machine.State,Is.EqualTo(QuestPairingUiState.Disconnected));
        }

        [TestCase("")]
        [TestCase("12345")]
        [TestCase("1234567")]
        [TestCase("12A456")]
        public void PairingCodeValidationRejectsMalformedValues(string value)
            =>Assert.That(QuestPairingStateMachine.IsSixDigitCode(value),Is.False);

        [Test]
        public void AccountLabelIsBoundedAndControlCharactersAreRemoved()
        {
            string result=QuestPairingStateMachine.SanitizeAccountLabel("  Alex\nAdmin\0  ");
            Assert.That(result,Is.EqualTo("AlexAdmin"));
            Assert.That(result.Length,Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void AccountConnectionRemainsOnFirstPaginatedMainMenuPage()
        {
            Assert.That(BrainstormMenuView.IsAccountConnectionOnMainPage(0),Is.False,
                "Account/device connection belongs on Home, not inside My Sessions.");
            Assert.That(BrainstormMenuView.IsAccountConnectionOnMainPage(1),Is.False);
            Assert.That(BrainstormMenuView.CalculateMainPageCount(0),Is.EqualTo(1));
        }

        [Test]
        public void PairingControllerHasNoMicrophoneOrRecordingDependencies()
        {
            string[] fieldTypes=typeof(QuestPairingController)
                .GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Select(field=>field.FieldType.FullName??field.FieldType.Name).ToArray();
            Assert.That(fieldTypes.Any(name=>name.Contains("Microphone")||name.Contains("Recording")
                ||name.Contains("ConversationalListening")),Is.False);
        }

        [Test]
        public void AuthoredSlimMenuCreatesPairingPanelInsideExistingCanvas()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuSlimUI.prefab");
            Assert.That(prefab,Is.Not.Null);
            GameObject instance=UnityEngine.Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                Assert.That(view,Is.Not.Null);
                typeof(BrainstormMenuView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic)
                    ?.Invoke(view,null);
                Transform pairing=instance.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item=>item.name=="Account and Device Connection Panel");
                Assert.That(pairing,Is.Not.Null);
                Assert.That(pairing.GetComponentInParent<Canvas>(),Is.SameAs(view.Canvas));
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
        }

        [Test]
        public void PairingStringsAreCompleteForEnglishSpanishAndFrench()
        {
            string[] keys={"pairing.account.title","pairing.unavailable.body","pairing.code.title",
                "pairing.waiting.title","pairing.confirm.title","pairing.paired.title",
                "pairing.expired.title","pairing.failed.title","pairing.unpair.title"};
            foreach(string key in keys)
            {
                Assert.That(ThinkingSpaceStringCatalog.TryGet(key,out ThinkingSpaceStringEntry entry),Is.True,key);
                Assert.That(entry.English,Is.Not.Empty,key+" English");
                Assert.That(entry.Spanish,Is.Not.Empty,key+" Spanish");
                Assert.That(entry.French,Is.Not.Empty,key+" French");
            }
        }
    }
}
