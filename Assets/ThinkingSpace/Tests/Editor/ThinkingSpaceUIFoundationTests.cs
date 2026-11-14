using NUnit.Framework;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ThinkingSpaceUIFoundationTests
    {
        [Test]
        public void MetaAdapterBuildsCompleteInteractionStackWithoutDuplicates()
        {
            int eventSystems=Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length;
            GameObject root=NewCanvas("Meta Adapter",new Vector2(520f,185f));
            try
            {
                MetaHandInteractionAdapter adapter=root.AddComponent<MetaHandInteractionAdapter>();
                adapter.Configure(root.GetComponent<Canvas>(),root.GetComponent<RectTransform>());
                Assert.That(adapter.IsConfigured,Is.True);
                Assert.That(root.GetComponents<PointableCanvas>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<PlaneSurface>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<BoundsClipper>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<ClippedPlaneSurface>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<RayInteractable>().Length,Is.EqualTo(1));
                Assert.That(root.GetComponents<PokeInteractable>().Length,Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length,Is.EqualTo(eventSystems));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void CompactPanelUsesNonRaycastDecorativeEdgesAndSolidBackground()
        {
            GameObject root=new GameObject("Compact",typeof(RectTransform),typeof(Image));
            try
            {
                CompactPanel panel=root.AddComponent<CompactPanel>();panel.ApplyVisuals();
                Assert.That(root.GetComponent<Image>().sprite,Is.Null,"Zero-border SlimUI sprites must not be stretched as sliced backgrounds.");
                Image[] images=root.GetComponentsInChildren<Image>(true);Assert.That(images.Length,Is.EqualTo(5));
                for(int index=1;index<images.Length;index++)Assert.That(images[index].raycastTarget,Is.False);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void StyledButtonReplacesOwnedCallbackAndLocalizesLabel()
        {
            GameObject root=new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button));
            GameObject labelObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));labelObject.transform.SetParent(root.transform,false);
            try
            {
                int calls=0;PrimaryButton style=root.AddComponent<PrimaryButton>();style.ConfigureLabel(labelObject.GetComponent<TMP_Text>());
                style.ConfigureLocalization("common.retry");style.SetAction(()=>calls++);style.SetAction(()=>calls++);root.GetComponent<Button>().onClick.Invoke();
                Assert.That(calls,Is.EqualTo(1));Assert.That(style.Label.text,Is.EqualTo(ThinkingSpaceLocalizationController.Get("common.retry")));
                Assert.That(style.Label.raycastTarget,Is.False);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void WorkflowStatusUsesSharedMetaStackAndRoutesActionsOnce()
        {
            GameObject owner=new GameObject("Owner");
            try
            {
                WorkflowStatusPresenter presenter=owner.AddComponent<WorkflowStatusPresenter>();int retry=0;
                presenter.ShowRecoverable(RecoverableWorkflowFailureKind.NetworkUnavailable,()=>retry++,()=>{},true);
                MetaHandInteractionAdapter adapter=owner.GetComponentInChildren<MetaHandInteractionAdapter>(true);
                Assert.That(adapter,Is.Not.Null);Assert.That(adapter.IsConfigured,Is.True);
                Button[] buttons=owner.GetComponentsInChildren<Button>(true);Assert.That(buttons.Length,Is.EqualTo(2));
                buttons[0].onClick.Invoke();Assert.That(retry,Is.EqualTo(1));Assert.That(presenter.IsVisible,Is.False);
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void ExistingMenuTemplateRemainsLoadable()
        {
            GameObject prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            Assert.That(prefab,Is.Not.Null);Assert.That(prefab.GetComponent<BrainstormMenuView>(),Is.Not.Null);Assert.That(prefab.GetComponent<Canvas>(),Is.Not.Null);
        }

        private static GameObject NewCanvas(string name,Vector2 size)
        {
            GameObject root=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            root.GetComponent<RectTransform>().sizeDelta=size;root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;return root;
        }
    }
}
