using NUnit.Framework;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ThinkingSpaceUIIntegrationTests
    {
        [TestCase("Orby, switch the UI panels to French.","fr")]
        [TestCase("Change the interface to Spanish.","es")]
        [TestCase("Put the menu in English.","en")]
        [TestCase("Cambia la interfaz a francés.","fr")]
        public void RouterRecognizesExplicitLocalInterfaceCommands(string transcript,string expected)
        {
            LocalInterfaceLanguageCommand command=LocalInterfaceLanguageCommandRouter.Parse(transcript);
            Assert.That(command.Outcome,Is.EqualTo(LocalInterfaceLanguageCommandOutcome.Supported));
            Assert.That(command.LanguageCode,Is.EqualTo(expected));
        }

        [TestCase("Speak French")]
        [TestCase("Answer in Spanish")]
        [TestCase("Tell me about French menus")]
        public void RouterDoesNotCaptureConversationLanguageOrOrdinaryDiscussion(string transcript)
            =>Assert.That(LocalInterfaceLanguageCommandRouter.Parse(transcript).Outcome,
                Is.EqualTo(LocalInterfaceLanguageCommandOutcome.NotCommand));

        [Test]
        public void RouterDistinguishesUnsupportedAndAmbiguousInterfaceRequests()
        {
            Assert.That(LocalInterfaceLanguageCommandRouter.Parse("Change the interface to German").Outcome,
                Is.EqualTo(LocalInterfaceLanguageCommandOutcome.Unsupported));
            Assert.That(LocalInterfaceLanguageCommandRouter.Parse("Change the interface to French or Spanish").Outcome,
                Is.EqualTo(LocalInterfaceLanguageCommandOutcome.Ambiguous));
        }

        [Test]
        public void WorkflowRecognizesLocalCommandBeforeConversationRouting()
        {
            GameObject owner=new GameObject("Workflow");
            try
            {
                ThinkingSpaceAIWorkflowController workflow=owner.AddComponent<ThinkingSpaceAIWorkflowController>();
                Assert.That(workflow.CanHandleLocalInterfaceLanguageCommand("Switch the UI panels to French"),Is.True);
                Assert.That(workflow.CanHandleLocalInterfaceLanguageCommand("Speak French"),Is.False);
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void RepeatedStylingPreservesExistingCallbackAndAddsOneStyle()
        {
            GameObject root=new GameObject("Panel",typeof(RectTransform),typeof(Image));
            GameObject buttonObject=new GameObject("Next Button",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(root.transform,false);Button button=buttonObject.GetComponent<Button>();int calls=0;
            button.onClick.AddListener(()=>calls++);
            try
            {
                ThinkingSpaceUIStyler.StylePanel(root,false,true);
                ThinkingSpaceUIStyler.StyleHierarchy(root);
                ThinkingSpaceUIStyler.StyleHierarchy(root);
                button.onClick.Invoke();
                Assert.That(calls,Is.EqualTo(1));
                Assert.That(buttonObject.GetComponents<NavigationButton>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<BasePanel>().Length,Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void NonBlockingStatusHasNoRetryOrDismissActions()
        {
            GameObject owner=new GameObject("Status");
            try
            {
                WorkflowStatusPresenter presenter=owner.AddComponent<WorkflowStatusPresenter>();
                presenter.ShowStatus("Language","Interface language changed.");
                Button[] buttons=owner.GetComponentsInChildren<Button>(true);
                Assert.That(buttons.Length,Is.EqualTo(2));
                Assert.That(buttons[0].gameObject.activeSelf,Is.False);
                Assert.That(buttons[1].gameObject.activeSelf,Is.False);
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void SynthesizedFallbackSuppressesOverlappingPrerecordedErrorSpeech()
        {
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldPlayPrerecordedErrorAcknowledgement(true),Is.False);
            Assert.That(ThinkingSpaceAIWorkflowController.ShouldPlayPrerecordedErrorAcknowledgement(false),Is.True);
        }
    }
}
