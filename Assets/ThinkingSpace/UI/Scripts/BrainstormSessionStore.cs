using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThinkingSpace.UI
{
    [Serializable] public sealed class BrainstormIdeaRecord
    {
        public string IdeaId = string.Empty, Title = string.Empty, Summary = string.Empty, Category = string.Empty;
        public string SourceTranscript = string.Empty, CreatedUtc = string.Empty;
        public List<string> RelatedIdeaIds = new List<string>();
        public BubbleTransformRecord Bubble = new BubbleTransformRecord();
    }
    [Serializable] public sealed class IdeaRelationshipRecord
    {
        public string SourceIdeaId = string.Empty, TargetIdeaId = string.Empty, RelationshipType = string.Empty;
    }
    [Serializable] public sealed class BubbleTransformRecord
    {
        public Vector3 Position; public Quaternion Rotation = Quaternion.identity; public Vector3 Scale = Vector3.one;
    }
    [Serializable] public sealed class ConversationRecord
    {
        public string MessageId = string.Empty, Speaker = string.Empty, Text = string.Empty, CreatedUtc = string.Empty;
        public string MessageType = string.Empty, RelatedRequestId = string.Empty;
        public List<string> RelatedIdeaIds = new List<string>();
    }

    [Serializable]
    public sealed class BrainstormSession
    {
        [SerializeField] private string sessionId;
        [SerializeField] private int number;
        [SerializeField] private string createdUtc, updatedUtc;
        [SerializeField] private List<BrainstormIdeaRecord> ideas = new List<BrainstormIdeaRecord>();
        [SerializeField] private List<IdeaRelationshipRecord> relationships = new List<IdeaRelationshipRecord>();
        [SerializeField] private List<ConversationRecord> conversation = new List<ConversationRecord>();
        public string SessionId => sessionId;
        public int Number => number;
        public string CreatedUtc => createdUtc;
        public string UpdatedUtc => updatedUtc;
        public List<BrainstormIdeaRecord> Ideas => ideas;
        public List<IdeaRelationshipRecord> Relationships => relationships;
        public List<ConversationRecord> Conversation => conversation;

        public BrainstormSession(int number) : this("session-" + Guid.NewGuid().ToString("N"), number,
            DateTime.UtcNow.ToString("O"), DateTime.UtcNow.ToString("O")) { }
        internal BrainstormSession(string id, int sessionNumber, string created, string updated)
        { sessionId = id; number = sessionNumber; createdUtc = created; updatedUtc = updated; }
        internal void MarkUpdated() => updatedUtc = DateTime.UtcNow.ToString("O");
    }

    public sealed class BrainstormSessionStore
    {
        public const int CurrentSchemaVersion = 2;
        public const string SaveFileName = "sessions-v1.json";
        [Serializable] private sealed class SaveDocument
        {
            public int SchemaVersion = CurrentSchemaVersion;
            public int NextSessionNumber = 1;
            public string ActiveSessionId = string.Empty;
            public bool OnboardingCompleted;
            public List<BrainstormSession> Sessions = new List<BrainstormSession>();
        }

        private readonly List<BrainstormSession> sessions = new List<BrainstormSession>();
        private readonly string directory, savePath, backupPath;
        private int nextSessionNumber = 1;
        public IReadOnlyList<BrainstormSession> Sessions => sessions;
        public BrainstormSession ActiveSession { get; private set; }
        public bool OnboardingCompleted { get; private set; }
        public string PersistencePath => savePath;
        public string LastPersistenceError { get; private set; } = string.Empty;

        // Call only from a Unity lifecycle method such as Awake. Keeping path resolution
        // outside the constructor makes explicit-directory tests independent of Unity APIs.
        public static BrainstormSessionStore CreateForApplication()
        {
            return new BrainstormSessionStore(Path.Combine(Application.persistentDataPath, "ThinkingSpace"));
        }

        public BrainstormSessionStore(string storageDirectory)
        {
            directory = storageDirectory; savePath = Path.Combine(directory, SaveFileName);
            backupPath = savePath + ".backup"; Load();
        }

        public BrainstormSession StartNewSession()
        {
            BrainstormSession session = new BrainstormSession(nextSessionNumber++);
            sessions.Add(session); ActiveSession = session; Save(); return session;
        }
        public bool ReopenSession(int number)
        {
            BrainstormSession session = sessions.Find(item => item.Number == number);
            if (session == null) return false; ActiveSession = session; Save(); return true;
        }
        public void EndActiveSession() { ActiveSession = null; Save(); }
        public void SetOnboardingCompleted(bool completed)
        { if (OnboardingCompleted == completed) return; OnboardingCompleted = completed; Save(); }

        public bool ResetAllSessionsForDevelopment()
        {
            var previousSessions=new List<BrainstormSession>(sessions);BrainstormSession previousActive=ActiveSession;
            int previousNext=nextSessionNumber;bool previousOnboarding=OnboardingCompleted;
            sessions.Clear();ActiveSession=null;nextSessionNumber=1;OnboardingCompleted=false;
            if(Save())return true;
            sessions.AddRange(previousSessions);ActiveSession=previousActive;nextSessionNumber=previousNext;OnboardingCompleted=previousOnboarding;return false;
        }

        public bool TryAddIdea(string sessionId, BrainstormIdeaRecord idea)
        {
            if (ActiveSession == null || idea == null || string.IsNullOrEmpty(idea.IdeaId)
                || ActiveSession.SessionId != sessionId
                || ActiveSession.Ideas.Exists(item => item.IdeaId == idea.IdeaId)) return false;
            if (string.IsNullOrEmpty(idea.CreatedUtc)) idea.CreatedUtc = DateTime.UtcNow.ToString("O");
            if (idea.RelatedIdeaIds == null) idea.RelatedIdeaIds = new List<string>();
            if (idea.Bubble == null) idea.Bubble = new BubbleTransformRecord();
            ActiveSession.Ideas.Add(idea);
            for (int i = 0; i < idea.RelatedIdeaIds.Count; i++)
            {
                string related = idea.RelatedIdeaIds[i];
                if (string.IsNullOrEmpty(related)
                    || !ActiveSession.Ideas.Exists(item => item.IdeaId == related)) continue;
                ActiveSession.Relationships.Add(new IdeaRelationshipRecord
                { SourceIdeaId = idea.IdeaId, TargetIdeaId = related,
                  RelationshipType = idea.RelatedIdeaIds.Count >= 2 ? "combined_from" : "derived_from" });
            }
            return Save();
        }

        public bool TryDeleteSession(int sessionNumber)
        {
            int index = sessions.FindIndex(value => value != null && value.Number == sessionNumber);
            if (index < 0) return false;
            BrainstormSession removed = sessions[index];
            BrainstormSession previousActive = ActiveSession;
            sessions.RemoveAt(index);
            if (ActiveSession == removed) ActiveSession = null;
            if (Save()) return true;
            sessions.Insert(index, removed);
            ActiveSession = previousActive;
            return false;
        }

        public bool AppendConversation(string sessionId, string speaker, string text,
            string messageType, string relatedRequestId, IEnumerable<string> relatedIdeaIds = null)
        {
            if (ActiveSession == null || ActiveSession.SessionId != sessionId
                || string.IsNullOrWhiteSpace(text)) return false;
            ActiveSession.Conversation.Add(new ConversationRecord
            {
                MessageId = "message-" + Guid.NewGuid().ToString("N"),
                Speaker = speaker ?? string.Empty,
                Text = text,
                MessageType = messageType ?? string.Empty,
                RelatedRequestId = relatedRequestId ?? string.Empty,
                RelatedIdeaIds = relatedIdeaIds == null
                    ? new List<string>() : new List<string>(relatedIdeaIds),
                CreatedUtc = DateTime.UtcNow.ToString("O")
            });
            return Save();
        }

        public bool TryUpdateBubbleTransform(string sessionId, string ideaId,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (ActiveSession == null || ActiveSession.SessionId != sessionId
                || string.IsNullOrEmpty(ideaId)) return false;
            BrainstormIdeaRecord idea = ActiveSession.Ideas.Find(item => item.IdeaId == ideaId);
            if (idea == null) return false;
            if (idea.Bubble == null) idea.Bubble = new BubbleTransformRecord();
            idea.Bubble.Position = position;
            idea.Bubble.Rotation = rotation;
            idea.Bubble.Scale = scale;
            return Save();
        }

        public bool TryDeleteIdea(string sessionId, string ideaId)
        {
            if (ActiveSession == null || ActiveSession.SessionId != sessionId
                || string.IsNullOrEmpty(ideaId)) return false;
            int ideaIndex = ActiveSession.Ideas.FindIndex(item => item != null && item.IdeaId == ideaId);
            if (ideaIndex < 0) return false;

            BrainstormIdeaRecord removed = ActiveSession.Ideas[ideaIndex];
            var priorRelationships = new List<IdeaRelationshipRecord>(ActiveSession.Relationships);
            var priorRelatedIds = new Dictionary<BrainstormIdeaRecord, List<string>>();
            for (int i = 0; i < ActiveSession.Ideas.Count; i++)
            {
                BrainstormIdeaRecord item = ActiveSession.Ideas[i];
                if (item != null)
                    priorRelatedIds[item] = item.RelatedIdeaIds == null
                        ? null : new List<string>(item.RelatedIdeaIds);
            }

            ActiveSession.Ideas.RemoveAt(ideaIndex);
            ActiveSession.Relationships.RemoveAll(value => value == null
                || value.SourceIdeaId == ideaId || value.TargetIdeaId == ideaId);
            for (int i = 0; i < ActiveSession.Ideas.Count; i++)
                ActiveSession.Ideas[i]?.RelatedIdeaIds?.RemoveAll(value => value == ideaId);

            if (Save()) return true;

            ActiveSession.Ideas.Insert(ideaIndex, removed);
            ActiveSession.Relationships.Clear();
            ActiveSession.Relationships.AddRange(priorRelationships);
            foreach (var pair in priorRelatedIds)
                pair.Key.RelatedIdeaIds = pair.Value == null ? null : new List<string>(pair.Value);
            return false;
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (ActiveSession != null) ActiveSession.MarkUpdated();
                var document = new SaveDocument { NextSessionNumber = nextSessionNumber,
                    ActiveSessionId = ActiveSession != null ? ActiveSession.SessionId : string.Empty,
                    OnboardingCompleted = OnboardingCompleted, Sessions = new List<BrainstormSession>(sessions) };
                string temporaryPath = savePath + ".temporary";
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                { writer.Write(JsonUtility.ToJson(document, true)); writer.Flush(); stream.Flush(true); }
                if (File.Exists(savePath)) File.Copy(savePath, backupPath, true);
                if (File.Exists(savePath)) File.Delete(savePath);
                File.Move(temporaryPath, savePath);
                LastPersistenceError = string.Empty; return true;
            }
            catch (Exception exception)
            { LastPersistenceError = "Session save failed: " + exception.Message; Debug.LogWarning(LastPersistenceError); return false; }
        }

        private void Load()
        {
            if (TryLoad(savePath) || TryLoad(backupPath)) return;
            sessions.Clear(); ActiveSession = null; nextSessionNumber = 1; OnboardingCompleted = false;
        }
        private bool TryLoad(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                SaveDocument document = JsonUtility.FromJson<SaveDocument>(File.ReadAllText(path));
                if (document == null || document.SchemaVersion < 1 || document.SchemaVersion > CurrentSchemaVersion
                    || document.Sessions == null) throw new InvalidDataException("Unsupported or incomplete session document.");
                sessions.Clear();
                sessions.AddRange(document.Sessions.FindAll(item => item != null && item.Number > 0
                    && !string.IsNullOrEmpty(item.SessionId)));
                int highest = 0; for (int i = 0; i < sessions.Count; i++) highest = Math.Max(highest, sessions[i].Number);
                nextSessionNumber = Math.Max(Math.Max(1, document.NextSessionNumber), highest + 1);
                OnboardingCompleted = document.OnboardingCompleted;
                ActiveSession = sessions.Find(item => item.SessionId == document.ActiveSessionId);
                LastPersistenceError = string.Empty; return true;
            }
            catch (Exception exception)
            { LastPersistenceError = "Session load failed for " + Path.GetFileName(path) + ": " + exception.Message;
                Debug.LogWarning(LastPersistenceError); return false; }
        }
    }
}
