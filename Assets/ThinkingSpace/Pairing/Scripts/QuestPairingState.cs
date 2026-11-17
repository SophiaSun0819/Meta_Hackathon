using System;

namespace ThinkingSpace.Pairing
{
    public enum QuestPairingUiState
    {
        UnavailableOffline,
        Disconnected,
        RequestingCode,
        CodeReady,
        WaitingForWebsite,
        AwaitingQuestConfirmation,
        Paired,
        Expired,
        Failed,
        UnpairConfirmation
    }

    public readonly struct QuestPairingSnapshot
    {
        public QuestPairingUiState State { get; }
        public string PairingCode { get; }
        public string RequestId { get; }
        public string AccountLabel { get; }
        public string MessageKey { get; }
        public int RemainingSeconds { get; }

        public QuestPairingSnapshot(QuestPairingUiState state, string pairingCode,
            string requestId, string accountLabel, string messageKey, int remainingSeconds)
        {
            State = state;
            PairingCode = pairingCode ?? string.Empty;
            RequestId = requestId ?? string.Empty;
            AccountLabel = accountLabel ?? string.Empty;
            MessageKey = messageKey ?? string.Empty;
            RemainingSeconds = Math.Max(0, remainingSeconds);
        }
    }

    public readonly struct PairingCodeResult
    {
        public bool Success { get; }
        public string RequestId { get; }
        public string PairingCode { get; }
        public DateTime ExpiresUtc { get; }
        public string ErrorKey { get; }

        private PairingCodeResult(bool success, string requestId, string pairingCode,
            DateTime expiresUtc, string errorKey)
        {
            Success = success;
            RequestId = requestId ?? string.Empty;
            PairingCode = pairingCode ?? string.Empty;
            ExpiresUtc = expiresUtc;
            ErrorKey = errorKey ?? string.Empty;
        }

        public static PairingCodeResult Ready(string requestId, string pairingCode, DateTime expiresUtc)
            => new PairingCodeResult(true, requestId, pairingCode, expiresUtc, string.Empty);

        public static PairingCodeResult Failed(string errorKey)
            => new PairingCodeResult(false, string.Empty, string.Empty, default, errorKey);
    }

    public sealed class QuestPairingStateMachine
    {
        private QuestPairingUiState state = QuestPairingUiState.Disconnected;
        private string requestId = string.Empty;
        private string pairingCode = string.Empty;
        private string accountLabel = string.Empty;
        private string messageKey = string.Empty;
        private DateTime expiresUtc;
        private int lastRemainingSeconds = -1;

        public event Action<QuestPairingSnapshot> Changed;
        public QuestPairingUiState State => state;
        public QuestPairingSnapshot Snapshot(DateTime utcNow)
            => new QuestPairingSnapshot(state, pairingCode, requestId, accountLabel,
                messageKey, RemainingSeconds(utcNow));

        public void Open(bool paired, DateTime utcNow)
        {
            if (paired) SetState(QuestPairingUiState.Paired, utcNow);
            else if (state == QuestPairingUiState.Paired || state == QuestPairingUiState.UnpairConfirmation)
                Reset(utcNow);
            else Publish(utcNow);
        }

        public void BeginRequest(IQuestPairingTransport transport, DateTime utcNow)
        {
            ClearTransient();
            if (transport == null || !transport.IsAvailable)
            {
                messageKey = transport?.UnavailableMessageKey ?? "pairing.unavailable.body";
                SetState(QuestPairingUiState.UnavailableOffline, utcNow);
                return;
            }

            SetState(QuestPairingUiState.RequestingCode, utcNow);
            transport.RequestPairingCode(result => ApplyCodeResult(result, DateTime.UtcNow));
        }

        public void ApplyCodeResult(PairingCodeResult result, DateTime utcNow)
        {
            if (state != QuestPairingUiState.RequestingCode) return;
            if (!result.Success || !IsSixDigitCode(result.PairingCode)
                || string.IsNullOrEmpty(result.RequestId) || result.ExpiresUtc <= utcNow)
            {
                messageKey = string.IsNullOrEmpty(result.ErrorKey)
                    ? "pairing.failed.body" : result.ErrorKey;
                SetState(QuestPairingUiState.Failed, utcNow);
                return;
            }

            requestId = result.RequestId;
            pairingCode = result.PairingCode;
            expiresUtc = result.ExpiresUtc;
            lastRemainingSeconds = -1;
            SetState(QuestPairingUiState.CodeReady, utcNow);
        }

