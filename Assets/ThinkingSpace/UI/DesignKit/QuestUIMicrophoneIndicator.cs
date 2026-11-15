using TMPro; using UnityEngine;
namespace ThinkingSpace.UI.DesignKit { public sealed class QuestUIMicrophoneIndicator:MonoBehaviour { [SerializeField] private TMP_Text stateLabel; public TMP_Text StateLabel=>stateLabel; public void Configure(TMP_Text value)=>stateLabel=value; public void SetListening(bool state){if(stateLabel!=null)stateLabel.text=state?"LISTENING":"MIC READY";} } }
