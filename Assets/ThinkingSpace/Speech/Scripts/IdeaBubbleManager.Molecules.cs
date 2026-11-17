using System.Collections.Generic;
using ThinkingSpace.Companion;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    // Molecule combine gestures and visuals.
    // - Grab one bubble in each hand and hold them touching for about a second to fuse them.
    // - A molecule is a larger center bubble with up to three smaller source bubbles bonded around it.
    // - Moving the center carries its sources; pulling a source far enough away breaks its bond.
    // Data and backend calls live in MoleculeCombine.cs; this file only handles hands and visuals.
    public sealed partial class IdeaBubbleManager
    {
        [Header("Molecule combine")]
        [SerializeField, Range(0f, 0.04f)] private float fuseContactGap = 0.015f;
        [SerializeField, Range(0.5f, 2f)] private float fuseHoldDuration = 1.0f;
        [SerializeField, Range(0.5f, 3f)] private float fuseCooldown = 1.2f;
        [SerializeField, Range(0.4f, 0.9f)] private float sourceScale = 0.65f;
        // 0 = sources touch the center. The bond line only appears while a source is pulled away.
        [SerializeField, Range(0f, 0.06f)] private float bondGap = 0f;
        [SerializeField, Range(0.05f, 0.3f)] private float bondBreakStretch = 0.12f;
        [SerializeField, Range(0.05f, 0.2f)] private float rejectionPushDistance = 0.10f;

        private MoleculeCombineCoordinator moleculeCoordinator;
        private readonly Dictionary<string, Vector3> sourceOffsets = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, LineRenderer> bonds = new Dictionary<string, LineRenderer>();
        private readonly HashSet<string> fusingIdeaIds = new HashSet<string>();
        private readonly Dictionary<string, Vector3> fuseReturnPositions = new Dictionary<string, Vector3>();
        private Material bondMaterial, stretchedBondMaterial;
        private string secondIdeaId = string.Empty;
        private BubbleInteractionHand secondOwner = BubbleInteractionHand.Unknown;
        private Vector3 secondStartPosition;
        private float secondElapsed, secondReleaseTimer, fuseTimer, fuseCooldownRemaining;
        private bool secondGliding;

        public string SecondGrabbedIdeaId => secondIdeaId;
        public float FuseProgress => fuseHoldDuration > 0f ? Mathf.Clamp01(fuseTimer / fuseHoldDuration) : 0f;
        public string LastMoleculeEvent { get; private set; } = "None";

        private void ConfigureMolecules()
        {
            if (moleculeCoordinator == null) moleculeCoordinator = GetComponent<MoleculeCombineCoordinator>();
            if (moleculeCoordinator == null) moleculeCoordinator = gameObject.AddComponent<MoleculeCombineCoordinator>();
            moleculeCoordinator.MoleculeChanged -= HandleMoleculeChanged;
            moleculeCoordinator.MoleculeChanged += HandleMoleculeChanged;
            moleculeCoordinator.MoleculeRequestFailed -= HandleMoleculeRequestFailed;
            moleculeCoordinator.MoleculeRequestFailed += HandleMoleculeRequestFailed;
            if (bondMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                bondMaterial = new Material(shader) { color = new Color(.85f, .95f, 1f, .75f) };
                stretchedBondMaterial = new Material(shader) { color = new Color(1f, .45f, .25f, .9f) };
            }
        }

        private void UnsubscribeMolecules()
        {
            if (moleculeCoordinator == null) return;
            moleculeCoordinator.MoleculeChanged -= HandleMoleculeChanged;
            moleculeCoordinator.MoleculeRequestFailed -= HandleMoleculeRequestFailed;
        }

        private BrainstormSession ActiveMoleculeSession
        {
            get
            {
                BrainstormSession session = Store?.ActiveSession;
                return session != null && session.SessionId == activeSessionId ? session : null;
            }
        }

        // ---------- Two-hand grab and fuse ----------

        // Called every frame while the primary hand is grabbing (not resizing).
        private void UpdateMoleculeDualGrab(bool leftValid, Vector3 leftPalm, bool leftFist, bool leftOpen,
            bool rightValid, Vector3 rightPalm, bool rightFist, bool rightOpen)
        {
            fuseCooldownRemaining = Mathf.Max(0f, fuseCooldownRemaining - Time.unscaledDeltaTime);
            BubbleInteractionHand otherHand = directOwner == BubbleInteractionHand.Left
                ? BubbleInteractionHand.Right : BubbleInteractionHand.Left;
            BeckonHand other = otherHand == BubbleInteractionHand.Left ? leftBeckon : rightBeckon;
            bool otherValid = otherHand == BubbleInteractionHand.Left ? leftValid : rightValid;
            Vector3 otherPalm = otherHand == BubbleInteractionHand.Left ? leftPalm : rightPalm;
            bool otherFist = otherHand == BubbleInteractionHand.Left ? leftFist : rightFist;
            bool otherOpen = otherHand == BubbleInteractionHand.Left ? leftOpen : rightOpen;

            if (string.IsNullOrEmpty(secondIdeaId))
            {
                if (otherValid && otherFist && fuseCooldownRemaining <= 0f)
                    TryBeginSecondGrab(other, otherHand, otherPalm);
                return;
            }
            if (!visible.TryGetValue(secondIdeaId, out IdeaBubbleView second) || second == null || !otherValid)
            { ReleaseSecondGrab(true); return; }
            if (otherOpen)
            {
                secondReleaseTimer += Time.unscaledDeltaTime;
                if (secondReleaseTimer >= grabReleaseHold) ReleaseSecondGrab(true);
                return;
            }
            secondReleaseTimer = 0f;
            if (secondGliding)
            {
                secondElapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(secondElapsed / grabGlideDuration));
                second.transform.position = Vector3.Lerp(secondStartPosition, otherPalm, t);
                if (t >= 1f) secondGliding = false;
            }
            else second.transform.position = otherPalm;
            UpdateFuse(second);
        }

        private void TryBeginSecondGrab(BeckonHand hand, BubbleInteractionHand owner, Vector3 palm)
        {
            BrainstormSession session = ActiveMoleculeSession;
            string primaryMolecule = MoleculeRules.FindMoleculeCenter(session, directIdeaId);
            IdeaBubbleView nearest = null;
            float best = float.PositiveInfinity;
            foreach (IdeaBubbleView bubble in visible.Values)
            {
                if (bubble == null || bubble.IdeaId == directIdeaId || movements.ContainsKey(bubble.IdeaId)
                    || fusingIdeaIds.Contains(bubble.IdeaId)) continue;
                // A bubble from the same molecule would just pull the molecule apart, not fuse.
                if (primaryMolecule != null && MoleculeRules.FindMoleculeCenter(session, bubble.IdeaId) == primaryMolecule)
                    continue;
                float surface = MinimumHandSurfaceDistance(hand, palm, bubble);
                float tolerance = ScaleAwareSurfaceTolerance(directGrabSurfaceRange, bubble.WorldRadius, .05f, .11f);
                if (surface <= tolerance && surface < best) { best = surface; nearest = bubble; }
            }
            if (nearest == null) return;
            secondIdeaId = nearest.IdeaId; secondOwner = owner;
            secondStartPosition = nearest.transform.position;
            secondElapsed = secondReleaseTimer = fuseTimer = 0f; secondGliding = true;
            nearest.SetMoving(true);
            if (openPanels.TryGetValue(secondIdeaId, out IdeaDetailsPanel panel) && panel != null)
                panel.SetMovementHidden(true);
            LastMoleculeEvent = "Second grab: " + secondIdeaId;
        }

        private void UpdateFuse(IdeaBubbleView second)
        {
            if (!visible.TryGetValue(directIdeaId, out IdeaBubbleView first) || first == null) return;
            float gap = SurfaceGap(first.transform.position, first.WorldRadius,
                second.transform.position, second.WorldRadius);
            bool touching = gap <= fuseContactGap;
            first.SetDirectProximity(touching);
            second.SetDirectProximity(touching);
            if (!touching) { fuseTimer = 0f; return; }
            fuseTimer += Time.unscaledDeltaTime;
            if (fuseTimer < fuseHoldDuration) return;
            fuseTimer = 0f;
            TriggerFuse(first, second);
        }

        private void TriggerFuse(IdeaBubbleView first, IdeaBubbleView second)
        {
            fuseCooldownRemaining = fuseCooldown;
            Vector3 fusePoint = Vector3.Lerp(first.transform.position, second.transform.position, .5f);
            string firstId = first.IdeaId, secondId = second.IdeaId;
            string rejection = null;
            if (moleculeCoordinator == null
                || !moleculeCoordinator.TryRequestFuse(firstId, secondId, fusePoint, out rejection))
            {
                string reason = moleculeCoordinator != null && moleculeCoordinator.IsBusy
                    ? "I'm still working on the last combination." : null;
                SpeakMoleculeLine(rejection ?? reason);
                LastMoleculeEvent = "Fuse rejected: " + (moleculeCoordinator?.LastResult ?? "no coordinator");
                // Push the second bubble back so the rejection is visible, then let the first hand keep holding.
                Vector3 away = second.transform.position - first.transform.position;
                if (away.sqrMagnitude < 1e-6f) away = Vector3.right;
                second.transform.position += away.normalized * rejectionPushDistance;
                ReleaseSecondGrab(true);
                return;
            }
            LastMoleculeEvent = "Fusing: " + firstId + " + " + secondId;
            fuseReturnPositions[firstId] = first.transform.position;
            fuseReturnPositions[secondId] = second.transform.position;
            fusingIdeaIds.Add(firstId); fusingIdeaIds.Add(secondId);
            ReleaseSecondGrab(false);
            FinishDirectManipulation(false);
            // Draw both bubbles into the fuse point while the backend works; the result re-lays them out.
            StartMovement(first, fusePoint, first.transform.rotation, first.transform.localScale * .85f, false);
            StartMovement(second, fusePoint, second.transform.rotation, second.transform.localScale * .85f, false);
        }

        private void ReleaseSecondGrab(bool persist)
        {
            if (string.IsNullOrEmpty(secondIdeaId)) return;
            if (visible.TryGetValue(secondIdeaId, out IdeaBubbleView second) && second != null)
            {
                second.SetMoving(false); second.SetDirectProximity(false);
                if (persist) CommitManualTransform(second);
                if (openPanels.TryGetValue(secondIdeaId, out IdeaDetailsPanel panel) && panel != null)
                    panel.RelocateAfterBubbleMovement(CollectVisiblePanelPositions(secondIdeaId));
            }
            secondIdeaId = string.Empty; secondOwner = BubbleInteractionHand.Unknown;
            secondElapsed = secondReleaseTimer = fuseTimer = 0f; secondGliding = false;
        }

        // When the primary hand lets go while the other hand still holds a bubble, that hand takes over.
        private bool TryPromoteSecondGrab()
        {
            if (string.IsNullOrEmpty(secondIdeaId) || !visible.TryGetValue(secondIdeaId, out IdeaBubbleView second)
                || second == null) return false;
            if (visible.TryGetValue(directIdeaId, out IdeaBubbleView first) && first != null)
            {
                first.SetMoving(false); first.SetDirectProximity(false);
                CommitManualTransform(first);
                FinishMoleculeMovement(directIdeaId);
            }
            directIdeaId = secondIdeaId; directOwner = secondOwner;
            directMode = DirectManipulationMode.Grabbed; directReleaseTimer = 0f;
            secondIdeaId = string.Empty; secondOwner = BubbleInteractionHand.Unknown;
            secondElapsed = secondReleaseTimer = fuseTimer = 0f; secondGliding = false;
            return true;
        }

        // ---------- Molecule layout, bonds and pull-out ----------

        // Called each frame: sources follow their center, and bond lines are drawn between them.
        private void UpdateMoleculeBonds()
        {
            BrainstormSession session = ActiveMoleculeSession;
            var activeBonds = new HashSet<string>();
            if (session != null)
                foreach (BrainstormIdeaRecord idea in session.Ideas)
                {
                    if (idea == null || !visible.TryGetValue(idea.IdeaId, out IdeaBubbleView center) || center == null)
                        continue;
                    List<string> sources = MoleculeRules.GetSources(session, idea.IdeaId);
                    if (sources.Count < 2) continue;
                    foreach (string sourceId in sources)
                    {
                        if (!visible.TryGetValue(sourceId, out IdeaBubbleView source) || source == null) continue;
                        bool held = sourceId == directIdeaId || sourceId == secondIdeaId;
                        if (!held && !movements.ContainsKey(sourceId) && !fusingIdeaIds.Contains(sourceId)
                            && sourceOffsets.TryGetValue(sourceId, out Vector3 offset))
                            source.transform.position = center.transform.position + offset;
                        // Resting sources touch the center, so the bond is only drawn while one is held.
                        if (!held) continue;
                        activeBonds.Add(sourceId);
                        DrawBond(sourceId, center, source, IsBondStretched(center, source));
                    }
                }
            var stale = new List<string>();
            foreach (string key in bonds.Keys) if (!activeBonds.Contains(key)) stale.Add(key);
            foreach (string key in stale) { if (bonds[key] != null) Destroy(bonds[key].gameObject); bonds.Remove(key); }
        }

        private void DrawBond(string sourceId, IdeaBubbleView center, IdeaBubbleView source, bool stretched)
        {
            if (!bonds.TryGetValue(sourceId, out LineRenderer line) || line == null)
            {
                var bondObject = new GameObject("Molecule Bond · " + sourceId);
                bondObject.transform.SetParent(bubbleRoot, false);
                line = bondObject.AddComponent<LineRenderer>();
                line.positionCount = 2; line.useWorldSpace = true;
                line.startWidth = line.endWidth = .006f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                bonds[sourceId] = line;
            }
            Vector3 direction = source.transform.position - center.transform.position;
            float length = direction.magnitude;
            Vector3 unit = length > 1e-5f ? direction / length : Vector3.up;
            // Draw between the bubble surfaces so the bond reads as a link, not a line through the bubbles.
            line.SetPosition(0, center.transform.position + unit * center.WorldRadius);
            line.SetPosition(1, source.transform.position - unit * source.WorldRadius);
            line.sharedMaterial = stretched ? stretchedBondMaterial : bondMaterial;
        }

        private bool IsBondStretched(IdeaBubbleView center, IdeaBubbleView source)
            => ShouldBreakBond(SurfaceGap(center.transform.position, center.WorldRadius,
                source.transform.position, source.WorldRadius), bondGap, bondBreakStretch);

        // Called when a direct grab of this bubble ends: pull-out check for sources, carry-along for centers.
        private void FinishMoleculeMovement(string ideaId)
        {
            BrainstormSession session = ActiveMoleculeSession;
            if (session == null || string.IsNullOrEmpty(ideaId) || fusingIdeaIds.Contains(ideaId)) return;
            string center = MoleculeRules.FindCenterOf(session, ideaId);
            if (center != null && visible.TryGetValue(center, out IdeaBubbleView centerBubble)
                && visible.TryGetValue(ideaId, out IdeaBubbleView source) && centerBubble != null && source != null)
            {
                if (IsBondStretched(centerBubble, source)
                    && moleculeCoordinator != null && moleculeCoordinator.TryRequestPullOut(center, ideaId))
                {
                    LastMoleculeEvent = "Pulled out: " + ideaId;
                    sourceOffsets.Remove(ideaId);
                    RestoreStandaloneScale(source);
                }
                else SnapSourceBack(centerBubble, source);
                return;
            }
            if (MoleculeRules.IsCenter(session, ideaId)) PersistSources(session, ideaId);
        }

        private void SnapSourceBack(IdeaBubbleView center, IdeaBubbleView source)
        {
            if (!sourceOffsets.TryGetValue(source.IdeaId, out Vector3 offset)) return;
            Vector3 target = center.transform.position + offset;
            StartMovement(source, target, source.transform.rotation, source.transform.localScale, false);
            Store?.TryUpdateBubbleTransform(activeSessionId, source.IdeaId, target, source.transform.rotation,
                source.transform.localScale);
        }

        private void PersistSources(BrainstormSession session, string centerId)
        {
            foreach (string sourceId in MoleculeRules.GetSources(session, centerId))
                if (visible.TryGetValue(sourceId, out IdeaBubbleView source) && source != null)
                    Store?.TryUpdateBubbleTransform(activeSessionId, sourceId, source.transform.position,
                        source.transform.rotation, source.transform.localScale);
        }

        private void HandleMoleculeChanged(MoleculeChange change)
        {
            if (change.SessionId != activeSessionId) return;
            foreach (string id in change.SourceIdeaIds) { fusingIdeaIds.Remove(id); fuseReturnPositions.Remove(id); }
            if (change.Dissolved)
            {
                LastMoleculeEvent = "Molecule dissolved: " + change.CenterIdeaId;
                var freed = new List<string>(sourceOffsets.Keys);
                foreach (string id in freed)
                    if (MoleculeRules.FindCenterOf(ActiveMoleculeSession, id) == null && visible.TryGetValue(id, out IdeaBubbleView bubble))
                    { sourceOffsets.Remove(id); RestoreStandaloneScale(bubble); }
                if (visible.TryGetValue(change.CenterIdeaId, out IdeaBubbleView center) && center != null)
                    RestoreStandaloneScale(center);
                return;
            }
            if (change.CreatedCenter) ShowSavedIdea(change.CenterIdeaId);
            LayoutMolecule(change.CenterIdeaId);
            LastMoleculeEvent = (change.CreatedCenter ? "Molecule created: " : "Molecule updated: ") + change.CenterIdeaId;
        }

        private void HandleMoleculeRequestFailed(string centerIdeaId, string[] sourceIdeaIds)
        {
            LastMoleculeEvent = "Molecule request failed";
            // Split the fusing bubbles back to where they were.
            foreach (string id in sourceIdeaIds)
            {
                fusingIdeaIds.Remove(id);
                if (fuseReturnPositions.TryGetValue(id, out Vector3 position)
                    && visible.TryGetValue(id, out IdeaBubbleView bubble) && bubble != null)
                    StartMovement(bubble, position, bubble.transform.rotation,
                        BaseScaleFor(id, ActiveMoleculeSession), false);
                fuseReturnPositions.Remove(id);
            }
            if (centerIdeaId != null) LayoutMolecule(centerIdeaId);
        }

        // Places the center at its size and arranges the sources around it, facing the user.
        private void LayoutMolecule(string centerId)
        {
            BrainstormSession session = ActiveMoleculeSession;
            if (session == null || !visible.TryGetValue(centerId, out IdeaBubbleView center) || center == null) return;
            List<string> sources = MoleculeRules.GetSources(session, centerId);
            if (sources.Count < 2) return;
            float baseRadius = center.WorldRadius / Mathf.Max(.01f, center.transform.localScale.x);
            Vector3 centerScale = Vector3.one * MoleculeCombineCoordinator.CenterScale;
            Vector3 centerPosition = movements.TryGetValue(centerId, out BubbleMovement moving)
                ? moving.toPosition : center.transform.position;
            if (center.transform.localScale != centerScale)
                StartMovement(center, centerPosition, center.transform.rotation, centerScale, false);
            Store?.TryUpdateBubbleTransform(activeSessionId, centerId, centerPosition, center.transform.rotation, centerScale);

            Vector3 toUser = headset != null ? headset.position - centerPosition : Vector3.back;
            Vector3 forward = toUser.sqrMagnitude > 1e-4f ? toUser.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Vector3[] offsets = ComputeSourceOffsets(sources.Count, baseRadius * centerScale.x,
                baseRadius * sourceScale, bondGap, right, up);
            for (int i = 0; i < sources.Count; i++)
            {
                if (!visible.TryGetValue(sources[i], out IdeaBubbleView source) || source == null) continue;
                sourceOffsets[sources[i]] = offsets[i];
                Vector3 target = centerPosition + offsets[i];
                Vector3 scale = Vector3.one * sourceScale;
                StartMovement(source, target, source.transform.rotation, scale, false);
                Store?.TryUpdateBubbleTransform(activeSessionId, sources[i], target, source.transform.rotation, scale);
            }
        }

        // Rebuilds source offsets from saved positions, laying out molecules that were never arranged
        // (for example ideas combined by voice before this feature).
        private void RestoreMolecules()
        {
            sourceOffsets.Clear();
            BrainstormSession session = ActiveMoleculeSession;
            if (session == null) return;
            foreach (BrainstormIdeaRecord idea in session.Ideas)
            {
                if (idea == null || !MoleculeRules.IsCenter(session, idea.IdeaId)
                    || !visible.TryGetValue(idea.IdeaId, out IdeaBubbleView center) || center == null) continue;
                bool arranged = true;
                foreach (string sourceId in MoleculeRules.GetSources(session, idea.IdeaId))
                {
                    if (!visible.TryGetValue(sourceId, out IdeaBubbleView source) || source == null) continue;
                    Vector3 offset = source.transform.position - center.transform.position;
                    float expected = center.WorldRadius + source.WorldRadius + bondGap;
                    if (Mathf.Abs(offset.magnitude - expected) > bondBreakStretch) { arranged = false; break; }
                    sourceOffsets[sourceId] = offset;
                }
                if (!arranged) LayoutMolecule(idea.IdeaId);
            }
        }

        // A combined idea that appears outside a gesture (for example by voice) becomes a molecule too.
        private void LayoutIfNewMolecule(string ideaId)
        {
            BrainstormSession session = ActiveMoleculeSession;
            if (session != null && MoleculeRules.IsCenter(session, ideaId) && !fusingIdeaIds.Overlaps(
                    MoleculeRules.GetSources(session, ideaId)))
                LayoutMolecule(ideaId);
        }

        private void RestoreStandaloneScale(IdeaBubbleView bubble)
        {
            Vector3 target = bubble.transform.position;
            StartMovement(bubble, target, bubble.transform.rotation, Vector3.one, false);
            Store?.TryUpdateBubbleTransform(activeSessionId, bubble.IdeaId, target, bubble.transform.rotation, Vector3.one);
        }

        private Vector3 BaseScaleFor(string ideaId, BrainstormSession session)
            => MoleculeRules.IsCenter(session, ideaId) ? Vector3.one * MoleculeCombineCoordinator.CenterScale
                : MoleculeRules.IsLockedSource(session, ideaId) ? Vector3.one * sourceScale : Vector3.one;

        private void ClearMoleculeVisuals()
        {
            ReleaseSecondGrab(false);
            foreach (LineRenderer line in bonds.Values) if (line != null) Destroy(line.gameObject);
            bonds.Clear(); sourceOffsets.Clear(); fusingIdeaIds.Clear(); fuseReturnPositions.Clear();
        }

        private void SpeakMoleculeLine(string line)
        {
            if (!string.IsNullOrWhiteSpace(line) && OrbyVoiceDirector.Instance != null)
                OrbyVoiceDirector.Instance.SpeakDynamic(line, activeSessionId, OrbyVoicePriority.Acknowledgement);
        }

        // ---------- Pure helpers (covered by EditMode tests) ----------

        public static float SurfaceGap(Vector3 firstCenter, float firstRadius, Vector3 secondCenter, float secondRadius)
            => Vector3.Distance(firstCenter, secondCenter) - Mathf.Max(0f, firstRadius) - Mathf.Max(0f, secondRadius);

        public static bool ShouldBreakBond(float surfaceGap, float restingGap, float breakStretch)
            => surfaceGap - restingGap > breakStretch;

        // Evenly spaced positions around the center in the plane facing the user:
        // two sources sit left and right, three form a triangle with one on top.
        public static Vector3[] ComputeSourceOffsets(int count, float centerRadius, float sourceRadius, float gap,
            Vector3 right, Vector3 up)
        {
            var offsets = new Vector3[Mathf.Max(0, count)];
            float distance = centerRadius + sourceRadius + gap;
            float start = offsets.Length == 2 ? 0f : Mathf.PI * .5f;
            for (int i = 0; i < offsets.Length; i++)
            {
                float angle = start + i * 2f * Mathf.PI / offsets.Length;
                offsets[i] = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * distance;
            }
            return offsets;
        }
    }
}
