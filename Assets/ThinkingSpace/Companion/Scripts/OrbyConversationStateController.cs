using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    public enum OrbyConversationState { Idle, Listening, SpeechActive, Paused, Thinking, Speaking, Reviewing, Error }

    [DisallowMultipleComponent]
    public sealed class OrbyConversationStateController : MonoBehaviour
    {
        public OrbyConversationState State { get; private set; } = OrbyConversationState.Idle;
        public string ErrorMessage { get; private set; } = string.Empty;
        public event Action<OrbyConversationState, OrbyConversationState> StateChanged;
        public event Action ListeningOffRequested;
        private readonly HashSet<string> pauseReasons=new HashSet<string>(StringComparer.Ordinal);
        private OrbyConversationState stateBeforePause=OrbyConversationState.Idle;
        public bool IsPaused => pauseReasons.Count>0;

        public void SetState(OrbyConversationState next)
        {
            if(IsPaused&&next!=OrbyConversationState.Paused)
            {stateBeforePause=next;return;}
            if (next == State) return;
            OrbyConversationState previous = State;
            State = next;
            if (next != OrbyConversationState.Error) ErrorMessage = string.Empty;
            StateChanged?.Invoke(previous, next);
        }

        public void AcquirePause(string reason)
        {
            if(string.IsNullOrWhiteSpace(reason))return;
            if(pauseReasons.Count==0)stateBeforePause=State==OrbyConversationState.Paused
                ?OrbyConversationState.Idle:State;
            if(!pauseReasons.Add(reason))return;
            SetState(OrbyConversationState.Paused);
        }

        public void ReleasePause(string reason)
        {
            if(string.IsNullOrWhiteSpace(reason)||!pauseReasons.Remove(reason)||pauseReasons.Count>0)return;
            OrbyConversationState restore=stateBeforePause;
            stateBeforePause=OrbyConversationState.Idle;
            SetState(restore==OrbyConversationState.Paused?OrbyConversationState.Idle:restore);
        }

        public void SetError(string message)
        {
            ErrorMessage = message ?? string.Empty;
            SetState(OrbyConversationState.Error);
        }

        // Future thumbs-down integration calls this regardless of the current Orby state.
        // It does not manipulate the microphone or existing gesture controller in this milestone.
        public void TurnListeningOffFromAnyState()
        {
            ListeningOffRequested?.Invoke();
            SetState(OrbyConversationState.Idle);
        }
    }
}
