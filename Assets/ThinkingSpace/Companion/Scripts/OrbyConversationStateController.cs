using System;
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

        public void SetState(OrbyConversationState next)
        {
            if (next == State) return;
            OrbyConversationState previous = State;
            State = next;
            if (next != OrbyConversationState.Error) ErrorMessage = string.Empty;
            StateChanged?.Invoke(previous, next);
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
