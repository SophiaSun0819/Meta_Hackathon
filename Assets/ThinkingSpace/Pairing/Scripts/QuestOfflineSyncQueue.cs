using System;
using System.Collections.Generic;

namespace ThinkingSpace.Pairing
{
    /// Account-bound in-memory queue. Persistence is deliberately deferred until the
    /// encrypted Android store has an approved schema; local sessions remain untouched.
    public sealed class QuestOfflineSyncQueue
    {
        private readonly string accountId;
        private readonly List<SessionSyncMutationV2> pending = new List<SessionSyncMutationV2>();
        private readonly HashSet<string> operationIds = new HashSet<string>(StringComparer.Ordinal);

        public QuestOfflineSyncQueue(string pairedAccountId)
        {
            if (!Guid.TryParse(pairedAccountId, out _)) throw new ArgumentException("Account UUID is required.");
            accountId = pairedAccountId;
        }

        public int Count => pending.Count;

        public void Enqueue(string currentAccountId, SessionSyncMutationV2 mutation)
        {
            RequireAccount(currentAccountId);
            if (mutation == null || !Guid.TryParse(mutation.OperationId, out _))
                throw new ArgumentException("A stable operation UUID is required.");
            if (operationIds.Add(mutation.OperationId)) pending.Add(mutation);
        }

        public IReadOnlyList<SessionSyncMutationV2> NextBatch(string currentAccountId)
        {
            RequireAccount(currentAccountId);
            int count = Math.Min(JudgeAccountSyncContract.MaximumPushMutations, pending.Count);
            return pending.GetRange(0, count);
        }

        public void ApplyResult(string currentAccountId, string operationId, SessionMutationResultStatus result)
        {
            RequireAccount(currentAccountId);
            if (result == SessionMutationResultStatus.CONFLICT) return; // requires explicit user resolution
            for (int i = 0; i < pending.Count; i++)
                if (string.Equals(pending[i].OperationId, operationId, StringComparison.Ordinal))
                { pending.RemoveAt(i); operationIds.Remove(operationId); return; }
        }

        public void DiscardConflict(string currentAccountId, string operationId)
        {
            RequireAccount(currentAccountId);
            ApplyResult(currentAccountId, operationId, SessionMutationResultStatus.REJECTED);
        }

        private void RequireAccount(string value)
        {
            if (!string.Equals(accountId, value, StringComparison.Ordinal))
                throw new InvalidOperationException("SYNC_ACCOUNT_MISMATCH");
        }
    }
}
