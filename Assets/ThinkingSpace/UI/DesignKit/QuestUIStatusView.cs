using TMPro; using UnityEngine;
namespace ThinkingSpace.UI.DesignKit { public sealed class QuestUIStatusView:MonoBehaviour { [SerializeField] private TMP_Text title,body,state; public void SetStatus(string a,string b,string c){if(title!=null)title.text=a;if(body!=null)body.text=b;if(state!=null)state.text=c;} public void Configure(TMP_Text a,TMP_Text b,TMP_Text c){title=a;body=b;state=c;} } }
