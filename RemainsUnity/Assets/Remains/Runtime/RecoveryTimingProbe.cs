using System;
using System.IO;
using Remains.Networking;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in smoke diagnostics. Each process uses its own monotonic clock;
    // cross-process absolute timestamps are not a shared timeline.
    // Prepared means captured; persisted means its queued disk publication completed.
    // An acknowledged snapshot has already been persisted by the remote client.
    public sealed class RecoveryTimingProbe : MonoBehaviour
    {
        [Serializable] private sealed class Entry
        {
            public string kind,snapshot;
            public int peer;
            public long revision;
            public double seconds;
        }
        private CoopSession session;
        private string path;
        public void Initialize(CoopSession source,string report)
        {
            session=source;path=report+".recovery.jsonl";
            File.WriteAllText(path,"");
            session.RecoveryPrepared+=Prepared;
            session.RecoveryPersisted+=Persisted;
            session.RecoveryAcknowledged+=Acknowledged;
            session.RecoveryPeerLeft+=Left;
        }
        private void Prepared(string snapshot,long revision)=>Write("prepared",-1,snapshot,revision);
        private void Persisted(RecoverySnapshot snapshot)=>Write("persisted",-1,snapshot.Id,snapshot.Revision);
        private void Acknowledged(int peer,string snapshot,long revision)=>Write("ack",peer,snapshot,revision);
        private void Left(int peer)=>Write("left",peer,null,0);
        private void Write(string kind,int peer,string snapshot,long revision)
        {
            var entry=new Entry {kind=kind,peer=peer,snapshot=snapshot,revision=revision,seconds=Time.realtimeSinceStartupAsDouble};
            File.AppendAllText(path,JsonUtility.ToJson(entry)+Environment.NewLine);
        }
        private void OnDestroy()
        {
            if(session==null) return;
            session.RecoveryPrepared-=Prepared;
            session.RecoveryPersisted-=Persisted;
            session.RecoveryAcknowledged-=Acknowledged;
            session.RecoveryPeerLeft-=Left;
        }
    }
}
