using System;
using System.Collections;
using System.Collections.Generic;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    // A molecule is a combined idea (the center) bonded to the ideas it was combined from (the sources).
    // It is derived from the session's "combined_from" relationships, so no separate state is stored.
    public static class MoleculeRules
    {
        public const int MaximumSources = 3;

        public enum FuseOutcome
        {
            NewMolecule,
            AddToMolecule,
            RejectedSameIdea,
            RejectedMissingIdea,
            RejectedMoleculeToMolecule,
            RejectedMoleculeFull
        }

        public readonly struct FuseDecision
        {
            public readonly FuseOutcome Outcome;
            public readonly string CenterIdeaId;
            public readonly string[] SourceIdeaIds;
            public bool Accepted => Outcome == FuseOutcome.NewMolecule || Outcome == FuseOutcome.AddToMolecule;

            public FuseDecision(FuseOutcome outcome, string centerIdeaId = null, string[] sourceIdeaIds = null)
            {
                Outcome = outcome;
                CenterIdeaId = centerIdeaId;
                SourceIdeaIds = sourceIdeaIds ?? Array.Empty<string>();
            }
        }

        public readonly struct PullOutDecision
        {
            public readonly bool Valid;
            public readonly bool Dissolve;
            public readonly string[] RemainingSourceIdeaIds;

            public PullOutDecision(bool valid, bool dissolve, string[] remaining)
            {
                Valid = valid;
                Dissolve = dissolve;
                RemainingSourceIdeaIds = remaining ?? Array.Empty<string>();
            }
        }

        public static bool IsCenter(BrainstormSession session, string ideaId)
            => GetSources(session, ideaId).Count >= 2;

        public static List<string> GetSources(BrainstormSession session, string centerIdeaId)
        {
            var sources = new List<string>();
            if (session == null || string.IsNullOrEmpty(centerIdeaId)) return sources;
            foreach (IdeaRelationshipRecord relationship in session.Relationships)
                if (relationship != null && relationship.SourceIdeaId == centerIdeaId
                    && relationship.RelationshipType == BrainstormSessionStore.CombinedFromRelationship
                    && !sources.Contains(relationship.TargetIdeaId)
                    && session.Ideas.Exists(idea => idea != null && idea.IdeaId == relationship.TargetIdeaId))
                    sources.Add(relationship.TargetIdeaId);
            return sources;
        }

        // The center a bonded source belongs to, or null when the idea is not a source of any molecule.
        public static string FindCenterOf(BrainstormSession session, string sourceIdeaId)
        {
            if (session == null || string.IsNullOrEmpty(sourceIdeaId)) return null;
            foreach (IdeaRelationshipRecord relationship in session.Relationships)
                if (relationship != null && relationship.TargetIdeaId == sourceIdeaId
                    && relationship.RelationshipType == BrainstormSessionStore.CombinedFromRelationship
                    && IsCenter(session, relationship.SourceIdeaId))
                    return relationship.SourceIdeaId;
            return null;
        }

        // Bonded sources are locked: they can't be expanded, challenged or edited until pulled out.
        public static bool IsLockedSource(BrainstormSession session, string ideaId)
            => FindCenterOf(session, ideaId) != null;

        // The molecule an idea belongs to (as center or source), or null for a standalone idea.
        public static string FindMoleculeCenter(BrainstormSession session, string ideaId)
            => IsCenter(session, ideaId) ? ideaId : FindCenterOf(session, ideaId);

        public static FuseDecision EvaluateFuse(BrainstormSession session, string firstIdeaId, string secondIdeaId)
        {
            if (string.IsNullOrEmpty(firstIdeaId) || firstIdeaId == secondIdeaId)
                return new FuseDecision(FuseOutcome.RejectedSameIdea);
            if (!Exists(session, firstIdeaId) || !Exists(session, secondIdeaId))
                return new FuseDecision(FuseOutcome.RejectedMissingIdea);
            string firstMolecule = FindMoleculeCenter(session, firstIdeaId);
            string secondMolecule = FindMoleculeCenter(session, secondIdeaId);
            if (firstMolecule == null && secondMolecule == null)
                return new FuseDecision(FuseOutcome.NewMolecule, null, new[] { firstIdeaId, secondIdeaId });
            if (firstMolecule != null && secondMolecule != null)
                return new FuseDecision(FuseOutcome.RejectedMoleculeToMolecule, firstMolecule);
            string center = firstMolecule ?? secondMolecule;
            string joining = firstMolecule == null ? firstIdeaId : secondIdeaId;
            List<string> sources = GetSources(session, center);
            if (sources.Count >= MaximumSources)
                return new FuseDecision(FuseOutcome.RejectedMoleculeFull, center);
            sources.Add(joining);
            return new FuseDecision(FuseOutcome.AddToMolecule, center, sources.ToArray());
        }

        public static PullOutDecision EvaluatePullOut(BrainstormSession session, string centerIdeaId, string sourceIdeaId)
        {
            List<string> sources = GetSources(session, centerIdeaId);
            if (sources.Count < 2 || !sources.Remove(sourceIdeaId)) return new PullOutDecision(false, false, null);
            return new PullOutDecision(true, sources.Count < 2, sources.ToArray());
        }

        public static string RejectionLine(FuseOutcome outcome)
        {
            switch (outcome)
            {
                case FuseOutcome.RejectedMoleculeFull:
                    return "That molecule is full. It can hold three ideas. Pull one out to make room.";
                case FuseOutcome.RejectedMoleculeToMolecule:
                    return "Two molecules can't combine. Try adding a single idea instead.";
                default:
                    return null;
            }
        }

        private static bool Exists(BrainstormSession session, string ideaId)
            => session != null && session.Ideas.Exists(idea => idea != null && idea.IdeaId == ideaId);
    }

    [Serializable] public sealed class MoleculeCombineResponse
    {
        public string contract_version, request_id, session_id, action, idea_id;
        public string title, summary, category, source;
        public string[] related_idea_ids;
        public string spoken_response, status, error, fallback_spoken_response;
        public bool updated_existing_idea;
    }

    [Serializable] internal sealed class CenterCombineWireRequest
    {
        public string request_id, session_id, action, raw_transcript, transcript_source, center_idea_id;
        public string[] related_idea_ids;
        public AIIdeaContext[] existing_ideas;
    }

    public readonly struct MoleculeChange
    {
        public readonly string SessionId, CenterIdeaId;
        public readonly string[] SourceIdeaIds;
        public readonly bool CreatedCenter, Dissolved;

        public MoleculeChange(string sessionId, string centerIdeaId, string[] sourceIdeaIds, bool createdCenter,
            bool dissolved)
        {
            SessionId = sessionId;
            CenterIdeaId = centerIdeaId;
            SourceIdeaIds = sourceIdeaIds ?? Array.Empty<string>();
            CreatedCenter = createdCenter;
            Dissolved = dissolved;
        }
    }

    // Runs molecule changes requested by the bubble gestures: calls the backend, saves the result, and
    // reports the change so the bubble manager can rearrange the molecule.
    public sealed class MoleculeCombineCoordinator : MonoBehaviour
    {
        public const string GestureTranscript = "Combine these ideas.";
        public const float CenterScale = 1.3f;

        private ThinkingSpaceAIBackendClient client;
        private BrainstormMenuController menuController;
        private int requestCounter;

        public event Action<MoleculeChange> MoleculeChanged;
        public event Action<string, string[]> MoleculeRequestFailed;
        public bool IsBusy { get; private set; }
        public string LastResult { get; private set; } = "None";

        private BrainstormSessionStore Store
        {
            get
            {
                if (menuController == null) menuController = FindFirstObjectByType<BrainstormMenuController>();
                return menuController != null ? menuController.SessionStore : null;
            }
        }

        private void Awake()
        {
            client = GetComponent<ThinkingSpaceAIBackendClient>();
            if (client == null) client = gameObject.AddComponent<ThinkingSpaceAIBackendClient>();
        }

        // Returns false with a line for Orby when the fuse breaks a molecule rule or another change is running.
        public bool TryRequestFuse(string firstIdeaId, string secondIdeaId, Vector3 fusePoint, out string rejectionLine)
        {
            rejectionLine = null;
            BrainstormSession session = Store?.ActiveSession;
            if (session == null || IsBusy) return false;
            MoleculeRules.FuseDecision decision = MoleculeRules.EvaluateFuse(session, firstIdeaId, secondIdeaId);
            if (!decision.Accepted)
            {
                rejectionLine = MoleculeRules.RejectionLine(decision.Outcome);
                LastResult = "Fuse rejected: " + decision.Outcome;
                return false;
            }
            StartCoroutine(Combine(session.SessionId, decision.CenterIdeaId, decision.SourceIdeaIds, fusePoint));
            return true;
        }

        public bool TryRequestPullOut(string centerIdeaId, string sourceIdeaId)
        {
            BrainstormSessionStore store = Store;
            BrainstormSession session = store?.ActiveSession;
            if (session == null || IsBusy) return false;
            MoleculeRules.PullOutDecision decision = MoleculeRules.EvaluatePullOut(session, centerIdeaId, sourceIdeaId);
            if (!decision.Valid) return false;
            if (decision.Dissolve)
            {
                // One idea can't be "combined", so the molecule falls apart without a backend call.
                bool dissolved = store.TryDissolveMolecule(session.SessionId, centerIdeaId);
                LastResult = dissolved ? "Molecule dissolved" : "Dissolve save failed";
                if (dissolved)
                    MoleculeChanged?.Invoke(new MoleculeChange(session.SessionId, centerIdeaId,
                        Array.Empty<string>(), false, true));
                return dissolved;
            }
            StartCoroutine(Combine(session.SessionId, centerIdeaId, decision.RemainingSourceIdeaIds, Vector3.zero));
            return true;
        }

        private IEnumerator Combine(string sessionId, string centerIdeaId, string[] sourceIdeaIds, Vector3 fusePoint)
        {
            IsBusy = true;
            BrainstormSession session = Store.ActiveSession;
            var request = BuildRequest(session, "molecule-" + (++requestCounter) + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 12), centerIdeaId, sourceIdeaIds);
            AIBackendResult<MoleculeCombineResponse> result = null;
            yield return client.ProcessMoleculeCombine(request, value => result = value);
            IsBusy = false;
            BrainstormSessionStore store = Store;
            if (store?.ActiveSession == null || store.ActiveSession.SessionId != sessionId) yield break;
            string problem = result == null ? "No response."
                : !result.Success ? result.Error : ValidateResponse(result.Response, request, centerIdeaId);
            if (!string.IsNullOrEmpty(problem))
            {
                LastResult = "Combine failed: " + problem;
                MoleculeRequestFailed?.Invoke(centerIdeaId, sourceIdeaIds);
                SpeakFallback(result?.Response?.fallback_spoken_response, sessionId);
                yield break;
            }
            MoleculeCombineResponse response = result.Response;
            bool saved = centerIdeaId == null
                ? store.TryAddIdea(sessionId, new BrainstormIdeaRecord
                {
                    IdeaId = response.idea_id, Title = response.title, Summary = response.summary,
                    Category = response.category, SourceTranscript = GestureTranscript,
                    RelatedIdeaIds = new List<string>(sourceIdeaIds), CreatedUtc = DateTime.UtcNow.ToString("O"),
                    Bubble = new BubbleTransformRecord { Position = fusePoint, Rotation = Quaternion.identity,
                        Scale = Vector3.one * CenterScale }
                })
                : store.TryUpdateCombinedIdea(sessionId, centerIdeaId, response.title, response.summary,
                    response.category, sourceIdeaIds);
            if (!saved)
            {
                LastResult = "Combine save failed: " + store.LastPersistenceError;
                MoleculeRequestFailed?.Invoke(centerIdeaId, sourceIdeaIds);
                yield break;
            }
            LastResult = centerIdeaId == null ? "Molecule created" : "Molecule updated";
            store.AppendConversation(sessionId, "orby", response.spoken_response, "molecule_result",
                request.request_id, sourceIdeaIds);
            MoleculeChanged?.Invoke(new MoleculeChange(sessionId, response.idea_id, sourceIdeaIds,
                centerIdeaId == null, false));
            if (!string.IsNullOrWhiteSpace(response.spoken_response))
                ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.SpeakDynamic(response.spoken_response, sessionId);
        }

        public static IdeaProcessRequest BuildRequest(BrainstormSession session, string requestId,
            string centerIdeaId, string[] sourceIdeaIds)
        {
            // The backend needs context for every source and for the center being revised.
            var ids = new HashSet<string>(sourceIdeaIds);
            if (!string.IsNullOrEmpty(centerIdeaId)) ids.Add(centerIdeaId);
            var contexts = new List<AIIdeaContext>();
            foreach (BrainstormIdeaRecord idea in session.Ideas)
                if (idea != null && ids.Contains(idea.IdeaId))
                    contexts.Add(new AIIdeaContext { idea_id = idea.IdeaId, title = idea.Title, summary = idea.Summary });
            return new IdeaProcessRequest
            {
                request_id = requestId, session_id = session.SessionId, action = "combine_ideas",
                raw_transcript = GestureTranscript, transcript_source = "raw",
                related_idea_ids = sourceIdeaIds, existing_ideas = contexts.ToArray(),
                center_idea_id = centerIdeaId
            };
        }

        public static string ValidateResponse(MoleculeCombineResponse response, IdeaProcessRequest request,
            string centerIdeaId)
        {
            if (response == null || response.status != "completed") return "Response did not complete.";
            if (response.request_id != request.request_id || response.session_id != request.session_id)
                return "Response identity mismatch.";
            if (string.IsNullOrWhiteSpace(response.idea_id) || string.IsNullOrWhiteSpace(response.title)
                || string.IsNullOrWhiteSpace(response.summary)) return "Response is missing the combined idea.";
            if (centerIdeaId != null && (!response.updated_existing_idea || response.idea_id != centerIdeaId))
                return "Response did not update the molecule center.";
            if (centerIdeaId == null && response.updated_existing_idea) return "Response unexpectedly updated an idea.";
            return string.Empty;
        }

        public const string DefaultFailureLine = "I couldn't combine those right now. Try again in a moment.";

        private static void SpeakFallback(string line, string sessionId)
        {
            // The backend line is only available when it returned an error body; otherwise use a local one.
            ThinkingSpace.Companion.OrbyVoiceDirector.Instance?.SpeakDynamic(
                string.IsNullOrWhiteSpace(line) ? DefaultFailureLine : line, sessionId);
        }
    }
}
