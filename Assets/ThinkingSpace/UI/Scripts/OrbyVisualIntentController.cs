using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public enum OrbyVisualIntentKind{None,GenerateImage,CreateBarChart}
    public enum OrbyVisualIntentOutcome{None,ImageStarted,ChartCreated,Disabled,InvalidContext,Error}
    public readonly struct OrbyVisualIntent
    {
        public readonly OrbyVisualIntentKind Kind;public readonly string Prompt;
        public OrbyVisualIntent(OrbyVisualIntentKind kind,string prompt){Kind=kind;Prompt=prompt??string.Empty;}
        public bool IsRecognized=>Kind!=OrbyVisualIntentKind.None;
    }
    public static class OrbyVisualIntentRouter
    {
        public static OrbyVisualIntent Parse(string transcript)
        {
            string value=(transcript??string.Empty).Trim();
            Match image=Regex.Match(value,"^orby\\s*[,;:]?\\s*(?:please\\s+)?generate\\s+(?:an?\\s+)?image\\s+of\\s+(.+?)[.!?]*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(image.Success&&!string.IsNullOrWhiteSpace(image.Groups[1].Value))return new OrbyVisualIntent(OrbyVisualIntentKind.GenerateImage,image.Groups[1].Value.Trim().TrimEnd('.','!','?'));
            if(Regex.IsMatch(value,"^orby\\s*[,;:]?\\s*(?:please\\s+)?(?:create|make|show)\\s+(?:me\\s+)?(?:an?\\s+)?(?:editable\\s+)?bar\\s+chart\\s+(?:comparing|for|of)\\s+(?:(?:the|my)\\s+)?(?:three|3)\\s+(?:active\\s+)?ideas[.!?]*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return new OrbyVisualIntent(OrbyVisualIntentKind.CreateBarChart,value);
            return new OrbyVisualIntent(OrbyVisualIntentKind.None,string.Empty);
        }
    }

    [DisallowMultipleComponent]
    public sealed class OrbyVisualIntentController:MonoBehaviour
    {
        private Func<BrainstormSession> sessionProvider;private Func<string> selectedIdeaProvider;private Transform headset;
        public OrbyVisualIntentOutcome LastOutcome{get;private set;}=OrbyVisualIntentOutcome.None;
        public string LastStatusCode{get;private set;}="none";
        public GeneratedImagePanelView LastImagePanel{get;private set;}
        public EditableChartPanelView LastChartPanel{get;private set;}
        public int ImageRequestsStarted{get;private set;}
        public void Configure(Func<BrainstormSession> activeSessionProvider,Func<string> activeIdeaProvider,Transform headsetTransform)
        {sessionProvider=activeSessionProvider??throw new ArgumentNullException(nameof(activeSessionProvider));selectedIdeaProvider=activeIdeaProvider;headset=headsetTransform;}
        public bool CanHandle(string transcript)=>OrbyVisualIntentRouter.Parse(transcript).IsRecognized;
        public bool TryHandle(string transcript,bool requestImageImmediately=true)
        {
            OrbyVisualIntent intent=OrbyVisualIntentRouter.Parse(transcript);if(!intent.IsRecognized)return false;
            BrainstormSession session=sessionProvider?.Invoke();if(session==null){SetOutcome(OrbyVisualIntentOutcome.InvalidContext,"active_session_required");return true;}
            try
            {
                if(intent.Kind==OrbyVisualIntentKind.GenerateImage)return GenerateImage(intent,session,requestImageImmediately);
                return CreateBarChart(intent,session);
            }
            catch(InvalidOperationException){SetOutcome(OrbyVisualIntentOutcome.Disabled,"feature_unavailable");return true;}
            catch(ArgumentException){SetOutcome(OrbyVisualIntentOutcome.Error,"invalid_request");return true;}
        }
        private bool GenerateImage(OrbyVisualIntent intent,BrainstormSession session,bool requestImmediately)
        {
            string ideaId=ResolveIdeaId(session);if(string.IsNullOrWhiteSpace(ideaId)){SetOutcome(OrbyVisualIntentOutcome.InvalidContext,"active_idea_required");return true;}
            QuestEphemeralImageRuntime runtime=FindFirstObjectByType<QuestEphemeralImageRuntime>();if(runtime==null){SetOutcome(OrbyVisualIntentOutcome.Disabled,"image_generation_disabled");return true;}
            Pose pose=PanelPose();LastImagePanel=runtime.GenerateForIdea(session.SessionId,ideaId,intent.Prompt,pose.position,pose.rotation,requestImmediately);ImageRequestsStarted+=requestImmediately?1:0;SetOutcome(OrbyVisualIntentOutcome.ImageStarted,requestImmediately?"image_request_started":"image_panel_bound");return true;
        }
        private bool CreateBarChart(OrbyVisualIntent intent,BrainstormSession session)
        {
            if(session.Ideas==null||session.Ideas.Count<3){SetOutcome(OrbyVisualIntentOutcome.InvalidContext,"three_ideas_required");return true;}
            int first=session.Ideas.Count-3;var points=new List<EditableChartPoint>();for(int i=first;i<session.Ideas.Count;i++){BrainstormIdeaRecord idea=session.Ideas[i];if(idea==null||string.IsNullOrWhiteSpace(idea.IdeaId)){SetOutcome(OrbyVisualIntentOutcome.InvalidContext,"three_ideas_required");return true;}points.Add(new EditableChartPoint{Label=string.IsNullOrWhiteSpace(idea.Title)?"Idea "+(i-first+1):idea.Title,Value=1f});}
            var root=new GameObject("Editable Bar Chart Panel",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));Pose pose=PanelPose();root.transform.SetPositionAndRotation(pose.position,pose.rotation);root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            LastChartPanel=root.AddComponent<EditableChartPanelView>();string chartId=Guid.NewGuid().ToString("N");LastChartPanel.Bind(new EditableChartRevision{Schema="ChartRevisionV1",Action="create_editable_chart",ProviderCall=false,OriginalSpokenRequest=intent.Prompt,ChartId=chartId,RevisionId=Guid.NewGuid().ToString("N"),SessionId=session.SessionId,IdeaId=ResolveIdeaId(session),Title="Three ideas (equal baseline)",Type=EditableChartType.Bar,Points=points});SetOutcome(OrbyVisualIntentOutcome.ChartCreated,"chart_created_offline");return true;
        }
        private string ResolveIdeaId(BrainstormSession session){string selected=selectedIdeaProvider?.Invoke();if(!string.IsNullOrWhiteSpace(selected)&&session.Ideas.Exists(item=>item!=null&&item.IdeaId==selected))return selected;for(int i=session.Ideas.Count-1;i>=0;i--)if(session.Ideas[i]!=null&&!string.IsNullOrWhiteSpace(session.Ideas[i].IdeaId))return session.Ideas[i].IdeaId;return string.Empty;}
        private Pose PanelPose(){if(headset==null)return new Pose(Vector3.forward*.8f,Quaternion.identity);Vector3 forward=Vector3.ProjectOnPlane(headset.forward,Vector3.up).normalized;if(forward.sqrMagnitude<.01f)forward=headset.forward;Vector3 position=headset.position+forward*.8f;return new Pose(position,Quaternion.LookRotation(position-headset.position,Vector3.up));}
        private void SetOutcome(OrbyVisualIntentOutcome outcome,string code){LastOutcome=outcome;LastStatusCode=code;}
    }
}
