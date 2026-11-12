using System;
using TMPro;
using UnityEngine;
using ThinkingSpace.Speech;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class AirSwipeGestureRecognizer : MonoBehaviour
    {
        public enum SwipeDirection { Left, Right }
        public event Action<SwipeDirection> SwipeAccepted;

        private sealed class FingerChain
        {
            public Transform proximal, intermediate, distal, tip;
            public bool Resolved => proximal != null && intermediate != null && distal != null && tip != null;
        }

        private sealed class SwipeHand
        {
            public OVRHand hand;
            public OVRSkeleton skeleton;
            public Transform wrist;
            public readonly FingerChain index=new FingerChain();
            public readonly FingerChain middle=new FingerChain();
            public readonly FingerChain ring=new FingerChain();
            public readonly FingerChain pinky=new FingerChain();
            public bool jointsResolved,trackingCandidate,requiresNeutral;
            public float startedAt,neutralSince;
            public Vector2 start;
            public string reason="Waiting";
            public int extended;
        }

        private readonly SwipeHand left=new SwipeHand();
        private readonly SwipeHand right=new SwipeHand();
        private Transform headset;
        private BrainstormMenuController menu;
        private IdeaBubbleManager bubbles;
        private ThumbRecordingGestureController recording;
        private float travel=.18f,minDuration=.25f,maxDuration=.65f,minSpeed=.4f,dominance=2f,cooldown=.5f;
        private float extendedRatio=.82f,lastAccepted=-100f;
        private string lastDirection="—";
        private int requiredExtended=3;
        private Vector3 workspaceRight=Vector3.right;
        private bool configured,showDiagnostics;
        private Canvas diagnosticCanvas;
        private TMP_Text diagnosticText;
        public string LastDiagnostic { get; private set; }="Not configured";

        public void Configure(OVRHand leftHand,OVRSkeleton leftSkeleton,OVRHand rightHand,
            OVRSkeleton rightSkeleton,Transform centerEye,BrainstormMenuController menuController,
            IdeaBubbleManager bubbleManager,ThumbRecordingGestureController recordingController,
            float horizontalTravel,float minimumDuration,float maximumDuration,float minimumSpeed,
            float horizontalDominance,float cooldownSeconds,float fingerExtensionRatio,
            int requiredExtendedFingerCount,bool diagnostics)
        {
            left.hand=leftHand;left.skeleton=leftSkeleton;right.hand=rightHand;right.skeleton=rightSkeleton;
            headset=centerEye;menu=menuController;bubbles=bubbleManager;recording=recordingController;
            travel=Mathf.Max(.05f,horizontalTravel);minDuration=Mathf.Max(.05f,minimumDuration);
            maxDuration=Mathf.Max(minDuration+.01f,maximumDuration);minSpeed=Mathf.Max(.05f,minimumSpeed);
            dominance=Mathf.Max(1f,horizontalDominance);cooldown=Mathf.Max(0f,cooldownSeconds);
            extendedRatio=Mathf.Clamp(fingerExtensionRatio,.5f,.98f);
            requiredExtended=Mathf.Clamp(requiredExtendedFingerCount,2,4);showDiagnostics=diagnostics;
            Vector3 forward=headset!=null?Vector3.ProjectOnPlane(headset.forward,Vector3.up):Vector3.forward;
            if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
            workspaceRight=Vector3.Cross(Vector3.up,forward.normalized).normalized;
            configured=true;
            if(showDiagnostics)BuildDiagnostics();
        }

        private void Update()
        {
            if(!configured)return;
            bool blocked=IsSuppressed();
            UpdateHand(left,blocked);UpdateHand(right,blocked);
            string direction=Time.unscaledTime-lastAccepted<.75f?lastDirection:"—";
            LastDiagnostic="Swipe "+direction+" | cooldown "+Mathf.Max(0f,cooldown-(Time.unscaledTime-lastAccepted)).ToString("F2")
                +"\nL "+left.reason+" · open "+left.extended+"/4"
                +"\nR "+right.reason+" · open "+right.extended+"/4";
            UpdateDiagnostics();
        }

        private bool IsSuppressed()
        {
            return ShouldSuppress(menu!=null&&menu.IsMenuVisible,
                menu!=null&&menu.IsMenuInteractionBusy,recording!=null&&recording.IsRecordingGestureBusy,
                bubbles!=null&&bubbles.DirectMode!=IdeaBubbleManager.DirectManipulationMode.None,
                bubbles!=null&&bubbles.IsPanelDragging);
        }

        public static bool ShouldSuppress(bool menuVisible,bool menuInteraction,bool recordingGesture,
            bool bubbleManipulation,bool panelDrag)
            =>menuVisible||menuInteraction||recordingGesture||bubbleManipulation||panelDrag;

        private void UpdateHand(SwipeHand hand,bool blocked)
        {
            if(blocked){hand.requiresNeutral=true;hand.neutralSince=0f;
                ResetCandidate(hand,"Suppressed by active interaction");return;}
            if(!Resolve(hand)){hand.requiresNeutral=true;hand.neutralSince=0f;
                ResetCandidate(hand,"Tracking or joints unavailable");return;}
            hand.extended=CountExtended(hand);
            bool open=hand.extended>=requiredExtended;
            if(hand.requiresNeutral)
            {
                if(open){hand.neutralSince=0f;hand.reason="Rearm: relax open hand";return;}
                if(hand.neutralSince<=0f)hand.neutralSince=Time.unscaledTime;
                if(Time.unscaledTime-hand.neutralSince<.12f){hand.reason="Neutral rearm";return;}
                hand.requiresNeutral=false;hand.neutralSince=0f;hand.reason="Armed";return;
            }
            if(!open){ResetCandidate(hand,"Need "+requiredExtended+" extended fingers");return;}
            Vector2 point=CompensatedPoint(hand.wrist.position);
            if(!hand.trackingCandidate)
            {
                hand.trackingCandidate=true;hand.startedAt=Time.unscaledTime;hand.start=point;
                hand.reason="Tracking open-hand stroke";return;
            }
            float duration=Time.unscaledTime-hand.startedAt;
            Vector2 delta=point-hand.start;
            SwipeEvaluation evaluation=Evaluate(delta,duration,travel,minDuration,maxDuration,minSpeed,dominance);
            hand.reason=evaluation.reason+" · x "+delta.x.ToString("F2")+" y "+delta.y.ToString("F2")
                +" t "+duration.ToString("F2");
            if(evaluation.accepted&&Time.unscaledTime-lastAccepted>=cooldown)
            {
                lastAccepted=Time.unscaledTime;hand.requiresNeutral=true;hand.trackingCandidate=false;
                SwipeDirection direction=delta.x<0f?SwipeDirection.Left:SwipeDirection.Right;
                lastDirection=direction==SwipeDirection.Left?"← LEFT":"RIGHT →";
                hand.reason="ACCEPTED "+direction+(direction==SwipeDirection.Left?" ←":" →");
                SwipeAccepted?.Invoke(direction);
                SwipeHand other=ReferenceEquals(hand,left)?right:left;
                other.requiresNeutral=true;other.trackingCandidate=false;
            }
            else if(duration>maxDuration)
            {
                hand.trackingCandidate=false;hand.requiresNeutral=true;hand.reason="Rejected: duration exceeded";
            }
        }

        private Vector2 CompensatedPoint(Vector3 wristPosition)
        {
            Vector3 relative=wristPosition-(headset!=null?headset.position:Vector3.zero);
            return new Vector2(Vector3.Dot(relative,workspaceRight),Vector3.Dot(relative,Vector3.up));
        }

        public static Vector3 CompensatedDelta(Vector3 wristStart,Vector3 wristEnd,
            Vector3 headsetStart,Vector3 headsetEnd)
            =>(wristEnd-wristStart)-(headsetEnd-headsetStart);

        private bool Resolve(SwipeHand hand)
        {
            if(hand.hand==null||hand.skeleton==null||!hand.hand.IsTracked
                ||!hand.skeleton.IsDataValid||!hand.skeleton.IsDataHighConfidence)return false;
            if(!hand.jointsResolved)ResolveJoints(hand);
            return hand.jointsResolved;
        }

        private static void ResetCandidate(SwipeHand hand,string reason)
        {hand.trackingCandidate=false;hand.reason=reason;hand.extended=0;}

        private int CountExtended(SwipeHand hand)
        {
            int count=0;if(IsExtended(hand.index))count++;if(IsExtended(hand.middle))count++;
            if(IsExtended(hand.ring))count++;if(IsExtended(hand.pinky))count++;return count;
        }

        private bool IsExtended(FingerChain finger)
        {
            float path=Vector3.Distance(finger.proximal.position,finger.intermediate.position)
                +Vector3.Distance(finger.intermediate.position,finger.distal.position)
                +Vector3.Distance(finger.distal.position,finger.tip.position);
            return path>.0001f&&Vector3.Distance(finger.proximal.position,finger.tip.position)/path>=extendedRatio;
        }

        private static void ResolveJoints(SwipeHand hand)
        {
            hand.wrist=null;
            bool xr=hand.skeleton.GetSkeletonType()==OVRSkeleton.SkeletonType.XRHandLeft
                ||hand.skeleton.GetSkeletonType()==OVRSkeleton.SkeletonType.XRHandRight;
            foreach(OVRBone bone in hand.skeleton.Bones)
            {
                if(bone?.Transform==null)continue;
                OVRSkeleton.BoneId id=bone.Id;Transform t=bone.Transform;
                if(xr&&id==OVRSkeleton.BoneId.XRHand_Wrist)hand.wrist=t;
                else if(!xr&&id==OVRSkeleton.BoneId.Hand_WristRoot)hand.wrist=t;
                AssignFinger(hand.index,id,t,xr,OVRSkeleton.BoneId.XRHand_IndexProximal,OVRSkeleton.BoneId.XRHand_IndexIntermediate,OVRSkeleton.BoneId.XRHand_IndexDistal,OVRSkeleton.BoneId.XRHand_IndexTip,OVRSkeleton.BoneId.Hand_Index1,OVRSkeleton.BoneId.Hand_Index2,OVRSkeleton.BoneId.Hand_Index3,OVRSkeleton.BoneId.Hand_IndexTip);
                AssignFinger(hand.middle,id,t,xr,OVRSkeleton.BoneId.XRHand_MiddleProximal,OVRSkeleton.BoneId.XRHand_MiddleIntermediate,OVRSkeleton.BoneId.XRHand_MiddleDistal,OVRSkeleton.BoneId.XRHand_MiddleTip,OVRSkeleton.BoneId.Hand_Middle1,OVRSkeleton.BoneId.Hand_Middle2,OVRSkeleton.BoneId.Hand_Middle3,OVRSkeleton.BoneId.Hand_MiddleTip);
                AssignFinger(hand.ring,id,t,xr,OVRSkeleton.BoneId.XRHand_RingProximal,OVRSkeleton.BoneId.XRHand_RingIntermediate,OVRSkeleton.BoneId.XRHand_RingDistal,OVRSkeleton.BoneId.XRHand_RingTip,OVRSkeleton.BoneId.Hand_Ring1,OVRSkeleton.BoneId.Hand_Ring2,OVRSkeleton.BoneId.Hand_Ring3,OVRSkeleton.BoneId.Hand_RingTip);
                AssignFinger(hand.pinky,id,t,xr,OVRSkeleton.BoneId.XRHand_LittleProximal,OVRSkeleton.BoneId.XRHand_LittleIntermediate,OVRSkeleton.BoneId.XRHand_LittleDistal,OVRSkeleton.BoneId.XRHand_LittleTip,OVRSkeleton.BoneId.Hand_Pinky1,OVRSkeleton.BoneId.Hand_Pinky2,OVRSkeleton.BoneId.Hand_Pinky3,OVRSkeleton.BoneId.Hand_PinkyTip);
            }
            hand.jointsResolved=hand.wrist!=null&&hand.index.Resolved&&hand.middle.Resolved&&hand.ring.Resolved&&hand.pinky.Resolved;
        }

        private static void AssignFinger(FingerChain f,OVRSkeleton.BoneId id,Transform t,bool xr,
            OVRSkeleton.BoneId xp,OVRSkeleton.BoneId xi,OVRSkeleton.BoneId xd,OVRSkeleton.BoneId xt,
            OVRSkeleton.BoneId lp,OVRSkeleton.BoneId li,OVRSkeleton.BoneId ld,OVRSkeleton.BoneId lt)
        {
            if(id==(xr?xp:lp))f.proximal=t;else if(id==(xr?xi:li))f.intermediate=t;
            else if(id==(xr?xd:ld))f.distal=t;else if(id==(xr?xt:lt))f.tip=t;
        }

        public readonly struct SwipeEvaluation
        {
            public readonly bool accepted;public readonly string reason;
            public SwipeEvaluation(bool value,string message){accepted=value;reason=message;}
        }

        public static SwipeEvaluation Evaluate(Vector2 delta,float duration,float requiredTravel,
            float minimumDuration,float maximumDuration,float minimumSpeed,float requiredDominance)
        {
            float horizontal=Mathf.Abs(delta.x),vertical=Mathf.Abs(delta.y);
            if(duration<minimumDuration)return new SwipeEvaluation(false,"Building duration");
            if(duration>maximumDuration)return new SwipeEvaluation(false,"Duration too long");
            if(horizontal<requiredTravel)return new SwipeEvaluation(false,"Horizontal travel too short");
            if(horizontal/Mathf.Max(.001f,vertical)<requiredDominance)return new SwipeEvaluation(false,"Vertical/diagonal motion");
            if(horizontal/Mathf.Max(.001f,duration)<minimumSpeed)return new SwipeEvaluation(false,"Speed too low");
            return new SwipeEvaluation(true,"Accepted");
        }

        private void BuildDiagnostics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GameObject root=new GameObject("Air Swipe Diagnostics");root.transform.SetParent(transform,false);
            diagnosticCanvas=root.AddComponent<Canvas>();diagnosticCanvas.renderMode=RenderMode.WorldSpace;
            RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(360f,105f);rect.localScale=Vector3.one*.00045f;
            GameObject textObject=new GameObject("Status");textObject.transform.SetParent(root.transform,false);
            diagnosticText=textObject.AddComponent<TextMeshProUGUI>();diagnosticText.font=TMP_Settings.defaultFontAsset;
            diagnosticText.fontSize=28f;diagnosticText.color=Color.white;diagnosticText.alignment=TextAlignmentOptions.Center;
            RectTransform textRect=textObject.GetComponent<RectTransform>();textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=textRect.offsetMax=Vector2.zero;
#endif
        }

        private void UpdateDiagnostics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(diagnosticCanvas==null||headset==null)return;
            diagnosticCanvas.transform.position=headset.position+headset.forward*.55f+Vector3.down*.20f;
            diagnosticCanvas.transform.rotation=Quaternion.LookRotation(diagnosticCanvas.transform.position-headset.position,Vector3.up);
            diagnosticText.text=LastDiagnostic;
#endif
        }
    }
}