        public void BeginWaiting(DateTime utcNow)
        {
            if (state == QuestPairingUiState.CodeReady)
                SetState(QuestPairingUiState.WaitingForWebsite, utcNow);
        }

        public void ReceiveWebsiteConfirmation(string safeAccountLabel, DateTime utcNow)
        {
            if (state != QuestPairingUiState.WaitingForWebsite) return;
            accountLabel = SanitizeAccountLabel(safeAccountLabel);
            SetState(QuestPairingUiState.AwaitingQuestConfirmation, utcNow);
        }

        public void ConfirmAccount(bool accepted, DateTime utcNow)
        {
            if (state != QuestPairingUiState.AwaitingQuestConfirmation) return;
            if (!accepted) Reset(utcNow);
            else
            {
                ClearTransient();
                SetState(QuestPairingUiState.Paired, utcNow);
            }
        }

        public void RequestUnpair(DateTime utcNow)
        {
            if (state == QuestPairingUiState.Paired)
                SetState(QuestPairingUiState.UnpairConfirmation, utcNow);
        }

        public void ConfirmUnpair(bool confirmed, DateTime utcNow)
        {
            if (state != QuestPairingUiState.UnpairConfirmation) return;
            if (confirmed) Reset(utcNow);
            else SetState(QuestPairingUiState.Paired, utcNow);
        }

        public void Cancel(IQuestPairingTransport transport, DateTime utcNow)
        {
            if (!string.IsNullOrEmpty(requestId)) transport?.CancelPairing(requestId);
            Reset(utcNow);
        }

        public void Fail(string safeMessageKey, DateTime utcNow)
        {
            messageKey = string.IsNullOrEmpty(safeMessageKey)
                ? "pairing.failed.body" : safeMessageKey;
            SetState(QuestPairingUiState.Failed, utcNow);
        }

        public void Expire(DateTime utcNow)
        {
            ClearTransient();
            SetState(QuestPairingUiState.Expired, utcNow);
        }

        public void Tick(DateTime utcNow)
        {
            if (!HasExpiringRequest()) return;
            int remaining = RemainingSeconds(utcNow);
            if (remaining <= 0)
            {
                ClearTransient();
                SetState(QuestPairingUiState.Expired, utcNow);
                return;
            }
            if (remaining != lastRemainingSeconds) Publish(utcNow);
        }

        private void Reset(DateTime utcNow)
        {
            ClearTransient();
            SetState(QuestPairingUiState.Disconnected, utcNow);
        }

        private void ClearTransient()
        {
            requestId = pairingCode = accountLabel = messageKey = string.Empty;
            expiresUtc = default;
            lastRemainingSeconds = -1;
        }

        private int RemainingSeconds(DateTime utcNow)
        {
            if (!HasExpiringRequest()) return 0;
            return Math.Max(0, (int)Math.Ceiling((expiresUtc - utcNow).TotalSeconds));
        }

        private bool HasExpiringRequest()
            => expiresUtc != default && (state == QuestPairingUiState.CodeReady
                || state == QuestPairingUiState.WaitingForWebsite
                || state == QuestPairingUiState.AwaitingQuestConfirmation);

        private void SetState(QuestPairingUiState value, DateTime utcNow)
        {
            state = value;
            Publish(utcNow);
        }

        private void Publish(DateTime utcNow)
        {
            lastRemainingSeconds = RemainingSeconds(utcNow);
            Changed?.Invoke(Snapshot(utcNow));
        }

        public static bool IsSixDigitCode(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 6) return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return true;
        }

        public static string SanitizeAccountLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            char[] safe = new char[Math.Min(64, trimmed.Length)];
            int count = 0;
            for (int i = 0; i < trimmed.Length && count < safe.Length; i++)
                if (!char.IsControl(trimmed[i])) safe[count++] = trimmed[i];
            return new string(safe, 0, count);
        }
    }
}
