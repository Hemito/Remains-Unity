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
        private readonly OrderedSaveWrites journal=new();
        public void Initialize(CoopSession source,string report)
        {
            session=source;path=report+".recovery.jsonl";
            File.WriteAllText(path,"");
            session.RecoveryCaptured+=Prepared;
            session.RecoveryPersisted+=Persisted;
            session.RecoveryAcknowledged+=Acknowledged;
            session.RecoveryPeerLeft+=Left;
        }
        private void Prepared(RecoverySnapshot snapshot)=>Write("prepared",-1,null,snapshot.Revision,snapshot);
        private void Persisted(RecoverySnapshot snapshot)=>Write("persisted",-1,snapshot.Id,snapshot.Revision);
        private void Acknowledged(int peer,string snapshot,long revision)=>Write("ack",peer,snapshot,revision);
        private void Left(int peer)=>Write("left",peer,null,0);
        private void Write(string kind,int peer,string snapshot,long revision,RecoverySnapshot captured=null)
        {
            var entry=new Entry {kind=kind,peer=peer,snapshot=snapshot,revision=revision,seconds=Time.realtimeSinceStartupAsDouble};
            journal.Enqueue(()=>
            {
                if(captured!=null) entry.snapshot=captured.Id;
                File.AppendAllText(path,Remains.Serialization.StateJson.ToJson(entry)+Environment.NewLine);
            },null,error=>Debug.LogWarning("Не записана диагностика сохранения: "+error.Message));
        }
        private void Update()=>journal.Pump();
        private void OnDestroy()
        {
            if(session==null) return;
            session.RecoveryCaptured-=Prepared;
            session.RecoveryPersisted-=Persisted;
            session.RecoveryAcknowledged-=Acknowledged;
            session.RecoveryPeerLeft-=Left;
            try {journal.Flush();}
            catch(IOException error) {Debug.LogWarning("Не завершена диагностика сохранений: "+error.Message);}
        }
    }
}
