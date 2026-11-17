using NUnit.Framework;
using System.Linq;
using TMPro;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using ThinkingSpace.UI.DesignKit;
using UnityEditor;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ThinkingSpaceUIIntegrationTests
    {
        [TestCase("Orby, switch the UI panels to French.","fr")]
        [TestCase("Change the interface to Spanish.","es")]
        [TestCase("Put the menu in English.","en")]
        [TestCase("Cambia la interfaz a francés.","fr")]
        [TestCase("Change the interface to Simplified Chinese.","zh-CN")]
        [TestCase("Put the menu in Hindi.","hi-IN")]
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
                Assert.That(handle.raycastTarget,Is.False,"Grab decoration must never intercept menu graphics.");
                Assert.That(handle.color.a,Is.Zero);
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
        public void PanelManipulatorUsesDirectHandGrabZonesOutsideInteractiveContent()
        {
            GameObject root=new GameObject("Movable Panel",typeof(RectTransform),typeof(Image));
            try
            {
                RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1000f,700f);
                WorldSpacePanelManipulator handle=WorldSpacePanelManipulator.Attach(rect);
                Assert.That(root.GetComponent<Grabbable>(),Is.Not.Null);
                Assert.That(root.GetComponent<StablePanelGrabTransformer>(),Is.Not.Null);
                Assert.That(handle.GetComponent<HandGrabInteractable>(),Is.Null);
                Assert.That(handle.GetComponent<DistanceGrabInteractable>(),Is.Null,
                    "The menu's ray must remain owned by PointableCanvas, not a distance-grab surface.");
                Assert.That(handle.GetComponent<BoxCollider>(),Is.Null);
                Transform proximity=handle.transform.Find("External Proximity Grab Zone");
                Assert.That(proximity,Is.Not.Null);
                Assert.That(proximity.GetComponent<HandGrabInteractable>(),Is.Null);
                Assert.That(proximity.GetComponent<DistanceGrabInteractable>(),Is.Null);
                Assert.That(proximity.GetComponent<PanelGrabProximityZone>().WorldRange,Is.InRange(.16f,.20f));
                string[] edgeNames={"Top","Bottom","Left","Right"};
                foreach(string edgeName in edgeNames)
                {
                    Transform edge=proximity.Find(edgeName);Assert.That(edge,Is.Not.Null,edgeName);
                    Assert.That(edge.GetComponent<HandGrabInteractable>(),Is.Not.Null,edgeName);
                    Assert.That(edge.GetComponent<BoxCollider>(),Is.Not.Null,edgeName);
                    Assert.That(edge.GetComponent<DistanceGrabInteractable>(),Is.Null,edgeName);
                    Assert.That(edge.GetComponent<Graphic>(),Is.Null,edgeName+" must be physically grabbable but visually/raycast transparent.");
                    RectTransform edgeRect=edge as RectTransform;
                    BoxCollider edgeCollider=edge.GetComponent<BoxCollider>();
                    Assert.That(edgeCollider.center.x,Is.EqualTo(edgeRect.rect.center.x).Within(.001f),edgeName);
                    Assert.That(edgeCollider.center.y,Is.EqualTo(edgeRect.rect.center.y).Within(.001f),
                        edgeName+" collider must follow the outward RectTransform instead of remaining on the menu anchor.");
                }
                Assert.That((proximity.Find("Top") as RectTransform).pivot,Is.EqualTo(Vector2.zero),
                    "Top reach must extend above the panel; a top pivot would extend it inward.");
                Transform highlight=handle.transform.Find("Grab Range Highlight");
                Assert.That(highlight,Is.Not.Null,"Near-grab reach needs a distinct non-button affordance.");
                Assert.That(highlight.GetComponent<Image>().raycastTarget,Is.False);
                Assert.That(highlight.GetComponentsInChildren<Image>(true).All(image=>!image.raycastTarget),Is.True);
                Assert.That(handle.HasDistinctProximityHighlight,Is.True);
                handle.ConfigureExternalProximityWorldRange(.25f);
                Assert.That(proximity.GetComponent<PanelGrabProximityZone>().WorldRange,Is.EqualTo(.25f).Within(.001f));
                RectTransform handleRect=handle.transform as RectTransform;
                Assert.That(handleRect.anchorMin,Is.EqualTo(Vector2.zero));
                Assert.That(handleRect.anchorMax,Is.EqualTo(Vector2.one));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void MainMenuEdgeGrabCollidersDoNotInterceptButtonPointerCallback()
        {
            GameObject eventObject=new GameObject("EventSystem",typeof(EventSystem));
            GameObject root=new GameObject("Main Menu",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(Image));
            GameObject buttonObject=new GameObject("Main Action",typeof(RectTransform),typeof(Image),typeof(Button));
            try
            {
                RectTransform rootRect=root.GetComponent<RectTransform>();rootRect.sizeDelta=new Vector2(1000f,700f);
                rootRect.localScale=Vector3.one*.000278f;
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                RectTransform buttonRect=buttonObject.GetComponent<RectTransform>();buttonRect.SetParent(rootRect,false);
                buttonRect.anchorMin=new Vector2(.2f,.65f);buttonRect.anchorMax=new Vector2(.8f,.82f);
                buttonRect.offsetMin=buttonRect.offsetMax=Vector2.zero;
                Button button=buttonObject.GetComponent<Button>();button.targetGraphic=buttonObject.GetComponent<Image>();
                int callbacks=0;button.onClick.AddListener(()=>callbacks++);
                WorldSpacePanelManipulator.Attach(rootRect);
                Physics.SyncTransforms();Canvas.ForceUpdateCanvases();

                Vector3 buttonCenter=buttonRect.TransformPoint(buttonRect.rect.center);
                Ray physicsRay=new Ray(buttonCenter-rootRect.forward*.25f,rootRect.forward);
                RaycastHit[] hits=Physics.RaycastAll(physicsRay,.5f,~0,QueryTriggerInteraction.Collide);
                Assert.That(hits.Any(hit=>hit.collider.transform.IsChildOf(rootRect)),Is.False,
                    "Outward grab colliders must not own a ray aimed at a main-menu button.");

                PointerEventData pointer=new PointerEventData(eventObject.GetComponent<EventSystem>())
                {pointerPress=buttonObject,rawPointerPress=buttonObject};
                ExecuteEvents.Execute(buttonObject,pointer,ExecuteEvents.pointerClickHandler);
                Assert.That(callbacks,Is.EqualTo(1),"A delivered menu pointer click must execute its bound callback.");
            }
            finally{Object.DestroyImmediate(buttonObject);Object.DestroyImmediate(root);Object.DestroyImmediate(eventObject);}
        }

        [Test]
        public void PanelGrabKeepsCanvasRaycasterAliveAndRecordsOnlyRealMovement()
        {
            GameObject root=new GameObject("Main Menu",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(Image));
            try
            {
                RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(1000f,700f);
                WorldSpacePanelManipulator handle=WorldSpacePanelManipulator.Attach(rect);
                var method=typeof(WorldSpacePanelManipulator).GetMethod("HandleGrabEvent",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Pose pose=new Pose(Vector3.zero,Quaternion.identity);

                method.Invoke(handle,new object[]{new PointerEvent(31,PointerEventType.Select,pose)});
                Assert.That(root.GetComponent<GraphicRaycaster>().enabled,Is.True,
                    "A grab candidate must not erase the pointer-up needed by a UI Button.");
                Assert.That(handle.HasActiveDragMoved,Is.False);
                method.Invoke(handle,new object[]{new PointerEvent(31,PointerEventType.Unselect,pose)});
                Assert.That(handle.LastDragMoved,Is.False,"A stationary pinch is still a button candidate, not a drag.");

                method.Invoke(handle,new object[]{new PointerEvent(32,PointerEventType.Select,pose)});
                root.transform.position+=Vector3.right*.02f;
                Assert.That(handle.HasActiveDragMoved,Is.True);
                method.Invoke(handle,new object[]{new PointerEvent(32,PointerEventType.Unselect,pose)});
                Assert.That(handle.LastDragMoved,Is.True,"A moved panel must retain release-click suppression.");
                Assert.That(root.GetComponent<GraphicRaycaster>().enabled,Is.True);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void GeneratedImagePanelKeepsOwnershipAndBubbleSafeInteractionContract()
        {
            GameObject root=new GameObject("Generated Image Panel",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            GameObject imageObject=new GameObject("Image",typeof(RectTransform),typeof(RawImage));
            try
            {
                imageObject.transform.SetParent(root.transform,false);
                GeneratedImagePanelView view=root.AddComponent<GeneratedImagePanelView>();
                GeneratedImagePanelBinding binding=new GeneratedImagePanelBinding
                {AccountId="account-a",SessionId="session-a",IdeaId="idea-a",AssetId="asset-a",RevisionId="revision-a",SourcePrompt="prompt"};
                view.Bind(binding,null);view.SetPanelSize(new Vector2(2000f,200f));
                Assert.That(view.IsBound,Is.True);
                Assert.That(view.Binding.SessionId,Is.EqualTo("session-a"));
                Assert.That((root.transform as RectTransform).sizeDelta,Is.EqualTo(new Vector2(1440f,360f)));
                Assert.That(imageObject.GetComponent<RawImage>().raycastTarget,Is.False,
                    "Generated pixels are presentation-only and must not steal bubble/menu pointer input.");
                Assert.That(view.Manipulator,Is.Not.Null,"Image panels must be repositionable with the shared stable panel manipulator.");
                Assert.That(view.Manipulator.GetComponent<Image>().raycastTarget,Is.False);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void GeneratedImagePanelKeepsRevisionAndAssetResponsesAccountBound()
        {
            GameObject root=new GameObject("Generated Image Panel",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            GameObject imageObject=new GameObject("Image",typeof(RectTransform),typeof(RawImage));
            Texture2D texture=new Texture2D(2,2);
            try
            {
                imageObject.transform.SetParent(root.transform,false);
                GeneratedImagePanelView view=root.AddComponent<GeneratedImagePanelView>();
                GeneratedImagePanelBinding binding=new GeneratedImagePanelBinding
                {AccountId="account-a",SessionId="session-a",IdeaId="idea-a",AssetId="asset-a",
                    RevisionId="revision-2",ParentRevisionId="revision-1",SourcePrompt="prompt"};
                view.Bind(binding,null);
                GeneratedImageAssetRequest request=default;
                view.AssetRequested+=value=>request=value;
                view.RequestAsset();

                Assert.That(view.State,Is.EqualTo(GeneratedImagePanelState.Loading));
                Assert.That(request.AccountId,Is.EqualTo("account-a"));
                Assert.That(request.SessionId,Is.EqualTo("session-a"));
                Assert.That(request.IdeaId,Is.EqualTo("idea-a"));
                Assert.That(request.RevisionId,Is.EqualTo("revision-2"));
                Assert.Throws<System.InvalidOperationException>(()=>view.ResolveAsset(new GeneratedImageAssetRequest
                    {AccountId="account-b",SessionId="session-a",IdeaId="idea-a",AssetId="asset-a",RevisionId="revision-2"},texture));
                view.SetAssetFailure(request,"provider timeout!",false);
                Assert.That(view.State,Is.EqualTo(GeneratedImagePanelState.Error));
                Assert.That(view.StatusCode,Is.EqualTo("provider_error"),"Raw provider details must not reach the UI state contract.");
                view.ResolveAsset(request,texture);
                Assert.That(view.State,Is.EqualTo(GeneratedImagePanelState.Ready));
                Assert.That(imageObject.GetComponent<RawImage>().raycastTarget,Is.False);

                GeneratedImagePanelBinding variation=binding;variation.AssetId="asset-b";variation.RevisionId="revision-3";
                Assert.Throws<System.InvalidOperationException>(()=>view.Bind(variation,null),
                    "Regenerations and variations must create a new panel revision rather than overwrite prior history.");
            }
            finally{Object.DestroyImmediate(texture);Object.DestroyImmediate(root);}
        }

        [Test]
        public void GeneratedImageLoaderValidatesOwnershipChecksumDimensionsAndEphemeralWebP()
        {
            var request=new GeneratedImageAssetRequest{AccountId="account-a",SessionId="session-a",IdeaId="idea-a",AssetId="asset-a",RevisionId="revision-a"};
            byte[] webp={82,73,70,70,4,0,0,0,87,69,66,80,1,2,3,4};
            var generation=new EphemeralImageGenerationWireResponse{generation_id="img-a",owner_id="account-a",session_id="session-a",idea_id="idea-a",operation="generate",mime_type="image/webp",width=1024,height=1024,asset_sha256=GeneratedImagePanelAssetLoader.Sha256(webp),ephemeral=true};
            var response=new EphemeralImageGenerateWireResponse{contract_version="1.0",generation=generation,asset=new EphemeralImageAssetWireResponse{encoding="base64",data=System.Convert.ToBase64String(webp)}};
            CollectionAssert.AreEqual(webp,GeneratedImagePanelAssetLoader.ValidateResponse(request,response));
            Assert.That(GeneratedImagePanelAssetLoader.CacheKey(generation),Does.StartWith("account-a|session-a|img-a|"));
            generation.owner_id="account-b";Assert.Throws<System.InvalidOperationException>(()=>GeneratedImagePanelAssetLoader.ValidateResponse(request,response));
            generation.owner_id="account-a";generation.asset_sha256=new string('0',64);Assert.Throws<System.InvalidOperationException>(()=>GeneratedImagePanelAssetLoader.ValidateResponse(request,response));
            generation.asset_sha256=GeneratedImagePanelAssetLoader.Sha256(webp);generation.estimated_cost_cents=30.01f;Assert.Throws<System.InvalidOperationException>(()=>GeneratedImagePanelAssetLoader.ValidateResponse(request,response));
            Assert.That(QuestWebPDecoder.HasWebPHeader(webp),Is.True);
        }

        [Test]
        public void QuestImageRuntimeComposesAuthenticatedOwnedMovablePanelAndPurgesOnLogout()
        {
            GameObject host=new GameObject("Quest Image Runtime");
            try
            {
                var tokenSource=new MemoryGeneratedImageAccessTokenSource();tokenSource.SetAuthenticatedSession("account-a","test-token-never-serialized");
                var runtime=host.AddComponent<QuestEphemeralImageRuntime>();runtime.Configure("https://images.staging.example/",tokenSource);
                GeneratedImagePanelView panel=runtime.GenerateForIdea("session-a","idea-a","A quiet blue library",new Vector3(0,1,2),Quaternion.identity,false);
                Assert.That(runtime.BackendOrigin,Is.EqualTo("https://images.staging.example"));
                Assert.That(panel.Binding.AccountId,Is.EqualTo("account-a"));Assert.That(panel.Binding.SessionId,Is.EqualTo("session-a"));Assert.That(panel.Binding.IdeaId,Is.EqualTo("idea-a"));
                Assert.That(panel.State,Is.EqualTo(GeneratedImagePanelState.Unavailable));Assert.That(panel.Manipulator,Is.Not.Null);
                Assert.That(panel.GetComponent<GeneratedImagePanelAssetLoader>().BackendOrigin,Is.EqualTo(runtime.BackendOrigin));
                runtime.RevokeAuthenticatedAccount("account-a");
                Assert.That(panel.State,Is.EqualTo(GeneratedImagePanelState.Unavailable));Assert.That(panel.StatusCode,Is.EqualTo("auth_required"));
                Assert.That(tokenSource.TryGetAccessToken(out _),Is.False);
                Object.DestroyImmediate(panel.gameObject);
            }
            finally{Object.DestroyImmediate(host);}
        }

        [Test]
        public void QuestImageRuntimeRejectsUnsafeOrRouteSpecificBackendConfiguration()
        {
            Assert.That(QuestEphemeralImageRuntime.NormalizeBackendOrigin("https://staging.example/"),Is.EqualTo("https://staging.example"));
            Assert.Throws<System.ArgumentException>(()=>QuestEphemeralImageRuntime.NormalizeBackendOrigin("http://staging.example"));
            Assert.Throws<System.ArgumentException>(()=>QuestEphemeralImageRuntime.NormalizeBackendOrigin("https://staging.example/api/images/generate"));
            Assert.Throws<System.ArgumentException>(()=>QuestEphemeralImageRuntime.NormalizeBackendOrigin("https://user:secret@staging.example"));
        }

        [Test]
        public void SpokenImageIntentRoutesToOwnedConfiguredMovablePanel()
        {
            GameObject host=new GameObject("Visual intents"),runtimeObject=new GameObject("Image runtime"),headsetObject=new GameObject("Headset");
            try
            {
                var session=new BrainstormSession(1);session.Ideas.Add(new BrainstormIdeaRecord{IdeaId="idea-city",Title="City"});
                var token=new MemoryGeneratedImageAccessTokenSource();token.SetAuthenticatedSession("account-owner","test-token");
                runtimeObject.AddComponent<QuestEphemeralImageRuntime>().Configure("https://images.staging.example",token);
                var controller=host.AddComponent<OrbyVisualIntentController>();controller.Configure(()=>session,()=>"idea-city",headsetObject.transform);
                Assert.That(controller.TryHandle("Orby, generate an image of a futuristic city.",false),Is.True);
                GeneratedImagePanelView panel=controller.LastImagePanel;Assert.That(controller.LastOutcome,Is.EqualTo(OrbyVisualIntentOutcome.ImageStarted));Assert.That(panel,Is.Not.Null);Assert.That(panel.gameObject.scene.IsValid(),Is.True);
                Assert.That(panel.Binding.SessionId,Is.EqualTo(session.SessionId));Assert.That(panel.Binding.IdeaId,Is.EqualTo("idea-city"));Assert.That(panel.Binding.AccountId,Is.EqualTo("account-owner"));Assert.That(panel.Binding.SourcePrompt,Is.EqualTo("a futuristic city"));
                Assert.That(panel.Manipulator,Is.Not.Null);Assert.That(panel.GetComponent<GeneratedImagePanelAssetLoader>().BackendOrigin,Is.EqualTo("https://images.staging.example"));Assert.That(controller.ImageRequestsStarted,Is.Zero);
                Object.DestroyImmediate(panel.gameObject);
            }
            finally{Object.DestroyImmediate(headsetObject);Object.DestroyImmediate(runtimeObject);Object.DestroyImmediate(host);}
        }

        [Test]
        public void SpokenBarChartIntentCreatesEditableRevisionFromThreeIdeasWithoutImageRequest()
        {
            GameObject host=new GameObject("Visual intents");
            try
            {
                var session=new BrainstormSession(1);session.Ideas.Add(new BrainstormIdeaRecord{IdeaId="idea-a",Title="Solar"});session.Ideas.Add(new BrainstormIdeaRecord{IdeaId="idea-b",Title="Garden"});session.Ideas.Add(new BrainstormIdeaRecord{IdeaId="idea-c",Title="Transit"});
                var controller=host.AddComponent<OrbyVisualIntentController>();controller.Configure(()=>session,()=>"idea-b",null);
                Assert.That(controller.TryHandle("Orby, create a bar chart comparing three ideas."),Is.True);
                EditableChartPanelView panel=controller.LastChartPanel;Assert.That(controller.LastOutcome,Is.EqualTo(OrbyVisualIntentOutcome.ChartCreated));Assert.That(panel,Is.Not.Null);Assert.That(panel.Revision.SessionId,Is.EqualTo(session.SessionId));Assert.That(panel.Revision.IdeaId,Is.EqualTo("idea-b"));
                CollectionAssert.AreEqual(new[]{"Solar","Garden","Transit"},panel.Revision.Points.Select(item=>item.Label));CollectionAssert.AreEqual(new[]{1f,1f,1f},panel.Revision.Points.Select(item=>item.Value));
                string parent=panel.Revision.RevisionId;EditableChartRevision edited=panel.CreateRevision("revision-edited","Priority",new[]{new EditableChartPoint{Label="S",Value=3},new EditableChartPoint{Label="G",Value=2},new EditableChartPoint{Label="T",Value=1}});
                Assert.That(edited.ParentRevisionId,Is.EqualTo(parent));Assert.That(edited.Title,Is.EqualTo("Priority"));Assert.That(edited.Points[0].Label,Is.EqualTo("S"));Assert.That(edited.Points[0].Value,Is.EqualTo(3));Assert.That(controller.ImageRequestsStarted,Is.Zero);Assert.That(panel.GetComponent<GeneratedImagePanelAssetLoader>(),Is.Null);
                Object.DestroyImmediate(panel.gameObject);
            }
            finally{Object.DestroyImmediate(host);}
        }

        [Test]
        public void VisualIntentRouterPreservesOrdinaryBrainstormingAndRequiresExactCommands()
        {
            Assert.That(OrbyVisualIntentRouter.Parse("Let's brainstorm a futuristic city").IsRecognized,Is.False);
            Assert.That(OrbyVisualIntentRouter.Parse("Create an idea about bar charts").IsRecognized,Is.False);
            Assert.That(OrbyVisualIntentRouter.Parse("Orby, generate an image of a futuristic city.").Kind,Is.EqualTo(OrbyVisualIntentKind.GenerateImage));
            Assert.That(GeneratedImagePanelAssetLoader.MapHttpFailure(503),Is.EqualTo("image_generation_disabled"));
            Assert.That(GeneratedImagePanelAssetLoader.MapHttpFailure(502),Is.EqualTo("provider_error"));
            Assert.That(GeneratedImagePanelAssetLoader.MapHttpFailure(400,"{\"code\":\"invalid_request\"}"),Is.EqualTo("invalid_request"));
            Assert.That(GeneratedImagePanelAssetLoader.MapHttpFailure(429,"{\"code\":\"daily_quota_exhausted\"}"),Is.EqualTo("quota_exhausted"));
            Assert.That(GeneratedImagePanelAssetLoader.MapHttpFailure(429,"{\"code\":\"rate_limited\"}"),Is.EqualTo("rate_limited"));
        }

        [Test]
        public void EditableBarChartKeepsStructuredRevisionAndSessionIdeaAssociation()
        {
            GameObject root=new GameObject("Editable Chart",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            try
            {
                EditableChartPanelView view=root.AddComponent<EditableChartPanelView>();
                var original=new EditableChartRevision{ChartId="chart-a",RevisionId="revision-1",SessionId="session-a",IdeaId="idea-a",Title="Votes",Type=EditableChartType.Bar,
                    Points=new System.Collections.Generic.List<EditableChartPoint>{new EditableChartPoint{Label="A",Value=2},new EditableChartPoint{Label="B",Value=5}}};
                view.Bind(original);
                Assert.That(root.transform.Find("Chart Plot").childCount,Is.EqualTo(2));
                EditableChartRevision edited=view.CreateRevision("revision-2","Updated votes",new[]{new EditableChartPoint{Label="A",Value=3},new EditableChartPoint{Label="B",Value=7}});
                Assert.That(edited.ParentRevisionId,Is.EqualTo("revision-1"));Assert.That(edited.SessionId,Is.EqualTo("session-a"));Assert.That(edited.IdeaId,Is.EqualTo("idea-a"));
                EditableChartRevision roundTrip=EditableChartPanelView.FromJson(view.ToJson());
                Assert.That(roundTrip.Title,Is.EqualTo("Updated votes"));Assert.That(roundTrip.Points[1].Value,Is.EqualTo(7));
                Assert.That(view.Manipulator,Is.Not.Null);Assert.That(root.GetComponentsInChildren<Image>(true).All(item=>!item.raycastTarget),Is.True);
                Assert.That(root.GetComponentInChildren<GeneratedImagePanelAssetLoader>(true),Is.Null,"Chart edits must never invoke image generation.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void FiveInterfaceLanguagesAreIndependentAndTranslated()
        {
            CollectionAssert.AreEquivalent(new[]{"en","es","fr","zh-CN","hi-IN"},
                ThinkingSpaceLocalizationController.SupportedInterfaceLanguages);
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("zh-cn"),Is.EqualTo("zh-CN"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("hi"),Is.EqualTo("hi-IN"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("zh-CN","menu.my_sessions"),Is.EqualTo("我的会话"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("hi-IN","menu.my_sessions"),Is.EqualTo("मेरे सत्र"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("hi-IN","settings.voice_language.info"),
                Does.Contain("इंटरफ़ेस"));
            foreach(string key in new[]{"hud.speech_detected","hud.finishing","hud.mic_off_finishing",
                        "state.listening","state.thinking","state.reviewing","ai.request_failed","ai.idea_save_failed"})
            {
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("zh-CN",key),
                    Is.Not.EqualTo(ThinkingSpaceLocalizationController.TranslateForLanguage("en",key)),key+" zh-CN");
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("hi-IN",key),
                    Is.Not.EqualTo(ThinkingSpaceLocalizationController.TranslateForLanguage("en",key)),key+" hi-IN");
            }
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("en","menu.help.voice"),Does.Contain("Hindi"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("hi-IN","menu.help.voice"),Does.Contain("यूआई"));
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
            Assert.That(BrainstormMenuView.ConversationLanguageControlEnabled(false,"en"),Is.True,
                "Legacy compatibility mode is a verified English conversation path.");
            Assert.That(BrainstormMenuView.ConversationLanguageControlEnabled(false,"es"),Is.False,
                "Unverified multilingual transcription must remain fail-closed.");
            Assert.That(BrainstormMenuView.ConversationLanguageControlEnabled(true,"hi-IN"),Is.True);
            Assert.That(ThinkingSpaceLocalizationController.Get("settings.conversation.unavailable"),
                Is.Not.Empty);
        }

        [Test]
        public void ActiveMenuPrefabCarriesApprovedDesignKitSkinAndExistingMetaCanvas()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponent<BrainstormMenuView>(),Is.Not.Null);
            Assert.That(prefab.GetComponent<QuestUIActiveMenuSkin>(),Is.Not.Null);
            Assert.That(prefab.GetComponent<PointableCanvas>(),Is.Not.Null);
        }

        [Test]
        public void ActiveMenuUsesOneAuthoritativeShellAcrossFirstOpenAndReopen()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                QuestUIActiveMenuSkin skin=instance.GetComponent<QuestUIActiveMenuSkin>();
                typeof(BrainstormMenuView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                view.ShowMain(System.Array.Empty<BrainstormSession>(),ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
                Assert.That(skin.AuthoritativeShellCount,Is.EqualTo(1));
                Assert.That(skin.HasAccountConnectionCard,Is.True,
                    "Home must route to the existing account/device connection flow.");
                Assert.That(skin.HasRedundantSinglePageIndicator,Is.False,
                    "A one-page Home must not display a meaningless Page 1 of 1 footer.");
                Assert.That(skin.LegacyFrameBehindContent,Is.True,"SlimUI frame must decorate behind the active shell instead of washing it out.");
                view.ShowDesignKitSessions();
                Assert.That(view.IsRedundantSessionStartVisible,Is.False,
                    "My Sessions must not repeat Start New Session from Home.");
                Assert.That(view.IsSessionImportVisible,Is.True,
                    "Import Session must remain available on the first My Sessions page.");
                Assert.That(view.IsLegacySessionThumbnailVisible,Is.False,
                    "The decorative thumbnail above My Sessions must be removed so content starts near the top.");
                view.ShowDesignKitHelp();
                Assert.That(skin.IsHelpVisible,Is.True,"Header Help must open the DesignKit help hub, not a legacy destination.");
                view.ShowMain(System.Array.Empty<BrainstormSession>(),ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
                Assert.That(skin.AuthoritativeShellCount,Is.EqualTo(1));
                Assert.That(instance.GetComponentsInChildren<QuestUIActiveMenuSkin>(true).Length,Is.EqualTo(1));
                Assert.That(instance.transform.Find("Fixed Header"),Is.Null,"Header must not escape the panel shell into the canvas root.");
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void ActiveMenuAppliesDesignKitPaletteToLegacyFunctionalPanels()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                typeof(BrainstormMenuView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                Transform session=FindDeep(instance.transform,"Session Menu");
                Assert.That(session,Is.Not.Null);
                Image image=session.GetComponent<Image>();
                Assert.That(image,Is.Not.Null);
                Assert.That(image.color,Is.EqualTo(QuestUIDesignTokens.Panel));
                Transform border=session.Find("DesignKit Thin Border");
                Assert.That(border==null||!border.gameObject.activeSelf,Is.True,
                    "Functional panels must not add the redundant inset rectangle seen on Quest.");
                Assert.That(session.GetComponent<Outline>(),Is.Null,
                    "Functional panel outlines must not stack with the accepted outer frame.");
                Transform legacy=FindDeep(instance.transform,"SlimUI Template Frame");
                Assert.That(legacy,Is.Not.Null);
                Assert.That(legacy.GetComponent<Image>().color.a,Is.Zero,
                    "The redundant authored frame must remain invisible.");
                Outline legacyOutline=legacy.GetComponent<Outline>();
                Assert.That(legacyOutline==null||!legacyOutline.enabled,Is.True);
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void SessionPageTwoUsesDistinctRowsWithoutScrolling()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                typeof(BrainstormMenuView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                GameObject panel=PrivateField<GameObject>(view,"sessionPanel");
                Button voice=PrivateField<Button>(view,"sessionVoiceButton");
                Button gestures=PrivateField<Button>(view,"sessionGesturesButton");
                Button left=PrivateField<Button>(view,"sessionLeftHandButton");
                Button wristLeft=PrivateField<Button>(view,"sessionWristLeftButton");
                Button recenter=PrivateField<Button>(view,"sessionRecenterButton");
                foreach(Button button in new[]{voice,gestures,left,wristLeft,recenter})
                {
                    Assert.That(button,Is.Not.Null);
                    Assert.That(button.transform.parent,Is.EqualTo(panel.transform),button.name);
                    RectTransform rect=button.GetComponent<RectTransform>();
                    Assert.That(rect.anchorMin.y,Is.GreaterThan(.11f),button.name+" must stay above the fixed footer.");
                }
                Assert.That(left.GetComponent<RectTransform>().anchorMin.y,
                    Is.GreaterThan(wristLeft.GetComponent<RectTransform>().anchorMax.y));
                Assert.That(voice.GetComponent<RectTransform>().anchorMin.y,
                    Is.GreaterThan(left.GetComponent<RectTransform>().anchorMax.y));
                Assert.That(view.SessionListNormalizedBounds.yMax,Is.EqualTo(.82f).Within(.001f));
                Assert.That(view.SessionListNormalizedBounds.yMin,Is.EqualTo(.04f).Within(.001f));
                Assert.That(BrainstormMenuView.CalculateSessionRowsPerPage(456f),Is.EqualTo(3));
                Assert.That(BrainstormMenuView.CalculateMainPageCount(7,3),Is.EqualTo(3));
                view.ShowSession(1,ThumbRecordingGestureController.RecordingHand.Right);
                typeof(BrainstormMenuView).GetMethod("ChangeSessionPage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,new object[]{1});

                Assert.That(voice.gameObject.activeSelf&&left.gameObject.activeSelf&&wristLeft.gameObject.activeSelf&&recenter.gameObject.activeSelf,Is.True);
                RectTransform voiceRect=voice.GetComponent<RectTransform>();
                RectTransform leftRect=left.GetComponent<RectTransform>();
                RectTransform wristRect=wristLeft.GetComponent<RectTransform>();
                RectTransform recenterRect=recenter.GetComponent<RectTransform>();
                Assert.That(leftRect.anchorMax.y,Is.LessThan(voiceRect.anchorMin.y));
                Assert.That(wristRect.anchorMax.y,Is.LessThan(leftRect.anchorMin.y));
                Assert.That(recenterRect.anchorMax.y,Is.LessThan(wristRect.anchorMin.y));
                Transform session=FindDeep(instance.transform,"Session Menu");
                Assert.That(session.GetComponentsInChildren<ScrollRect>(true).Any(scroll=>scroll.enabled),Is.False);
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void RuntimeVisibilityDiagnosticsRejectBorderOnlyMenu()
        {
            GameObject root=new GameObject("Menu",typeof(RectTransform),typeof(Canvas));
            GameObject border=new GameObject("DesignKit Thin Border",typeof(RectTransform),typeof(Image));
            border.transform.SetParent(root.transform,false);
            border.GetComponent<RectTransform>().sizeDelta=new Vector2(720f,720f);
            border.GetComponent<Image>().color=Color.cyan;
            try
            {
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                MenuRuntimeVisibilitySnapshot snapshot=MenuRuntimeVisibilityDiagnostics.Capture(
                    canvas,null,null,"Main","test-border-only");
                Assert.That(snapshot.BorderActive,Is.True);
                Assert.That(snapshot.VisibleGraphics,Is.GreaterThan(0));
                Assert.That(snapshot.ActionableButtons,Is.Zero);
                Assert.That(snapshot.TextVertices,Is.Zero);
                Assert.That(snapshot.HasActionableContent,Is.False,
                    "A shell or cyan border must never satisfy menu visibility diagnostics.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void RuntimeVisibilityDiagnosticsAcceptActionableRenderedMenu()
        {
            GameObject root=new GameObject("Menu",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(720f,720f);
            GameObject buttonObject=new GameObject("Action",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(root.transform,false);
            buttonObject.GetComponent<RectTransform>().sizeDelta=new Vector2(240f,100f);
            Button button=buttonObject.GetComponent<Button>();button.targetGraphic=buttonObject.GetComponent<Image>();
            GameObject labelObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform,false);
            labelObject.GetComponent<RectTransform>().sizeDelta=new Vector2(200f,70f);
            labelObject.GetComponent<TextMeshProUGUI>().text="Action";
            try
            {
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                Canvas.ForceUpdateCanvases();
                MenuRuntimeVisibilitySnapshot snapshot=MenuRuntimeVisibilityDiagnostics.Capture(
                    canvas,null,null,"Main","test-actionable");
                Assert.That(snapshot.ActionableButtons,Is.EqualTo(1));
                Assert.That(snapshot.VisibleLabels,Is.EqualTo(1));
                Assert.That(snapshot.TextVertices,Is.GreaterThan(0));
                Assert.That(snapshot.HasActionableContent,Is.True);
                Assert.That(snapshot.ToSanitizedLine().Length,Is.LessThanOrEqualTo(1024));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void RuntimeVisibilityDiagnosticsDetectsOpaqueFullPanelAboveControls()
        {
            GameObject root=new GameObject("Menu",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(720f,720f);
            GameObject buttonObject=new GameObject("Action",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(root.transform,false);
            buttonObject.GetComponent<RectTransform>().sizeDelta=new Vector2(240f,100f);
            Button button=buttonObject.GetComponent<Button>();button.targetGraphic=buttonObject.GetComponent<Image>();
            GameObject labelObject=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform,false);
            labelObject.GetComponent<RectTransform>().sizeDelta=new Vector2(200f,70f);
            labelObject.GetComponent<TextMeshProUGUI>().text="Action";
            GameObject overlay=new GameObject("Opaque Non Raycast Overlay",typeof(RectTransform),typeof(Image));
            overlay.transform.SetParent(root.transform,false);
            RectTransform overlayRect=overlay.GetComponent<RectTransform>();overlayRect.anchorMin=Vector2.zero;overlayRect.anchorMax=Vector2.one;
            overlayRect.offsetMin=overlayRect.offsetMax=Vector2.zero;
            overlay.GetComponent<Image>().color=new Color(.02f,.04f,.08f,.97f);
            overlay.GetComponent<Image>().raycastTarget=false;
            try
            {
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                Canvas.ForceUpdateCanvases();
                MenuRuntimeVisibilitySnapshot snapshot=MenuRuntimeVisibilityDiagnostics.Capture(
                    canvas,null,null,"Session","test-obstruction");
                Assert.That(snapshot.PotentialObstructions,Is.GreaterThanOrEqualTo(1));
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void DevelopmentRenderProbeIsLastSiblingAndNonInteractive()
        {
            GameObject root=new GameObject("Menu",typeof(RectTransform),typeof(Canvas));
            try
            {
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                Assert.That(MenuRuntimeVisibilityDiagnostics.ToggleProbe(canvas),Is.True);
                Transform probe=root.transform.Find("TS Development Menu Render Probe");
                Assert.That(probe,Is.Not.Null);
                Assert.That(probe.GetSiblingIndex(),Is.EqualTo(root.transform.childCount-1));
                Assert.That(probe.GetComponent<Image>().raycastTarget,Is.False);
                TMP_Text label=probe.GetComponentInChildren<TMP_Text>(true);
                Assert.That(label,Is.Not.Null);Assert.That(label.raycastTarget,Is.False);
                Assert.That(root.transform.Find("TS Development Menu Render Probe/Original Legacy Comparison"),Is.Not.Null);
                Assert.That(root.transform.Find("TS Development Menu Render Probe/Normalized Legacy Comparison"),Is.Not.Null);
                Assert.That(MenuRuntimeVisibilityDiagnostics.ToggleProbe(canvas),Is.True,
                    "Second toggle should show the normalized comparison state.");
                Transform normalized=root.transform.Find("TS Development Menu Render Probe/Normalized Legacy Comparison");
                Assert.That(normalized.gameObject.activeSelf,Is.True);
                TMP_Text normalizedLabel=normalized.Find("Normalized Legacy Rubik Label").GetComponent<TMP_Text>();
                Assert.That(normalizedLabel.fontSharedMaterial,Is.Not.Null);
                normalizedLabel.ForceMeshUpdate(false,false);
                Assert.That(normalizedLabel.textInfo.meshInfo.Sum(mesh=>mesh.vertexCount),Is.GreaterThan(0));
                Assert.That(MenuRuntimeVisibilityDiagnostics.ToggleProbe(canvas),Is.False,
                    "Third toggle should hide the probe.");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void DevelopmentRenderProbeCopiesLegacyMaterialsWithoutInput()
        {
            GameObject root=new GameObject("Menu",typeof(RectTransform),typeof(Canvas));
            GameObject session=new GameObject("Session Menu",typeof(RectTransform));session.transform.SetParent(root.transform,false);
            GameObject buttonObject=new GameObject("Export Session",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(session.transform,false);
            Image sourceImage=buttonObject.GetComponent<Image>();sourceImage.color=new Color(.2f,.3f,.4f,1f);
            Button button=buttonObject.GetComponent<Button>();button.targetGraphic=sourceImage;
            GameObject labelObject=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform,false);
            TMP_Text sourceLabel=labelObject.GetComponent<TMP_Text>();sourceLabel.text="Export Session";sourceLabel.color=Color.magenta;
            try
            {
                Canvas canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                Assert.That(MenuRuntimeVisibilityDiagnostics.ToggleProbe(canvas),Is.True);
                Transform original=root.transform.Find("TS Development Menu Render Probe/Original Legacy Comparison");
                Image copy=original.Find("Original Legacy Button Image").GetComponent<Image>();
                TMP_Text copiedLabel=original.Find("Original Legacy Rubik Label").GetComponent<TMP_Text>();
                Assert.That(copy.color,Is.EqualTo(sourceImage.color));
                Assert.That(copiedLabel.color,Is.EqualTo(sourceLabel.color));
                Assert.That(copy.raycastTarget,Is.False);Assert.That(copiedLabel.raycastTarget,Is.False);
                Assert.That(original.GetComponentsInChildren<Button>(true),Is.Empty);
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void AuthoredRuntimeMenuProducesActionableVisibilitySnapshot()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                typeof(BrainstormMenuView).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                view.ShowMain(System.Array.Empty<BrainstormSession>(),
                    ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
                Canvas.ForceUpdateCanvases();
                MenuRuntimeVisibilitySnapshot snapshot=MenuRuntimeVisibilityDiagnostics.Capture(
                    instance.GetComponent<Canvas>(),view,view.ActiveMenuSkin,"Main","test-authored-runtime");
                Assert.That(snapshot.SkinInitialized,Is.True);
                Assert.That(snapshot.ActionableButtons,Is.GreaterThanOrEqualTo(4));
                Assert.That(snapshot.VisibleLabels,Is.GreaterThan(0));
                Assert.That(snapshot.TextVertices,Is.GreaterThan(0));
                Assert.That(snapshot.HasActionableContent,Is.True);
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void AuthoredPaginatedMenuDisablesLegacyClipMasks()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                typeof(BrainstormMenuView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                Transform main=FindDeep(instance.transform,"Main Menu");
                Transform session=FindDeep(instance.transform,"Session Menu");
                Assert.That(main,Is.Not.Null);
                Assert.That(session,Is.Not.Null);
                Assert.That(main.GetComponentsInChildren<RectMask2D>(true).Count(mask=>mask.enabled),Is.Zero,
                    "The non-scrolling Main menu must not retain a clip mask that can cull its runtime labels and buttons.");
                Assert.That(session.GetComponentsInChildren<RectMask2D>(true).Count(mask=>mask.enabled),Is.Zero,
                    "The non-scrolling Session menu must not retain a clip mask that can cull its runtime labels and buttons.");
                Assert.That(instance.GetComponentsInChildren<QuestUIActiveMenuSkin>(true)[0]
                    .GetComponentsInChildren<RectMask2D>(true).Count(mask=>mask.enabled),Is.Zero,
                    "The DesignKit functional hierarchy must not add a replacement clip mask.");
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void FunctionalPanelPaletteRemovesLegacyGrayStructuralSurfaces()
        {
            GameObject owner=new GameObject("Owner",typeof(RectTransform));
            GameObject panel=new GameObject("Session Panel",typeof(RectTransform),typeof(Image));
            GameObject legacy=new GameObject("Legacy Background",typeof(RectTransform),typeof(Image));
            GameObject decoration=new GameObject("SlimUI Decoration",typeof(RectTransform),typeof(Image));
            GameObject viewport=new GameObject("Functional Page Content",typeof(RectTransform),typeof(Image));
            panel.transform.SetParent(owner.transform,false);legacy.transform.SetParent(panel.transform,false);
            decoration.transform.SetParent(panel.transform,false);
            viewport.transform.SetParent(panel.transform,false);
            legacy.GetComponent<Image>().color=Color.gray;
            try
            {
                QuestUIActiveMenuSkin skin=owner.AddComponent<QuestUIActiveMenuSkin>();
                skin.ApplyPaletteToFunctionalPanels(panel);
                Assert.That(panel.GetComponent<Image>().color,Is.EqualTo(QuestUIDesignTokens.Panel));
                Assert.That(legacy.GetComponent<Image>().color.r,Is.EqualTo(QuestUIDesignTokens.Panel.r).Within(.001f));
                Assert.That(legacy.GetComponent<Image>().raycastTarget,Is.False);
                Assert.That(decoration.GetComponent<Image>().color.a,Is.Zero);
                Assert.That(decoration.GetComponent<Image>().raycastTarget,Is.False);
                Assert.That(viewport.GetComponent<Image>().color.a,Is.Zero);
                Assert.That(panel.transform.Find("DesignKit Thin Border"),Is.Null,
                    "Palette application must not create a redundant inset rectangle.");
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void FunctionalButtonsAndLabelsRemainRaycastableWithoutInsetBorder()
        {
            GameObject canvasObject=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));
            GameObject panel=new GameObject("Session Panel",typeof(RectTransform),typeof(Image));
            panel.transform.SetParent(canvasObject.transform,false);
            RectTransform panelRect=panel.GetComponent<RectTransform>();panelRect.sizeDelta=new Vector2(960f,640f);
            GameObject content=new GameObject("Content",typeof(RectTransform));content.transform.SetParent(panel.transform,false);
            GameObject buttonObject=new GameObject("Export Session",typeof(RectTransform),typeof(Image),typeof(Button));
            buttonObject.transform.SetParent(content.transform,false);
            buttonObject.GetComponent<RectTransform>().sizeDelta=new Vector2(300f,80f);
            Button button=buttonObject.GetComponent<Button>();button.targetGraphic=buttonObject.GetComponent<Image>();
            GameObject labelObject=new GameObject("Text",typeof(RectTransform),typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform,false);
            labelObject.GetComponent<RectTransform>().sizeDelta=new Vector2(280f,60f);
            TMP_Text label=labelObject.GetComponent<TMP_Text>();label.text="Export Session";
            try
            {
                Canvas canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                QuestUIActiveMenuSkin skin=canvasObject.AddComponent<QuestUIActiveMenuSkin>();
                skin.ApplyPaletteToFunctionalPanels(panel);
                Canvas.ForceUpdateCanvases();label.ForceMeshUpdate(false,false);
                Assert.That(panel.transform.Find("DesignKit Thin Border"),Is.Null);
                Assert.That(button.targetGraphic.raycastTarget,Is.True);
                Assert.That(button.targetGraphic.canvasRenderer.cull,Is.False);
                Assert.That(label.canvasRenderer.cull,Is.False);
                Assert.That(panel.GetComponentsInChildren<RectMask2D>(true).Count(mask=>mask.enabled),Is.Zero);
            }
            finally{Object.DestroyImmediate(canvasObject);}
        }

        [Test]
        public void OrbySubtitleUsesSingleApprovedSpeechBubbleSurface()
        {
            GameObject owner=new GameObject("Subtitle",typeof(RectTransform));
            try
            {
                ThinkingSpace.Companion.OrbySubtitleView view=
                    owner.AddComponent<ThinkingSpace.Companion.OrbySubtitleView>();
                typeof(ThinkingSpace.Companion.OrbySubtitleView).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                Assert.That(view.UsesDesignKitSpeechBubble,Is.True);
                Assert.That(view.SubtitleTextSurfaceCount,Is.EqualTo(1));
                Assert.That(owner.GetComponents<Canvas>().Length,Is.EqualTo(1));
                Assert.That(owner.GetComponents<QuestUIOrbySpeechBubble>().Length,Is.EqualTo(1));
                Assert.That(owner.GetComponent<Image>().color,Is.EqualTo(QuestUIDesignTokens.Panel));
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void OrbySubtitleBubbleSizesToContentWithinCompactBounds()
        {
            GameObject owner=new GameObject("Subtitle",typeof(RectTransform));
            try
            {
                ThinkingSpace.Companion.OrbySubtitleView view=owner.AddComponent<ThinkingSpace.Companion.OrbySubtitleView>();
                typeof(ThinkingSpace.Companion.OrbySubtitleView).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                view.Show("A short response.",true,2f);
                Vector2 shortSize=view.CurrentPanelSize;
                view.Show("This longer response verifies that the approved speech bubble grows only as much as its readable content requires.",true,5f);
                Vector2 longSize=view.CurrentPanelSize;
                Assert.That(shortSize.x,Is.InRange(340f,610f));
                Assert.That(shortSize.y,Is.InRange(112f,190f));
                Assert.That(longSize.x,Is.GreaterThanOrEqualTo(shortSize.x));
                Assert.That(longSize.y,Is.InRange(112f,190f));
            }
            finally{Object.DestroyImmediate(owner);}
        }

        [Test]
        public void MySessionsRebuildsFromLiveStoreWhenOpened()
        {
            string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"thinking-space-ui-"+System.Guid.NewGuid().ToString("N"));
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab");
            GameObject instance=Object.Instantiate(prefab);
            try
            {
                BrainstormMenuView view=instance.GetComponent<BrainstormMenuView>();
                typeof(BrainstormMenuView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(view,null);
                BrainstormSessionStore store=new BrainstormSessionStore(directory);
                store.StartNewSession();
                view.ShowMain(store.Sessions,ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
                Assert.That(view.RuntimeSessionRowCount,Is.EqualTo(1));
                store.StartNewSession();
                view.ShowDesignKitSessions();
                Assert.That(view.RuntimeSessionRowCount,Is.EqualTo(2),"My Sessions must refresh after import or session creation.");
                Assert.That(view.InterfaceLanguageButtonCount,Is.EqualTo(5));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);
            }
        }

        [TestCase("TTS backend base URL is unavailable",0,"None","configuration_missing")]
        [TestCase("Development authentication token is unavailable",0,"None","authentication_unavailable")]
        [TestCase("TTS HTTP request failed: 503",503,"text/plain","server_unavailable")]
        [TestCase("Unexpected TTS MIME type",200,"application/json","invalid_audio_response")]
        public void VoiceUnavailableDiagnosticsExposeOnlyStableCategories(string failure,long status,string mime,string expected)
            =>Assert.That(ThinkingSpace.Companion.OrbyVoiceDirector.ClassifyTtsFailure(failure,status,mime),Is.EqualTo(expected));

        [TestCase("Voice service is not configured.",503,"configuration_missing")]
        [TestCase("Service temporarily unavailable.",503,"server_unavailable")]
        [TestCase("Too many voice requests; retry later.",429,"rate_limited")]
        [TestCase("Voice provider timed out.",504,"provider_timeout")]
        [TestCase("Voice provider returned invalid audio.",502,"invalid_audio_response")]
        public void SanitizedTtsErrorsRemainBoundedAndSpecific(string error,long status,string expected)
        {
            string json=JsonUtility.ToJson(new SanitizedVoiceError
                {request_id="voice-1",session_id="session-1",status="error",error=error});
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(json);
            Assert.That(ThinkingSpace.Companion.OrbyVoiceDirector.ParseSanitizedTtsErrorCategory(
                bytes,"application/json",status,"voice-1","session-1"),Is.EqualTo(expected));
        }

        [Test]
        public void SanitizedTtsErrorParserRejectsOversizeAndIdentityMismatch()
        {
            Assert.That(ThinkingSpace.Companion.OrbyVoiceDirector.ParseSanitizedTtsErrorCategory(
                new byte[4097],"application/json",503,"voice-1","session-1"),Is.EqualTo("server_unavailable"));
            byte[] mismatch=System.Text.Encoding.UTF8.GetBytes(
                "{\"request_id\":\"other\",\"session_id\":\"session-1\",\"status\":\"error\",\"error\":\"Voice service is not configured.\"}");
            Assert.That(ThinkingSpace.Companion.OrbyVoiceDirector.ParseSanitizedTtsErrorCategory(
                mismatch,"application/json",503,"voice-1","session-1"),Is.EqualTo("response_identity_mismatch"));
            Assert.That(ThinkingSpace.Companion.OrbyVoiceDirector.ClassifyTtsFailure("None",200,"audio/mpeg"),Is.EqualTo("none"));
        }

        [Test]
        public void DesignKitInteractionPaletteProvidesDistinctSpatialStates()
        {
            Assert.That(QuestUIDesignTokens.Panel.a,Is.GreaterThan(.95f));
            Assert.That(QuestUIDesignTokens.Hover,Is.Not.EqualTo(QuestUIDesignTokens.Raised));
            Assert.That(QuestUIDesignTokens.Pressed,Is.Not.EqualTo(QuestUIDesignTokens.Hover));
            Assert.That(QuestUIDesignTokens.Disabled.a,Is.LessThan(QuestUIDesignTokens.Raised.a));
        }

        private static Transform FindDeep(Transform root,string name)
        {
            if(root.name==name)return root;
            for(int i=0;i<root.childCount;i++){Transform found=FindDeep(root.GetChild(i),name);if(found!=null)return found;}
            return null;
        }

        private static T PrivateField<T>(object target,string name) where T:class
            =>target.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(target) as T;

        [Test]
        public void UnfinishedSessionActionsRemainExplicitlyUnavailable()
        {
            Assert.That(ThinkingSpaceLocalizationController.Get("menu.feature.unavailable"),Is.Not.Empty);
            Assert.That(BrainstormMenuView.ExplicitConversationControlsEnabled(false),Is.False);
        }

        [System.Serializable]
        private sealed class SanitizedVoiceError
        {public string request_id,session_id,status,error;}
    }
}
