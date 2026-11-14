using System;

namespace ThinkingSpace.Pairing
{
    public interface IQuestPairingTransport
    {
        bool IsAvailable { get; }
        string UnavailableMessageKey { get; }
        void RequestPairingCode(Action<PairingCodeResult> completed);
        void CancelPairing(string requestId);
    }
}
