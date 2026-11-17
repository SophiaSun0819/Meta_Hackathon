using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThinkingSpace.Tests.PlayMode
{
    public sealed class QuestUIDesignKitPlayModeTests
    {
        [UnityTest] public IEnumerator ActionCardInvokesExactlyOnce()
        {
            Type type=RuntimeType("ThinkingSpace.UI.DesignKit.QuestUIActionCard");Assert.That(type,Is.Not.Null);
            GameObject root=new GameObject("Action",typeof(RectTransform),typeof(Image),typeof(Button));Component card=root.AddComponent(type);Button button=root.GetComponent<Button>();
            type.GetMethod("Configure")?.Invoke(card,new object[]{"test",button,null});int count=0;UnityEngine.Events.UnityAction action=()=>count++;
            type.GetMethod("SetAction")?.Invoke(card,new object[]{action});type.GetMethod("SetAction")?.Invoke(card,new object[]{action});button.onClick.Invoke();yield return null;
            Assert.That(count,Is.EqualTo(1));UnityEngine.Object.Destroy(root);
        }

        [UnityTest] public IEnumerator MicrophoneIndicatorNeverDisplaysRecognizedWords()
        {
            Type type=RuntimeType("ThinkingSpace.UI.DesignKit.QuestUIMicrophoneIndicator");Assert.That(type,Is.Not.Null);
            GameObject root=new GameObject("Mic",typeof(RectTransform));Component indicator=root.AddComponent(type);
            Type textType=RuntimeType("TMPro.TextMeshProUGUI");Assert.That(textType,Is.Not.Null);
            GameObject label=new GameObject("State",typeof(RectTransform));Component text=label.AddComponent(textType);label.transform.SetParent(root.transform,false);
            type.GetMethod("Configure")?.Invoke(indicator,new object[]{text});type.GetMethod("SetListening")?.Invoke(indicator,new object[]{true});yield return null;
            object state=type.GetProperty("StateLabel")?.GetValue(indicator);string value=state?.GetType().GetProperty("text")?.GetValue(state) as string;
            Assert.That(value,Is.EqualTo("LISTENING"));UnityEngine.Object.Destroy(root);
        }

        private static Type RuntimeType(string fullName)=>AppDomain.CurrentDomain.GetAssemblies().Select(assembly=>assembly.GetType(fullName,false)).FirstOrDefault(value=>value!=null);
    }
}
