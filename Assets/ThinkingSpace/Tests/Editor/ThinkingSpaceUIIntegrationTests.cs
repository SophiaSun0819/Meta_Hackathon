using NUnit.Framework;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;
using UnityEngine.EventSystems;
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

        [Test]
        public void SharedPanelManipulatorCreatesOneSlimUiGrabHandle()
        {
            GameObject root=new GameObject("Movable Panel",typeof(RectTransform),typeof(Image));
            try
            {
                RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(600f,400f);
                WorldSpacePanelManipulator first=WorldSpacePanelManipulator.Attach(rect);
                WorldSpacePanelManipulator second=WorldSpacePanelManipulator.Attach(rect);
                Assert.That(second,Is.SameAs(first));
                Assert.That(root.GetComponentsInChildren<WorldSpacePanelManipulator>(true).Length,Is.EqualTo(1));
                Image handle=first.GetComponent<Image>();
                Assert.That(handle.raycastTarget,Is.True);
                Assert.That(handle.sprite,Is.Not.Null,"Grab affordance should reuse the installed SlimUI theme.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void MetaSelectionBoundsIncludeReachTolerance()
        {
            Vector3 size=MetaHandInteractionAdapter.CalculateBoundsSize(new Vector2(600f,400f),28f,.035f);
            Assert.That(size,Is.EqualTo(new Vector3(656f,456f,.035f)));
        }

        [Test]
        public void PanelManipulatorMovesOnlyAfterDeliberatePointerDrag()
        {
            GameObject eventOwner=new GameObject("Event System",typeof(EventSystem));
            GameObject root=new GameObject("Movable Panel",typeof(RectTransform),typeof(Image));
            try
            {
                RectTransform rect=root.GetComponent<RectTransform>();
                WorldSpacePanelManipulator handle=WorldSpacePanelManipulator.Attach(rect);
                PointerEventData pointer=new PointerEventData(eventOwner.GetComponent<EventSystem>());
                pointer.pointerCurrentRaycast=new RaycastResult{gameObject=handle.gameObject,worldPosition=Vector3.zero};
                handle.OnPointerDown(pointer);handle.OnBeginDrag(pointer);
                pointer.pointerCurrentRaycast=new RaycastResult{gameObject=handle.gameObject,worldPosition=new Vector3(.02f,.01f,0f)};
                handle.OnDrag(pointer);
                Assert.That(handle.IsDragging,Is.True);
                Assert.That(handle.HasManualPlacement,Is.True);
                Assert.That(rect.position,Is.EqualTo(new Vector3(.02f,.01f,0f)));
                handle.OnEndDrag(pointer);
                Assert.That(handle.IsDragging,Is.False);
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(eventOwner);}
        }

        [Test]
        public void RectangularButtonsUseSlicedSlimUiSprites()
        {
            GameObject root=new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button));
            try
            {
                root.GetComponent<RectTransform>().sizeDelta=new Vector2(240f,72f);
                ThinkingSpaceUIStyler.StyleButton(root.GetComponent<Button>());
                Image image=root.GetComponent<Image>();
                Assert.That(image.sprite,Is.Not.Null);
                Assert.That(image.type,Is.EqualTo(Image.Type.Sliced));
                Assert.That(root.GetComponent<Button>().transition,Is.EqualTo(Selectable.Transition.SpriteSwap));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void ExplicitConversationControlsFollowBackendCapability()
        {
            Assert.That(BrainstormMenuView.ExplicitConversationControlsEnabled(false),Is.False);
            Assert.That(BrainstormMenuView.ExplicitConversationControlsEnabled(true),Is.True);
            Assert.That(ThinkingSpaceLocalizationController.Get("settings.conversation.unavailable"),
                Is.Not.Empty);
        }
    }
}
