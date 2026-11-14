using System;

namespace ThinkingSpace.Pairing
{
    public sealed class UnavailableQuestPairingTransport : IQuestPairingTransport
    {
        public bool IsAvailable => false;
        public string UnavailableMessageKey => "pairing.unavailable.body";

        public void RequestPairingCode(Action<PairingCodeResult> completed)
            => completed?.Invoke(PairingCodeResult.Failed(UnavailableMessageKey));

        public void CancelPairing(string requestId) { }
    }
}
