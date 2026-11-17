using System.Collections;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThinkingSpace.Tests.PlayMode
{
    public sealed class AndroidQuestP256KeyStoreTests
    {
        private const string DiagnosticAlias = "thinking-space-device-diagnostic-v1";
        private const string CanonicalPayload =
            "eyJjb250cmFjdElkZW50aWZpZXIiOiJ0aGlua2luZy1zcGFjZS1hY2NvdW50LXN5bmMiLCJjb250cmFjdFZlcnNpb24iOjEsInB1cnBvc2UiOiJQQUlSSU5HX0RFVklDRV9DT05GSVJNQVRJT04iLCJjaGFsbGVuZ2VJZCI6IjEwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDAwMSIsInB1YmxpY0tleVRodW1icHJpbnQiOiJ6WnN4WElXR2RMaVRIdldzTkNKR09UZklyUzBMYWV0dnBhcnpIU0hmWmJrIiwiY29uZmlybWF0aW9uTm9uY2UiOiJDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDQ0NDIiwiYWNjb3VudElkIjoiMzAwMDAwMDAtMDAwMC00MDAwLTgwMDAtMDAwMDAwMDAwMDAxIiwiZXhwaXJlc0F0IjoiMjAzMC0wMS0wMVQwMDowNTowMC4wMDBaIn0";

        [UnityTest]
        [UnityPlatform(RuntimePlatform.Android)]
        public IEnumerator DedicatedDiagnosticAliasPersistsSignsReportsSecurityAndDeletes()
        {
            Type bridgeType = Type.GetType(
                "ThinkingSpace.Pairing.AndroidQuestP256KeyStore, Assembly-CSharp", true);
            object bridge = Activator.CreateInstance(bridgeType);
            Assert.That((bool)bridgeType.GetProperty("IsSupported").GetValue(bridge), Is.True);
            byte[] payload = DecodeBase64Url(CanonicalPayload);

            MethodInfo run = bridgeType.GetMethod("RunDiagnostics");
            MethodInfo contains = bridgeType.GetMethod("ContainsKey");
            bool existedBefore = (bool)contains.Invoke(bridge, new object[] { DiagnosticAlias });
            object result = run.Invoke(bridge,
                new object[] { DiagnosticAlias, payload, existedBefore });
            AssertField(result, "keyExistedBefore", existedBefore);
            AssertField(result, "keyExistsAfterGeneration", true);
            AssertField(result, "signatureVerified", true);
            AssertField(result, "signatureLength", 64);
            AssertField(result, "privateKeyExportable", false);
            string securityLevel = Field<string>(result, "securityLevel");
            bool hardwareBacked = Field<bool>(result, "insideSecureHardware");
            Assert.That(securityLevel, Is.Not.Empty);

            if (!existedBefore)
            {
                AssertField(result, "deleted", false);
                Assert.That((bool)contains.Invoke(bridge, new object[] { DiagnosticAlias }), Is.True);
                Debug.Log("UNITY037_KEYSTORE_DIAGNOSTIC PHASE1_PASS keyCreated=true "
                    + "signatureVerified=true privateKeyExportable=false securityLevel="
                    + securityLevel + " insideSecureHardware=" + hardwareBacked);
            }
            else
            {
                AssertField(result, "deleted", true);
                Assert.That((bool)contains.Invoke(bridge, new object[] { DiagnosticAlias }), Is.False);
                Debug.Log("UNITY037_KEYSTORE_DIAGNOSTIC PHASE2_PASS persistedAcrossLaunch=true "
                    + "signatureVerified=true privateKeyExportable=false deleted=true securityLevel="
                    + securityLevel + " insideSecureHardware=" + hardwareBacked);
            }

            yield return null;
        }

        private static void AssertField<T>(object target, string name, T expected)
            => Assert.That(Field<T>(target, name), Is.EqualTo(expected), name);

        private static T Field<T>(object target, string name)
            => (T)target.GetType().GetField(name).GetValue(target);

        private static byte[] DecodeBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }
}
