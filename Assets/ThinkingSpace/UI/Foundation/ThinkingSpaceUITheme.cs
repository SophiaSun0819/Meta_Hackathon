using UnityEngine;

namespace ThinkingSpace.UI
{
    [CreateAssetMenu(menuName = "Thinking Space/UI Theme", fileName = "ThinkingSpaceUITheme")]
    public sealed class ThinkingSpaceUITheme : ScriptableObject
    {
        private static ThinkingSpaceUITheme cached;
        [Header("SlimUI References")]
        [SerializeField] private Sprite panelBackground, panelFrame, buttonNormal, buttonHover, buttonPressed, buttonSelected, navigationArrow;
        [Header("Palette")]
        [SerializeField] private Color panelColor = new Color(.025f,.045f,.075f,.96f);
        [SerializeField] private Color primaryColor = new Color(.08f,.52f,.72f,1f);
        [SerializeField] private Color secondaryColor = new Color(.11f,.2f,.32f,1f);
        [SerializeField] private Color selectedColor = new Color(1f,.62f,.16f,1f);
        [SerializeField] private Color disabledColor = new Color(.18f,.21f,.25f,.72f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color borderColor = new Color(.08f,.42f,.62f,.82f);
        [SerializeField, Min(32f)] private float minimumHitHeight = 72f;
        [SerializeField, Min(8f)] private float safePadding = 28f;
        public Sprite PanelBackground=>panelBackground; public Sprite PanelFrame=>panelFrame;
        public Sprite ButtonNormal=>buttonNormal; public Sprite ButtonHover=>buttonHover; public Sprite ButtonPressed=>buttonPressed;
        public Sprite ButtonSelected=>buttonSelected; public Sprite NavigationArrow=>navigationArrow;
        public Color PanelColor=>panelColor; public Color PrimaryColor=>primaryColor; public Color SecondaryColor=>secondaryColor;
        public Color SelectedColor=>selectedColor; public Color DisabledColor=>disabledColor; public Color TextColor=>textColor;
        public Color BorderColor=>borderColor; public float MinimumHitHeight=>minimumHitHeight; public float SafePadding=>safePadding;
        public static ThinkingSpaceUITheme LoadDefault(){if(cached==null)cached=Resources.Load<ThinkingSpaceUITheme>("ThinkingSpaceUITheme");return cached;}
        public static void ClearCachedThemeForTests()=>cached=null;
    }
}
