using System;

namespace ThinkingSpace.Onboarding
{
    [Serializable]
    public sealed class TutorialPage
    {
        public string PageId;
        public string Title;
        public string Body;
        public string IllustrationKey;
        public TutorialPage(string id, string title, string body, string illustrationKey)
        { PageId = id; Title = title; Body = body; IllustrationKey = illustrationKey; }
    }

    public static class OnboardingTutorialCatalog
    {
        public static readonly TutorialPage[] Pages =
        {
            new TutorialPage("open-menu", "Open the menu", "Close a fist, then open the same palm upward.", "gesture-menu"),
            new TutorialPage("start-session", "Start a session", "Choose Start New Session before recording ideas.", "menu-session"),
            new TutorialPage("start-listening", "Start listening", "Hold a thumbs-up with the selected recording hand.", "gesture-thumb-up"),
            new TutorialPage("stop-listening", "Stop listening", "Hold a thumbs-down to finish the recording.", "gesture-thumb-down"),
            new TutorialPage("idea-bubbles", "Explore ideas", "Use Meta pinch and poke interactions to work with idea bubbles.", "idea-bubbles")
        };
    }
}
