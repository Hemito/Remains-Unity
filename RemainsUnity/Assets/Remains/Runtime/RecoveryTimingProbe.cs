using System;
using System.IO;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in smoke diagnostics. All timestamps use the host's monotonic clock.
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
            session.RecoveryAcknowledged+=Acknowledged;
            session.RecoveryPeerLeft+=Left;
        }
        private void Prepared(string snapshot,long revision)=>Write("prepared",-1,snapshot,revision);
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
            session.RecoveryAcknowledged-=Acknowledged;
            session.RecoveryPeerLeft-=Left;
        }
    }
}
