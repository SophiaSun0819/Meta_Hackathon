using TMPro;
using UnityEngine;

namespace ThinkingSpace.UI
{
    public class StatusMessage : MonoBehaviour
    {
        [SerializeField] private TMP_Text target;
        [SerializeField] private string localizationKey;
        protected virtual void OnEnable(){ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=HandleChanged;Refresh();}
        protected virtual void OnDisable()=>ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=HandleChanged;
        public void Configure(TMP_Text text,string key){target=text;localizationKey=key;Refresh();}
        public void SetLocalizationKey(string key){localizationKey=key;Refresh();}
        public void SetText(string value){if(target!=null)target.text=value??string.Empty;}
        public void Refresh(){if(target!=null&&!string.IsNullOrWhiteSpace(localizationKey))target.text=ThinkingSpaceLocalizationController.Get(localizationKey);}
        private void HandleChanged(string _)=>Refresh();
    }
    public sealed class ErrorMessage : StatusMessage { }
}
