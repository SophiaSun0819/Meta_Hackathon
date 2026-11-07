namespace ThinkingSpace.UI
{
    /// <summary>Separates physical pointer cycles from menu toggle transitions.</summary>
    public sealed class PalmMenuActivationGate
    {
        private int interactionGeneration;
        private int consumedGeneration;
        private bool pointerHeld;
        private float lastBeginTime = -10f;
        private float lastActivationTime = -10f;

        public int InteractionGeneration => interactionGeneration;
        public int ConsumedGeneration => consumedGeneration;
        public bool PointerHeld => pointerHeld;

        public bool BeginInteraction(float now, float duplicatePressWindow = 0.05f)
        {
            if (pointerHeld || now - lastBeginTime < duplicatePressWindow) return false;
            pointerHeld = true;
            lastBeginTime = now;
            interactionGeneration++;
            return true;
        }

        public void EndInteraction() => pointerHeld = false;

        public bool TryConsume(float now, float activationCooldown)
        {
            if (interactionGeneration == 0 || interactionGeneration == consumedGeneration
                || now - lastActivationTime < activationCooldown) return false;
            consumedGeneration = interactionGeneration;
            lastActivationTime = now;
            return true;
        }

        public void BeginDevelopmentInteraction(float now)
        {
            pointerHeld = false;
            lastBeginTime = now;
            interactionGeneration++;
        }
    }
}
