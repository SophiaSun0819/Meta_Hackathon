using System;
using System.Collections.Generic;

namespace ThinkingSpace.UI
{
    [Serializable]
    public sealed class BrainstormSession
    {
        public int Number { get; }

        public BrainstormSession(int number)
        {
            Number = number;
        }
    }

    public sealed class BrainstormSessionStore
    {
        private readonly List<BrainstormSession> sessions = new List<BrainstormSession>();

        public IReadOnlyList<BrainstormSession> Sessions => sessions;
        public BrainstormSession ActiveSession { get; private set; }

        public BrainstormSession StartNewSession()
        {
            BrainstormSession session = new BrainstormSession(sessions.Count + 1);
            sessions.Add(session);
            ActiveSession = session;
            return session;
        }

        public bool ReopenSession(int number)
        {
            BrainstormSession session = sessions.Find(item => item.Number == number);
            if (session == null) return false;
            ActiveSession = session;
            return true;
        }

        public void EndActiveSession()
        {
            ActiveSession = null;
        }
    }
}
