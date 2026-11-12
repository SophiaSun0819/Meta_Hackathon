using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    [Serializable] public sealed class OrbyVoiceCacheEntry
    { public string Key, FileName, SessionId, CreatedUtc, LastAccessUtc; public long Bytes; }
    [Serializable] internal sealed class OrbyVoiceCacheIndex
    { public int SchemaVersion=1; public List<OrbyVoiceCacheEntry> Entries=new List<OrbyVoiceCacheEntry>(); }

    public sealed class OrbyVoiceCache
    {
        public const long DefaultMaximumBytes=50L*1024L*1024L;
        public const int DefaultMaximumAgeDays=30;
        private readonly string directory,indexPath,backupPath;
        private readonly long maximumBytes;
        private readonly TimeSpan maximumAge;
        private OrbyVoiceCacheIndex index=new OrbyVoiceCacheIndex();
        public string LastError { get; private set; }=string.Empty;

        public OrbyVoiceCache(string root,long maxBytes=DefaultMaximumBytes,int maxAgeDays=DefaultMaximumAgeDays)
        {directory=Path.Combine(root,"VoiceCache");indexPath=Path.Combine(directory,"index-v1.json");backupPath=indexPath+".backup";maximumBytes=maxBytes;maximumAge=TimeSpan.FromDays(maxAgeDays);Load();Prune();}

        public static string CreateKey(string text,string identity,string sessionId)
        {
            using(SHA256 hash=SHA256.Create())
            {byte[] bytes=hash.ComputeHash(Encoding.UTF8.GetBytes((identity??"")+"\n"+(sessionId??"")+"\n"+(text??"")));return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        }
        public bool TryGetPath(string key,out string path)
        {
            path=null;OrbyVoiceCacheEntry entry=index.Entries.Find(value=>value.Key==key);if(entry==null)return false;
            string candidate=Path.Combine(directory,entry.FileName);
            if(!File.Exists(candidate)||new FileInfo(candidate).Length!=entry.Bytes){index.Entries.Remove(entry);Save();return false;}
            entry.LastAccessUtc=DateTime.UtcNow.ToString("O");Save();path=candidate;return true;
        }
        public bool Store(string key,string sessionId,byte[] mp3)
        {
            if(string.IsNullOrEmpty(key)||mp3==null||mp3.Length<4)return false;
            try{Directory.CreateDirectory(directory);string file=key+".mp3",target=Path.Combine(directory,file),temp=target+".tmp";File.WriteAllBytes(temp,mp3);if(File.Exists(target))File.Delete(target);File.Move(temp,target);
                index.Entries.RemoveAll(value=>value.Key==key);string now=DateTime.UtcNow.ToString("O");index.Entries.Add(new OrbyVoiceCacheEntry{Key=key,FileName=file,SessionId=sessionId??"",CreatedUtc=now,LastAccessUtc=now,Bytes=mp3.Length});Prune();return Save();}
            catch(Exception e){LastError=e.Message;return false;}
        }
        public void RemoveSession(string sessionId)
        {foreach(var entry in index.Entries.FindAll(value=>value.SessionId==sessionId))DeleteFile(entry);index.Entries.RemoveAll(value=>value.SessionId==sessionId);Save();}
        public void Clear(){foreach(var entry in index.Entries)DeleteFile(entry);index.Entries.Clear();Save();}
        public int Count=>index.Entries.Count;
        private void Prune()
        {
            DateTime cutoff=DateTime.UtcNow-maximumAge;
            foreach(var entry in index.Entries.FindAll(value=>Parse(value.CreatedUtc)<cutoff||!File.Exists(Path.Combine(directory,value.FileName))))DeleteFile(entry);
            index.Entries.RemoveAll(value=>Parse(value.CreatedUtc)<cutoff||!File.Exists(Path.Combine(directory,value.FileName)));
            index.Entries.Sort((a,b)=>Parse(a.LastAccessUtc).CompareTo(Parse(b.LastAccessUtc)));long total=0;foreach(var e in index.Entries)total+=e.Bytes;
            while(total>maximumBytes&&index.Entries.Count>0){var e=index.Entries[0];total-=e.Bytes;DeleteFile(e);index.Entries.RemoveAt(0);}Save();
        }
        private void Load(){Directory.CreateDirectory(directory);if(TryLoad(indexPath)||TryLoad(backupPath))return;index=new OrbyVoiceCacheIndex();}
        private bool TryLoad(string path){try{if(!File.Exists(path))return false;OrbyVoiceCacheIndex loaded=JsonUtility.FromJson<OrbyVoiceCacheIndex>(File.ReadAllText(path));if(loaded==null||loaded.SchemaVersion!=1)return false;if(loaded.Entries==null)loaded.Entries=new List<OrbyVoiceCacheEntry>();index=loaded;return true;}catch{return false;}}
        private bool Save(){try{Directory.CreateDirectory(directory);string temp=indexPath+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(index,true));if(File.Exists(indexPath))File.Copy(indexPath,backupPath,true);if(File.Exists(indexPath))File.Delete(indexPath);File.Move(temp,indexPath);LastError="";return true;}catch(Exception e){LastError=e.Message;return false;}}
        private void DeleteFile(OrbyVoiceCacheEntry entry){try{string path=Path.Combine(directory,entry.FileName);if(File.Exists(path))File.Delete(path);}catch{}}
        private static DateTime Parse(string value)=>DateTime.TryParse(value,out DateTime parsed)?parsed:DateTime.MinValue;
    }
}
