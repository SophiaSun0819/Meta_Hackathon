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

        [TestCase("Switch the UI panels to French","fr")]
        [TestCase("Change the interface to Spanish","es")]
        [TestCase("Put the menu in English","en")]
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
