using NUnit.Framework;
using ThinkingSpace.Companion;

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
    }
}
