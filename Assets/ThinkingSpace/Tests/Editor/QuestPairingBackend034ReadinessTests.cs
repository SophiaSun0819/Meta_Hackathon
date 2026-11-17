using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ThinkingSpace.Pairing;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class QuestPairingBackend034ReadinessTests
    {
        private const string Backend034DocumentSha256 =
            "40df9d6581615a478af581352ca8dcf3c5cce23119fb33eb041279552b8d91ea";

        [Test]
        public void StaleContractRemainsFailClosedUntilBackend034IsImplemented()
        {
            Assert.That(JudgeAccountSyncContract.PinnedDocumentSha256,
                Is.Not.EqualTo(Backend034DocumentSha256),
                "When the Backend-034 hash is adopted, replace this audit guard with full golden-vector conformance tests.");

            var unavailable = new UnavailableQuestPairingTransport();
            Assert.That(unavailable.IsAvailable, Is.False);
            Assert.That(typeof(QuestPairingController)
                .GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
        }

        [Test]
        public void ReviewedVaultsRemainExplicitAndLiveTransportIsNotControllerDefault()
        {
            Type[] implementations = typeof(IQuestCredentialVault).Assembly.GetTypes()
                .Where(type => typeof(IQuestCredentialVault).IsAssignableFrom(type)
                    && !type.IsInterface && !type.IsAbstract)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { typeof(InMemoryQuestCredentialVault), typeof(AndroidQuestCredentialVault) }, implementations);
            Assert.That(typeof(QuestPairingController).GetConstructors()
                .Any(constructor => constructor.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(Backend034PairingTransport))), Is.False,
                "The live transport must not become an implicit runtime default.");
        }

        [Test]
        public void LocalTransportIsExplicitlySimulationOnly()
        {
            using (var transport = new LocalQuestPairingTransport())
            {
                Assert.That(transport.IsLocalSimulation, Is.True);
                Assert.That(transport.CredentialVault,
                    Is.TypeOf<InMemoryQuestCredentialVault>());
            }
        }
    }
}
