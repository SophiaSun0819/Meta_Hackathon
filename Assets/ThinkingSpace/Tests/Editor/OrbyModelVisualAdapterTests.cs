using NUnit.Framework;
using ThinkingSpace.Companion;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class OrbyModelVisualAdapterTests
    {
        [TestCase(OrbyConversationState.Idle, OrbyMaterialExpression.Idle)]
        [TestCase(OrbyConversationState.Paused, OrbyMaterialExpression.Idle)]
        [TestCase(OrbyConversationState.Listening, OrbyMaterialExpression.Listening)]
        [TestCase(OrbyConversationState.SpeechActive, OrbyMaterialExpression.Listening)]
        [TestCase(OrbyConversationState.Thinking, OrbyMaterialExpression.Thinking)]
        [TestCase(OrbyConversationState.Speaking, OrbyMaterialExpression.Speaking)]
        [TestCase(OrbyConversationState.Reviewing, OrbyMaterialExpression.Curious)]
        [TestCase(OrbyConversationState.Error, OrbyMaterialExpression.Error)]
        public void ConversationStateUsesExpectedMaterialExpression(
            OrbyConversationState state, OrbyMaterialExpression expected)
        {
            Assert.That(OrbyModelVisualAdapter.FromConversationState(state), Is.EqualTo(expected));
        }

        [Test]
        public void MouthHysteresisClosesOnlyBelowLowerThreshold()
        {
            Assert.That(BubbleMouthSwap.ShouldMouthOpen(false,0.064f,0.065f,0.028f),Is.False);
            Assert.That(BubbleMouthSwap.ShouldMouthOpen(false,0.065f,0.065f,0.028f),Is.True);
            Assert.That(BubbleMouthSwap.ShouldMouthOpen(true,0.04f,0.065f,0.028f),Is.True);
            Assert.That(BubbleMouthSwap.ShouldMouthOpen(true,0.02f,0.065f,0.028f),Is.False);
        }

        [Test]
        public void MouthSmoothingUsesFasterAttackAndNaturalRelease()
        {
            float attack=BubbleMouthSwap.SmoothLevel(0f,1f,0.055f,0.11f,0.05f);
            float release=BubbleMouthSwap.SmoothLevel(1f,0f,0.055f,0.11f,0.05f);
            Assert.That(attack,Is.GreaterThan(1f-release));
            Assert.That(attack,Is.InRange(0f,1f));
            Assert.That(release,Is.InRange(0f,1f));
        }

        [Test]
        public void MouthScaleRemainsSubtleInsteadOfStretchingExtremely()
        {
            Vector3 scale=BubbleMouthSwap.CalculateMouthScale(Vector3.one,1f,1f,
                0.82f,1.16f,0.94f,1.06f);
            Assert.That(scale.x,Is.EqualTo(1.06f).Within(0.0001f));
            Assert.That(scale.y,Is.EqualTo(1.16f).Within(0.0001f));
            Assert.That(scale.z,Is.EqualTo(1f));
        }
    }
}
