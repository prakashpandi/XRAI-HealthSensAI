using System;
using Meta.WitAi.TTS.Utilities;
using UnityEngine;

public class AssistantController : MonoBehaviour
{
    public enum AssistantState
    {
        Idle,
        Listening,
        Processing,
        Speaking
    };
    public AssistantState currentState = AssistantState.Idle;
    public TTSSpeaker speaker;

    public void Say(string message)
    {
        currentState = AssistantState.Speaking;
        speaker.Speak(message);
        Debug.Log("Assistant says: " + message);
    }

    void Update()
    {
        switch (currentState)
        {
            case AssistantState.Idle:
                // Handle idle state
                break;
            case AssistantState.Listening:
                // Handle listening state
                break;
            case AssistantState.Processing:
                // Handle processing state
                break;
            case AssistantState.Speaking:
                // Handle speaking state
                break;
        }
    }
}