using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ThinkingSpace.Tests.PlayMode
{
    public sealed class ThinkingSpaceQuestRuntimeSmokeTests
    {
        [UnityTest]
        public IEnumerator SandboxLoadsWithoutMissingScriptsOrDuplicateEventSystems()
        {
            AsyncOperation load=SceneManager.LoadSceneAsync("Sandbox",LoadSceneMode.Single);
            Assert.That(load,Is.Not.Null,"Sandbox must remain in enabled build settings.");
            while(!load.isDone)yield return null;
            yield return null;
            yield return null;

            Scene scene=SceneManager.GetActiveScene();
            Assert.That(scene.path,Is.EqualTo("Assets/Scenes/Sandbox.unity"));
            int missing=0;
            foreach(GameObject root in scene.GetRootGameObjects())
                foreach(Transform item in root.GetComponentsInChildren<Transform>(true))
                    missing+=item.gameObject.GetComponents<Component>().Count(component=>component==null);
            Assert.That(missing,Is.Zero,"Sandbox contains missing MonoBehaviour references.");
            Assert.That(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length,
                Is.LessThanOrEqualTo(1),"Runtime UI must not create a duplicate EventSystem.");
        }

        [UnityTest]
        public IEnumerator OrbyBootstrapCreatesOneCompanionOwner()
        {
            if(SceneManager.GetActiveScene().name!="Sandbox")
            {
                AsyncOperation load=SceneManager.LoadSceneAsync("Sandbox",LoadSceneMode.Single);
                while(load!=null&&!load.isDone)yield return null;
            }
            yield return null;
            Type type=RuntimeType("ThinkingSpace.Companion.OrbyPlaceholderView");
            Assert.That(type,Is.Not.Null);
            Assert.That(Resources.FindObjectsOfTypeAll(type).Count(value=>IsSceneObject(value)),Is.EqualTo(1),
                "Exactly one visible-companion owner should exist after the runtime bootstrap.");
        }

        [UnityTest]
        public IEnumerator AnimatedOrbyUsesOneVisualAndAuthoritativeVoiceSource()
        {
            if(SceneManager.GetActiveScene().name!="Sandbox")
            {
                AsyncOperation load=SceneManager.LoadSceneAsync("Sandbox",LoadSceneMode.Single);
                while(load!=null&&!load.isDone)yield return null;
            }
            yield return null;
            yield return null;

            Type viewType=RuntimeType("ThinkingSpace.Companion.OrbyPlaceholderView");
            Type voiceType=RuntimeType("ThinkingSpace.Companion.OrbyVoiceDirector");
            Component view=UnityEngine.Object.FindFirstObjectByType(viewType) as Component;
            Component voice=UnityEngine.Object.FindFirstObjectByType(voiceType) as Component;
            Assert.That(view,Is.Not.Null);
            Assert.That(voice,Is.Not.Null);
            object adapter=viewType.GetProperty("ModelAdapter")?.GetValue(view);
            Transform model=adapter?.GetType().GetProperty("ModelRoot")?.GetValue(adapter) as Transform;
            Assert.That(model,Is.Not.Null);
            Assert.That(model.name,Is.EqualTo("OrbiM_N"));
            Vector3 authoredScale=(Vector3)adapter.GetType().GetProperty("AuthoredScale").GetValue(adapter);
            Assert.That(authoredScale.x,Is.EqualTo(0.06654542f).Within(0.00001f),
                "Animated Orby must retain the prior authored world scale.");
            Component mouth=viewType.GetProperty("MouthAnimator")?.GetValue(view) as Component;
            Assert.That(mouth,Is.Not.Null);
            Assert.That(mouth.GetType().GetMethod("ResolveAuthoredMouths")?.Invoke(mouth,null),Is.EqualTo(true));
            object mouthSource=mouth.GetType().GetProperty("VoiceSource")?.GetValue(mouth);
            object playbackSource=voiceType.GetProperty("PlaybackSource")?.GetValue(voice);
            Assert.That(mouthSource,Is.SameAs(playbackSource));
            Assert.That(model.GetComponentsInChildren<AudioSource>(true),Is.Empty,
                "The visual model must not introduce a duplicate playback source.");

            GameObject legacy=SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(root=>root.name=="Orbi");
            Assert.That(legacy==null||!legacy.activeSelf,Is.True,
                "The legacy model must not remain visible beside animated Orby.");
        }

        [TestCase("Switch the UI panels to French","fr")]
        [TestCase("Change the interface to Spanish","es")]
        [TestCase("Put the menu in English","en")]
        [TestCase("Change the interface to Simplified Chinese","zh-CN")]
        [TestCase("Put the menu in Hindi","hi-IN")]
        public void LocalInterfaceCommandsResolveWithoutBackendRouting(string transcript,string languageCode)
        {
            Type router=RuntimeType("ThinkingSpace.UI.LocalInterfaceLanguageCommandRouter");
            MethodInfo parse=router?.GetMethod("Parse",BindingFlags.Public|BindingFlags.Static);
            Assert.That(parse,Is.Not.Null);
            object command=parse.Invoke(null,new object[]{transcript});
            Type commandType=command.GetType();
            Assert.That(commandType.GetField("LanguageCode")?.GetValue(command),Is.EqualTo(languageCode));
            Assert.That(commandType.GetProperty("IsRecognized")?.GetValue(command),Is.EqualTo(true));
        }

        [Test]
        public void ConversationLanguageRequestIsNotCapturedAsUiCommand()
        {
            Type router=RuntimeType("ThinkingSpace.UI.LocalInterfaceLanguageCommandRouter");
            object command=router.GetMethod("Parse",BindingFlags.Public|BindingFlags.Static)
                ?.Invoke(null,new object[]{"Speak French"});
            Assert.That(command,Is.Not.Null);
            Assert.That(command.GetType().GetProperty("IsRecognized")?.GetValue(command),Is.EqualTo(false));
        }

        [Test]
        public void PairingTransportRemainsExplicitlyUnavailable()
        {
            Type transportType=RuntimeType("ThinkingSpace.Pairing.UnavailableQuestPairingTransport");
            Assert.That(transportType,Is.Not.Null);
            object transport=Activator.CreateInstance(transportType);
            Assert.That(transportType.GetProperty("IsAvailable")?.GetValue(transport),Is.EqualTo(false));
            Assert.That(transportType.GetProperty("UnavailableMessageKey")?.GetValue(transport),
                Is.EqualTo("pairing.unavailable.body"));
        }

        [UnityTest]
        public IEnumerator VoiceLifecycleCancellationCompletesCallbacksAndReleasesPendingKeys()
        {
            Type directorType=RuntimeType("ThinkingSpace.Companion.OrbyVoiceDirector");
            Type stateType=RuntimeType("ThinkingSpace.Companion.OrbyConversationStateController");
            Assert.That(directorType,Is.Not.Null);Assert.That(stateType,Is.Not.Null);
            Component director=UnityEngine.Object.FindFirstObjectByType(directorType) as Component;
            bool created=director==null;
            GameObject owner=null;
            if(created)
            {
                owner=new GameObject("Voice lifecycle test");owner.AddComponent(stateType);
                director=owner.AddComponent(directorType);
            }
            MethodInfo cancelAll=directorType.GetMethod("CancelAllSpeech");
            MethodInfo setSession=directorType.GetMethod("SetActiveSession");
            MethodInfo speak=directorType.GetMethod("SpeakDynamicTracked");
            cancelAll.Invoke(director,null);setSession.Invoke(director,new object[]{"lifecycle-test"});
            int completions=0;
            Type priorityType=RuntimeType("ThinkingSpace.Companion.OrbyVoicePriority");
            object priority=Enum.Parse(priorityType,"AIResponse");Action<bool> completed=_=>completions++;
            object[] firstArguments={"First synthetic request","lifecycle-test",priority,completed,"dynamic_tts","neutral",null,null};
            object[] queuedArguments={"Second synthetic request","lifecycle-test",priority,completed,"dynamic_tts","neutral",null,null};
            string first=speak.Invoke(director,firstArguments) as string;
            string queued=speak.Invoke(director,queuedArguments) as string;
            Assert.That(first,Is.Not.Null);Assert.That(queued,Is.Not.Null);
            directorType.GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic)
                ?.Invoke(director,new object[]{false});
            Assert.That(completions,Is.EqualTo(2));
            Assert.That(directorType.GetProperty("PendingVoiceRequestCount")?.GetValue(director),Is.EqualTo(0));
            directorType.GetMethod("OnApplicationFocus",BindingFlags.Instance|BindingFlags.NonPublic)
                ?.Invoke(director,new object[]{true});
            object[] resumedArguments={"Second synthetic request","lifecycle-test",priority,new Action<bool>(_=>{}),"dynamic_tts","neutral",null,null};
            string resumed=speak.Invoke(director,resumedArguments) as string;
            Assert.That(resumed,Is.Not.Null,"Resume must not retain a stale duplicate cache key.");
            cancelAll.Invoke(director,null);
            if(created)UnityEngine.Object.Destroy(owner);
            yield return null;
        }

        private static Type RuntimeType(string fullName)
            =>AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly=>assembly.GetType(fullName,false))
                .FirstOrDefault(type=>type!=null);

        private static bool IsSceneObject(UnityEngine.Object value)
        {
            Component component=value as Component;
            return component!=null&&component.gameObject.scene.IsValid();
        }
    }
}
