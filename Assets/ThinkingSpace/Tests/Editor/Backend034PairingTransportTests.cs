using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ThinkingSpace.Pairing;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class Backend034PairingTransportTests
    {
        private const string Manifest = "{\"contractIdentifier\":\"thinking-space-account-sync\",\"pairingContractVersion\":1,\"syncContractVersion\":2,\"portableSessionFormat\":{\"identifier\":\"thinking-space-session\",\"version\":1},\"pairingTtlSeconds\":300,\"pollAfterMs\":1000,\"proof\":\"P-256/SHA-256/P1363\",\"syncMutations\":[\"UPSERT\"],\"maxPushMutations\":50,\"maxPullEvents\":200,\"maxRequestBytes\":1048576,\"enabled\":true}";

        [Test]
        public void LiveTransportIsDisabledByDefaultAndPerformsNoHttp()
        {
            var http = new FakeHttp();
            var transport = NewTransport(http, new InMemoryQuestCredentialVault(), false);
            PairingCodeResult result = default;
            transport.RequestPairingCode(value => result = value);
            Assert.That(result.Success, Is.False);
            Assert.That(http.Requests, Is.Empty);
        }

        [Test]
        public void CapabilityNegotiationFailsClosedForUnknownOrDisabledFields()
        {
            Assert.That(Backend034PairingTransport.TryCapabilities(
                new Backend034HttpResponse(200, Manifest), out _), Is.True);
            Assert.That(Backend034PairingTransport.TryCapabilities(new Backend034HttpResponse(200,
                Manifest.Replace("\"enabled\":true", "\"enabled\":false")), out _), Is.False);
            Assert.That(Backend034PairingTransport.TryCapabilities(new Backend034HttpResponse(200,
                Manifest.Replace("\"enabled\":true", "\"extra\":1,\"enabled\":true")), out _), Is.False);
            Assert.That(Backend034PairingTransport.TryCapabilities(
                new Backend034HttpResponse(503, "{\"error\":\"LIVE_ACCOUNT_SYNC_DISABLED\"}"), out _), Is.False);
        }

        [Test]
        public void StartAndActivateUseExactContractAndStoreCredential()
        {
            var vault = new InMemoryQuestCredentialVault();
            var http = new FakeHttp(request => request.Path switch
            {
                "/api/device/capabilities" => new Backend034HttpResponse(200, Manifest),
                "/api/device/v1/pairing/start" => new Backend034HttpResponse(200,
                    "{\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"code\":\"123456\",\"pollingSecret\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\"}"),
                "/api/device/v1/pairing/display" => new Backend034HttpResponse(200,
                    "{\"contractVersion\":1,\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"label\":\"Test Account\",\"confirmationNonce\":\"CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC\"}"),
                "/api/device/v1/pairing/proof-payload" => new Backend034HttpResponse(200,
                    "{\"contractIdentifier\":\"thinking-space-account-sync\",\"contractVersion\":1,\"purpose\":\"PAIRING_DEVICE_CONFIRMATION\",\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"publicKeyThumbprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"confirmationNonce\":\"CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC\",\"accountId\":\"30000000-0000-4000-8000-000000000001\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\"}"),
                "/api/device/v1/pairing/confirm" => new Backend034HttpResponse(200,
                    "{\"contractVersion\":1,\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"state\":\"DEVICE_CONFIRMED\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\",\"serverTime\":\"2030-01-01T00:01:00.000Z\",\"pollAfterMs\":1000}"),
                "/api/device/v1/pairing/activate" => new Backend034HttpResponse(200,
                    "{\"deviceId\":\"20000000-0000-4000-8000-000000000001\",\"accountId\":\"30000000-0000-4000-8000-000000000001\",\"scope\":\"quest-session-sync\",\"credentialVersion\":1,\"token\":\"EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE\"}"),
                _ => new Backend034HttpResponse(404, "{\"error\":\"NOT_FOUND\"}")
            });
            var transport = NewTransport(http, vault, true);
            Assert.That(transport.IsAvailable,Is.True,"explicitly enabled transport must be able to negotiate capabilities");
            PairingCodeResult started = default;
            transport.RequestPairingCode(value => started = value);
            Assert.That(started.Success, Is.True);
            Assert.That(JObject.Parse(http.Requests[1].Body).Count, Is.EqualTo(3));
            Backend034PairingTransport.ConfirmationContext context = null;
            string confirmationError = "not-called";
            transport.RequestConfirmationContext((value, errorValue) => { context = value; confirmationError = errorValue; });
            Assert.That(confirmationError, Is.Null);
            Assert.That(context.SafeAccountLabel, Is.EqualTo("Test Account"));
            transport.Confirm(context.ProofPayload, true, value => confirmationError = value);
            Assert.That(confirmationError, Is.Null);
            string error = "not-called";
            transport.Activate(value => error = value);
            Assert.That(error, Is.Null);
            Assert.That(vault.TryLoad(out DeviceCredential stored), Is.True);
            Assert.That(transport.HasStoredCredential,Is.True);
            Assert.That(stored.Version, Is.EqualTo(1));
            Assert.That(http.Requests[5].Body,
                Is.EqualTo("{\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"pollingSecret\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}"));
            transport.DeleteStoredCredential();
            Assert.That(vault.HasCredential,Is.False);
        }

        [Test]
        public void CredentialRotationUsesCanonicalProofAndAtomicallyReplacesToken()
        {
            var vault = new InMemoryQuestCredentialVault();
            vault.Store(new DeviceCredential("20000000-0000-4000-8000-000000000001",
                "30000000-0000-4000-8000-000000000001", 1, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE"));
            var http = new FakeHttp(request =>
            {
                if (request.Path == "/api/device/capabilities") return new Backend034HttpResponse(200, Manifest);
                if (request.Path.EndsWith("/start")) return new Backend034HttpResponse(200,
                    "{\"challengeId\":\"10000000-0000-4000-8000-000000000001\",\"code\":\"123456\",\"pollingSecret\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\"}");
                if (request.Path.EndsWith("/refresh-start")) return new Backend034HttpResponse(200,
                    "{\"contractVersion\":1,\"refreshChallengeId\":\"60000000-0000-4000-8000-000000000001\",\"deviceId\":\"20000000-0000-4000-8000-000000000001\",\"credentialVersion\":1,\"nonce\":\"DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\"}");
                if (request.Path.EndsWith("/refresh"))
                {
                    JObject body = JObject.Parse(request.Body);
                    return new Backend034HttpResponse(200,
                        "{\"deviceId\":\"20000000-0000-4000-8000-000000000001\",\"accountId\":\"30000000-0000-4000-8000-000000000001\",\"scope\":\"quest-session-sync\",\"credentialVersion\":2,\"token\":"
                        + body["nextRefreshToken"].ToString(Newtonsoft.Json.Formatting.None) + "}");
                }
                return new Backend034HttpResponse(404, "{\"error\":\"NOT_FOUND\"}");
            });
            var transport = NewTransport(http, vault, true);
            transport.RequestPairingCode(_ => { });
            string error = "not-called";
            transport.RotateCredential(value => error = value);
            Assert.That(error, Is.Null);
            Assert.That(vault.TryLoad(out DeviceCredential rotated), Is.True);
            Assert.That(rotated.Version, Is.EqualTo(2));
            Assert.That(rotated.Token, Is.Not.EqualTo("EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE"));
            JObject refresh = JObject.Parse(http.Requests[3].Body);
            Assert.That(refresh.Count, Is.EqualTo(9));
            Assert.That((string)refresh["deviceProof"], Is.EqualTo("fixture-signature"));
        }

        [Test]
        public void CanonicalRefreshProofMatchesBackendFieldOrder()
        {
            var payload = new PairingRefreshProofPayloadV1
            {
                RefreshChallengeId = "60000000-0000-4000-8000-000000000001",
                DeviceId = "20000000-0000-4000-8000-000000000001",
                PublicKeyThumbprint = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                CredentialVersion = 1,
                Nonce = "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
                IdempotencyKey = "70000000-0000-4000-8000-000000000001",
                ExpiresAt = "2030-01-01T00:05:00.000Z"
            };
            string json = Encoding.UTF8.GetString(PairingProofCryptography.CanonicalRefreshProof(payload));
            Assert.That(json, Is.EqualTo("{\"contractIdentifier\":\"thinking-space-account-sync\",\"contractVersion\":1,\"purpose\":\"DEVICE_CREDENTIAL_REFRESH\",\"refreshChallengeId\":\"60000000-0000-4000-8000-000000000001\",\"deviceId\":\"20000000-0000-4000-8000-000000000001\",\"publicKeyThumbprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"credentialVersion\":1,\"nonce\":\"DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD\",\"idempotencyKey\":\"70000000-0000-4000-8000-000000000001\",\"expiresAt\":\"2030-01-01T00:05:00.000Z\"}"));
        }

        [Test]
        public void RetryBackoffHonorsServerMinimumAndIsBounded()
        {
            Assert.That(Backend034PairingTransport.Backoff(0, 5000), Is.EqualTo(5000));
            Assert.That(Backend034PairingTransport.Backoff(20, 0), Is.LessThan(35000));
        }

        [Test]
        public void HttpOriginRequiresHttpsExceptLoopbackAndRejectsEmbeddedCredentials()
        {
            Assert.That(Backend034UnityHttpExecutor.ValidateOrigin("https://example.test").Scheme, Is.EqualTo("https"));
            Assert.That(Backend034UnityHttpExecutor.ValidateOrigin("http://127.0.0.1:8080").IsLoopback, Is.True);
            Assert.Throws<ArgumentException>(() => Backend034UnityHttpExecutor.ValidateOrigin("http://example.test"));
            Assert.Throws<ArgumentException>(() => Backend034UnityHttpExecutor.ValidateOrigin("https://user:secret@example.test"));
        }

        private static Backend034PairingTransport NewTransport(FakeHttp http, IQuestCredentialVault vault, bool enabled)
            => new Backend034PairingTransport(http, vault, new FakeSigner(), "test-key",
                "quest-install-id", enabled);

        private sealed class FakeHttp : IBackend034HttpExecutor
        {
            private readonly Func<Backend034HttpRequest, Backend034HttpResponse> responder;
            public readonly List<Backend034HttpRequest> Requests = new List<Backend034HttpRequest>();
            public FakeHttp(Func<Backend034HttpRequest, Backend034HttpResponse> response = null)
                => responder = response ?? (_ => new Backend034HttpResponse(503, "{\"error\":\"LIVE_ACCOUNT_SYNC_DISABLED\"}"));
            public void Send(Backend034HttpRequest request, Action<Backend034HttpResponse> completed)
            { Requests.Add(request); completed(responder(request)); }
        }

        private sealed class FakeSigner : IBackend034PairingSigner
        {
            public void EnsureKey(string alias) { }
            public string GetPublicJwk(string alias) => "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"y\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\"}";
            public string GetPublicKeyThumbprint(string alias) => "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
            public string SignCanonicalPayload(string alias, byte[] canonicalPayload) => "fixture-signature";
        }
    }
}
