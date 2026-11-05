using System.Collections.Generic;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    // Unity requires ScriptableObject classes to be declared in a same-named
    // source file so generated assets receive a valid MonoScript reference.
    public sealed class OrbyPrerecordedManifest : ScriptableObject
    {
        public List<OrbyPrerecordedEntry> Entries = new List<OrbyPrerecordedEntry>();

        public bool TryGet(string key, out OrbyPrerecordedEntry entry)
        {
            entry=Entries.Find(value=>value!=null&&value.EventKey==key);
            return entry!=null&&entry.Clip!=null;
        }
    }
}
