using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ThinkingSpace.Pairing
{
    public interface IQuestCredentialVault
    {
        bool HasCredential { get; }
        void Store(DeviceCredential credential);
        bool TryLoad(out DeviceCredential credential);
        void Delete();
    }

    public readonly struct DeviceCredential
    {
        public string DeviceId { get; }
        public string AccountId { get; }
        public int Version { get; }
        public string Token { get; }
        public DeviceCredential(string deviceId, string accountId, int version, string token)
        { DeviceId=deviceId??string.Empty;AccountId=accountId??string.Empty;Version=version;Token=token??string.Empty; }
    }

    // Test/development-only vault. Production must replace this with Android Keystore-backed encrypted storage.
    public sealed class InMemoryQuestCredentialVault : IQuestCredentialVault, IDisposable
    {
        private DeviceCredential credential;
        public bool HasCredential { get; private set; }
        public void Store(DeviceCredential value) { Delete(); credential=value;HasCredential=true; }
        public bool TryLoad(out DeviceCredential value) { value=credential;return HasCredential; }
        public void Delete() { credential=new DeviceCredential();HasCredential=false; }
        public void Dispose()=>Delete();
    }

    public readonly struct LocalPairingStatus
    {
        public PairingChallengeStatus Status { get; }
        public string SafeAccountLabel { get; }
        public LocalPairingStatus(PairingChallengeStatus status,string label)
        {Status=status;SafeAccountLabel=label??string.Empty;}
    }

    // Explicit opt-in simulator. It performs no network I/O and is never selected by the runtime controller by default.
    public sealed class LocalQuestPairingTransport : IQuestPairingTransport, IDisposable
    {
        private sealed class Challenge
        {
            public string Id,Code,AccountLabel;
            public DateTime ExpiresUtc;
            public PairingChallengeStatus Status;
        }
        private readonly Dictionary<string,Challenge> challenges=new Dictionary<string,Challenge>();
        private readonly IQuestCredentialVault vault;
        public bool IsAvailable=>true;
        public bool IsLocalSimulation=>true;
        public string UnavailableMessageKey=>string.Empty;
        public IQuestCredentialVault CredentialVault=>vault;

        public LocalQuestPairingTransport(IQuestCredentialVault credentialVault=null)
            =>vault=credentialVault??new InMemoryQuestCredentialVault();

        public void RequestPairingCode(Action<PairingCodeResult> completed)
        {
            string id=Guid.NewGuid().ToString("D");
            string code=CreateSixDigitCode();
            var challenge=new Challenge{Id=id,Code=code,ExpiresUtc=DateTime.UtcNow.AddSeconds(
                JudgeAccountSyncContract.PairingTtlSeconds),Status=PairingChallengeStatus.PENDING};
            challenges[id]=challenge;
            completed?.Invoke(PairingCodeResult.Ready(id,code,challenge.ExpiresUtc));
        }

        public void CancelPairing(string requestId)
        {if(challenges.TryGetValue(requestId??string.Empty,out Challenge value))value.Status=PairingChallengeStatus.CANCELLED;}

        public bool TryGetStatus(string requestId,DateTime utcNow,out LocalPairingStatus status)
        {
            if(!challenges.TryGetValue(requestId??string.Empty,out Challenge value))
            {status=default;return false;}
            if(value.ExpiresUtc<=utcNow&&value.Status!=PairingChallengeStatus.ACTIVE
                &&value.Status!=PairingChallengeStatus.CANCELLED)value.Status=PairingChallengeStatus.EXPIRED;
            status=new LocalPairingStatus(value.Status,value.AccountLabel);return true;
        }

        public bool SimulateWebsiteClaim(string code,string safeAccountLabel)
        {
            foreach(Challenge value in challenges.Values)
                if(value.Status==PairingChallengeStatus.PENDING&&value.ExpiresUtc>DateTime.UtcNow
                    &&string.Equals(value.Code,code,StringComparison.Ordinal))
                {value.Status=PairingChallengeStatus.CLAIMED;value.AccountLabel=QuestPairingStateMachine.SanitizeAccountLabel(safeAccountLabel);return true;}
            return false;
        }

        public bool ConfirmAndActivate(string requestId,bool confirmed)
        {
            if(!challenges.TryGetValue(requestId??string.Empty,out Challenge value)
                ||value.Status!=PairingChallengeStatus.CLAIMED||value.ExpiresUtc<=DateTime.UtcNow)return false;
            if(!confirmed){value.Status=PairingChallengeStatus.CANCELLED;return true;}
            value.Status=PairingChallengeStatus.DEVICE_CONFIRMED;
            string deviceId=Guid.NewGuid().ToString("D");
            string accountId=Guid.NewGuid().ToString("D");
            vault.Store(new DeviceCredential(deviceId,accountId,1,CreateOpaqueToken()));
            value.Status=PairingChallengeStatus.ACTIVE;
            return true;
        }

        public void Unpair()
        {
            vault.Delete();
            foreach(Challenge value in challenges.Values)
                if(value.Status==PairingChallengeStatus.ACTIVE)value.Status=PairingChallengeStatus.CANCELLED;
        }

        public void Dispose(){Unpair();challenges.Clear();if(vault is IDisposable disposable)disposable.Dispose();}

        private static string CreateSixDigitCode()
        {byte[] bytes=new byte[4];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);uint value=BitConverter.ToUInt32(bytes,0)%1000000;return value.ToString("D6");}
        private static string CreateOpaqueToken()
        {byte[] bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return Convert.ToBase64String(bytes);}
    }
}
