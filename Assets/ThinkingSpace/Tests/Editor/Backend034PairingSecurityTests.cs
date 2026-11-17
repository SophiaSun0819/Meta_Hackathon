using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using ThinkingSpace.Pairing;
using UnityEditor;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class Backend034PairingSecurityTests
    {
        private const string PublicJwk =
            "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"saWCQEOz8sWvsojHTbxL0oNd3idJ1Oy7RoKgzT8SGKQ\",\"y\":\"Vziqubd-uPlvzF5pwnn6jOTKSYcs3UGbyRHFXi0RDk8\",\"ext\":true,\"key_ops\":[\"verify\"]}";
        private const string CanonicalPayload =
            "eyJjb250cmFjdElkZW50aWZpZXIiOiJ0aGlua2luZy1zcGFjZS1hY2NvdW50LXN5bmMiLCJjb250cmFjdFZlcnNpb24iOjEsInB1cnBvc2UiOiJQQUlSSU5HX0RFVklDRV9DT05GSVJNQVRJT04iLCJjaGFsbGVuZ2VJZCI6IjEwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDAwMSIsInB1YmxpY0tleVRodW1icHJpbnQiOiJ6WnN4WElXR2RMaVRIdldzTkNKR09UZklyUzBMYWV0dnBhcnpIU0hmWmJrIiwiY29uZmlybWF0aW9uTm9uY2UiOiJDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDIiwiYWNjb3VudElkIjoiMzAwMDAwMDAtMDAwMC00MDAwLTgwMDAtMDAwMDAwMDAwMDAxIiwiZXhwaXJlc0F0IjoiMjAzMC0wMS0wMVQwMDowNTowMC4wMDBaIn0";
        private const string P1363Signature =
            "Z2d6VpzQqygYEL_XN8cKcPzxGhDEKXPB4CYqDy-5wlMXAkW2x16-Axnd7oL8JaIr6grUaKa6xbMHcUMEWJe6fQ";
        private const string DerSignature =
            "MEQCIGdnelac0KsoGBC_1zfHCnD88RoQxClzweAmKg8vucJTAiAXAkW2x16-Axnd7oL8JaIr6grUaKa6xbMHcUMEWJe6fQ";

        [Test]
        public void StrictCapabilityParserAcceptsOnlyBackend034Manifest()
        {
            string valid = "{\"contractIdentifier\":\"thinking-space-account-sync\","
                + "\"pairingContractVersion\":1,\"syncContractVersion\":2,"
                + "\"portableSessionFormat\":{\"identifier\":\"thinking-space-session\",\"version\":1},"
                + "\"pairingTtlSeconds\":300,\"pollAfterMs\":1000,"
                + "\"proof\":\"P-256/SHA-256/P1363\",\"syncMutations\":[\"UPSERT\"],"
                + "\"maxPushMutations\":50,\"maxPullEvents\":200,\"maxRequestBytes\":1048576,\"enabled\":true}";

            Assert.That(Backend034CapabilityParser.TryParseCompatible(valid, out AccountSyncCapabilityManifest parsed), Is.True);
            Assert.That(parsed.SyncContractVersion, Is.EqualTo(2));
            Assert.That(Backend034CapabilityParser.ContractDocumentSha256,
                Is.EqualTo("40df9d6581615a478af581352ca8dcf3c5cce23119fb33eb041279552b8d91ea"));
            Assert.That(Backend034CapabilityParser.TryParseCompatible(
                valid.Replace("\"enabled\":true", "\"enabled\":false"), out _), Is.False);
            Assert.That(Backend034CapabilityParser.TryParseCompatible(
                valid.Replace("\"enabled\":true", "\"unexpected\":1,\"enabled\":true"), out _), Is.False);
            Assert.That(Backend034CapabilityParser.TryParseCompatible(
                valid.Replace("P-256/SHA-256/P1363", "ES256"), out _), Is.False);
        }

        [Test]
        public void CanonicalPairingProofMatchesAuthoritativeGoldenVector()
        {
            var payload = new PairingDeviceProofPayloadV1
            {
                ChallengeId = "10000000-0000-4000-8000-000000000001",
                PublicKeyThumbprint = "zZsxXIWGdLiTHvWsNCJGOTfIrS0LaetvparzHSHfZbk",
                ConfirmationNonce = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
                AccountId = "30000000-0000-4000-8000-000000000001",
                ExpiresAt = "2030-01-01T00:05:00.000Z"
            };

            byte[] canonical = PairingProofCryptography.CanonicalDeviceProof(payload);
            Assert.That(PairingProofCryptography.Base64UrlEncode(canonical), Is.EqualTo(CanonicalPayload));
            Assert.That(Encoding.UTF8.GetString(canonical), Does.StartWith("{\"contractIdentifier\""));
        }

        [Test]
        public void JwkThumbprintMatchesAuthoritativeGoldenVector()
            => Assert.That(PairingProofCryptography.PublicKeyThumbprint(PublicJwk),
                Is.EqualTo("zZsxXIWGdLiTHvWsNCJGOTfIrS0LaetvparzHSHfZbk"));

        [Test]
        public void DerAndP1363ConversionsMatchAuthoritativeGoldenVector()
        {
            byte[] raw = PairingProofCryptography.Base64UrlDecode(P1363Signature);
            byte[] der = PairingProofCryptography.Base64UrlDecode(DerSignature);
            Assert.That(PairingProofCryptography.DerToP1363(der), Is.EqualTo(raw));
            Assert.That(PairingProofCryptography.P1363ToDer(raw), Is.EqualTo(der));
            Assert.That(raw.Length, Is.EqualTo(64));
        }

        [Test]
        public void CryptoValidationFailsClosedForMalformedInputs()
        {
            Assert.Throws<ArgumentException>(() => PairingProofCryptography.DerToP1363(new byte[] { 0x30, 0x00 }));
            Assert.Throws<ArgumentException>(() => PairingProofCryptography.P1363ToDer(new byte[64]));
            byte[] orderOrLarger = new byte[64];
            byte[] order =
            {
                0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xbc, 0xe6, 0xfa, 0xad, 0xa7, 0x17, 0x9e, 0x84,
                0xf3, 0xb9, 0xca, 0xc2, 0xfc, 0x63, 0x25, 0x51
            };
            Buffer.BlockCopy(order, 0, orderOrLarger, 0, 32);
            orderOrLarger[63] = 1;
            Assert.Throws<ArgumentException>(() => PairingProofCryptography.P1363ToDer(orderOrLarger));
            Assert.Throws<ArgumentException>(() => PairingProofCryptography.PublicKeyThumbprint(
                PublicJwk.Replace("saWCQEOz8sWvsojHTbxL0oNd3idJ1Oy7RoKgzT8SGKQ", "AA")));
        }

        [Test]
        public void AndroidBridgeIsUnavailableInEditorAndJavaPluginIsAndroidOnly()
        {
            var bridge = new AndroidQuestP256KeyStore();
            Assert.That(bridge.IsSupported, Is.False);
            Assert.Throws<PlatformNotSupportedException>(() => bridge.EnsureKey("thinking-space-device-v1"));
            Assert.Throws<ArgumentException>(() => bridge.EnsureKey("invalid alias with spaces"));

            const string pluginPath =
                "Assets/Plugins/Android/com.meta_hackathon.thinkingspace.security/QuestP256KeyStore.java";
            PluginImporter importer = AssetImporter.GetAtPath(pluginPath) as PluginImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.Android), Is.True);
            Assert.That(importer.GetCompatibleWithEditor(), Is.False);

            string source = File.ReadAllText(pluginPath);
            StringAssert.Contains("AndroidKeyStore", source);
            StringAssert.Contains("secp256r1", source);
            StringAssert.Contains("SHA256withECDSA", source);
            StringAssert.Contains("privateKey.getEncoded()", source,
                "Diagnostics must test the AndroidKeyStore non-exportability contract.");
            StringAssert.Contains("Arrays.fill(encoded, (byte)0)", source,
                "Any unexpected exportable bytes must be cleared immediately.");
            StringAssert.Contains("KeyInfo", source);
            StringAssert.Contains("getSecurityLevel()", source);
            StringAssert.DoesNotContain("Log.", source);
            StringAssert.DoesNotContain("System.out", source);
        }

        [Test]
        public void DiagnosticResultParserRejectsNonAndroidExecution()
        {
            var bridge = new AndroidQuestP256KeyStore();
            byte[] payload = PairingProofCryptography.Base64UrlDecode(CanonicalPayload);
            Assert.Throws<PlatformNotSupportedException>(() =>
                bridge.RunDiagnostics("thinking-space-device-diagnostic-v1", payload, true));
        }
    }
}
