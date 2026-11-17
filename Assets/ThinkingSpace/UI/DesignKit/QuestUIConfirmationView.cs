using UnityEngine; using UnityEngine.UI;
namespace ThinkingSpace.UI.DesignKit { public sealed class QuestUIConfirmationView:MonoBehaviour { [SerializeField] private Button primary,secondary; public Button Primary=>primary; public Button Secondary=>secondary; public void Configure(Button a,Button b){primary=a;secondary=b;} } }
