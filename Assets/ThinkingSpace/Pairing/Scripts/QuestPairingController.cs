using System;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Pairing
{
    [DisallowMultipleComponent]
    public sealed class QuestPairingController : MonoBehaviour
    {
        private readonly QuestPairingStateMachine stateMachine = new QuestPairingStateMachine();
        private IQuestPairingTransport transport;
        private BrainstormMenuView view;
        private Action returnToMain;
        private bool showing;
        private DateTime nextLocalPollUtc;

        public QuestPairingUiState State => stateMachine.State;

        public void Initialize(BrainstormMenuView menuView, Action backToMain,
            IQuestPairingTransport pairingTransport = null)
        {
            if (view != null) stateMachine.Changed -= HandleChanged;
            view = menuView;
            returnToMain = backToMain;
            transport = pairingTransport ?? new UnavailableQuestPairingTransport();
            stateMachine.Changed += HandleChanged;
        }

        public void Open()
        {
            showing = true;
            stateMachine.Open(false, DateTime.UtcNow);
        }

        public void Hide() => showing = false;

        private void Update()
        {
            if (!showing) return;
            DateTime now=DateTime.UtcNow;
            stateMachine.Tick(now);
            if(stateMachine.State==QuestPairingUiState.WaitingForWebsite
                &&transport is LocalQuestPairingTransport local&&now>=nextLocalPollUtc)
            {
                nextLocalPollUtc=now.AddMilliseconds(JudgeAccountSyncContract.PollAfterMilliseconds);
                QuestPairingSnapshot snapshot=stateMachine.Snapshot(now);
                if(local.TryGetStatus(snapshot.RequestId,now,out LocalPairingStatus status))
                {
                    if(status.Status==PairingChallengeStatus.CLAIMED)
                        stateMachine.ReceiveWebsiteConfirmation(status.SafeAccountLabel,now);
                    else if(status.Status==PairingChallengeStatus.EXPIRED)
                        stateMachine.Tick(now.AddSeconds(JudgeAccountSyncContract.PairingTtlSeconds));
                    else if(status.Status==PairingChallengeStatus.CANCELLED)
                        stateMachine.Fail("pairing.failed.body",now);
                }
            }
        }

        private void OnDestroy() => stateMachine.Changed -= HandleChanged;

        private void HandleChanged(QuestPairingSnapshot snapshot)
        {
            if (!showing || view == null) return;
            view.ShowPairing(snapshot, Previous, Next, Auxiliary);
        }

        private void Previous()
        {
            if (stateMachine.State == QuestPairingUiState.UnpairConfirmation)
            {
                stateMachine.ConfirmUnpair(false, DateTime.UtcNow);
                return;
            }
            stateMachine.Cancel(transport, DateTime.UtcNow);
            showing = false;
            returnToMain?.Invoke();
        }

        private void Next()
        {
            DateTime now = DateTime.UtcNow;
            switch (stateMachine.State)
            {
                case QuestPairingUiState.Disconnected:
                case QuestPairingUiState.UnavailableOffline:
                case QuestPairingUiState.Expired:
                case QuestPairingUiState.Failed:
                    stateMachine.BeginRequest(transport, now);
                    break;
                case QuestPairingUiState.CodeReady:
                    stateMachine.BeginWaiting(now);
                    break;
                case QuestPairingUiState.AwaitingQuestConfirmation:
                    if(transport is LocalQuestPairingTransport local)
                    {
                        string requestId=stateMachine.Snapshot(now).RequestId;
                        if(local.ConfirmAndActivate(requestId,true))stateMachine.ConfirmAccount(true,now);
                        else stateMachine.Fail("pairing.failed.body",now);
                    }
                    else stateMachine.ConfirmAccount(true, now);
                    break;
                case QuestPairingUiState.Paired:
                    showing = false;
                    returnToMain?.Invoke();
                    break;
                case QuestPairingUiState.UnpairConfirmation:
                    if(transport is LocalQuestPairingTransport localUnpair)localUnpair.Unpair();
                    stateMachine.ConfirmUnpair(true, now);
                    break;
            }
        }

        private void Auxiliary()
        {
            DateTime now = DateTime.UtcNow;
            if (stateMachine.State == QuestPairingUiState.Paired)
                stateMachine.RequestUnpair(now);
            else if (stateMachine.State == QuestPairingUiState.CodeReady)
            {
                stateMachine.Cancel(transport, now);
                stateMachine.BeginRequest(transport, now);
            }
            else if (stateMachine.State == QuestPairingUiState.RequestingCode
                || stateMachine.State == QuestPairingUiState.WaitingForWebsite
                || stateMachine.State == QuestPairingUiState.AwaitingQuestConfirmation)
                stateMachine.Cancel(transport, now);
            else
                stateMachine.BeginRequest(transport, now);
        }
    }
}
