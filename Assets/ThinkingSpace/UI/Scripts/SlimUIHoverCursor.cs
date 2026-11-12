using UnityEngine;
using UnityEngine.EventSystems;

namespace ThinkingSpace.UI
{
    public sealed class SlimUIHoverCursor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject cursorVisual;

        public void Configure(GameObject visual)
        {
            cursorVisual = visual;
            if (cursorVisual != null) cursorVisual.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (cursorVisual != null) cursorVisual.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (cursorVisual != null) cursorVisual.SetActive(false);
        }
    }
}
