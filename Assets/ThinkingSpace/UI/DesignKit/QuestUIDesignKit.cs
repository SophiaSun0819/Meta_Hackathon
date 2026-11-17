using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ThinkingSpace.UI.DesignKit
{
    public static class QuestUIDesignTokens
    {
        public const float CanvasWidth=960f,CanvasHeight=640f,WorldScale=.00075f;
        public const float HeaderHeight=110f,ContentHeight=426f,FooterHeight=104f;
        public static readonly Color Panel=new Color32(6,19,31,247);
        public static readonly Color Raised=new Color32(16,38,59,255);
        public static readonly Color Cyan=new Color32(53,201,244,255);
        public static readonly Color Hover=new Color32(27,71,99,255);
        public static readonly Color Pressed=new Color32(34,103,132,255);
        public static readonly Color Disabled=new Color32(18,35,49,210);
        public static readonly Color Orange=new Color32(255,173,72,255);
        public static readonly Color Mint=new Color32(121,230,202,255);
        public static readonly Color Coral=new Color32(255,125,117,255);
        public static readonly Color Text=new Color32(246,251,255,255);
        public static readonly Color Muted=new Color32(169,189,202,255);
    }

    public enum QuestUIComponentKind
    { PanelShell,Header,Footer,ActionCard,SegmentedControl,Status,Confirmation,IdeaDetail,OrbySubtitle,MicrophoneIndicator }

}
