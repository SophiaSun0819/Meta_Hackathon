using UnityEngine;
namespace ThinkingSpace.UI.DesignKit { public sealed class QuestUIDesignComponent:MonoBehaviour { [SerializeField] private QuestUIComponentKind kind; public QuestUIComponentKind Kind=>kind; public void Configure(QuestUIComponentKind value)=>kind=value; } }
