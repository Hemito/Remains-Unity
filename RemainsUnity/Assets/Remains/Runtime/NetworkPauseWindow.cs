using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct PauseWindowRequest : NetworkMessage {public string id,level,label;}
    public struct PauseWindowReply : NetworkMessage {public string id,detail;public bool valid;}
    public struct PauseWindowAbort : NetworkMessage {public string detail;}

    // Shared test barrier: each process verifies its own simulation before the host resumes.
    public sealed class NetworkPauseWindow
    {
        private readonly MonoBehaviour runner;
        private readonly CoopSession session;
        private readonly int peers;
        private readonly Func<float[]> snapshot;
        private readonly Func<bool> valid,stopped;
        private readonly Action<string> failure;
        private readonly HashSet<int> replies=new();
        private string current;
        private bool aborted;
        public int Verified {get;private set;}
        public NetworkPauseWindow(MonoBehaviour runner,CoopSession session,int peers,Func<float[]> snapshot,
            Func<bool> valid,Func<bool> stopped,Action<string> failure)
        {
            this.runner=runner;this.session=session;this.peers=peers;this.snapshot=snapshot;
            this.valid=valid;this.stopped=stopped;this.failure=failure;
            session.ServerReady+=()=>NetworkServer.RegisterHandler<PauseWindowReply>((connection,message)=>
            {
                if(message.id!=current) return;
                if(!message.valid) {Reject(message.detail);return;}
                replies.Add(connection.connectionId);
            });
            session.ClientReady+=()=>
            {
                Verified=0;aborted=false;
                NetworkClient.RegisterHandler<PauseWindowRequest>(message=>runner.StartCoroutine(Check(message)));
                NetworkClient.RegisterHandler<PauseWindowAbort>(message=>{aborted=true;failure(message.detail);});
            };
        }
        private bool Stopped=>aborted || stopped();
        public IEnumerator Run(string label)
        {
            current=Guid.NewGuid().ToString("N");replies.Clear();
            string level=session.LocalActor.level;
            session.Pause.SetManual(true);
            while(!session.Pause.IsPaused(level) && !Stopped) yield return null;
            if(Stopped) yield break;
            if(!valid()) {Reject("Пауза не застала активное взаимодействие: "+label);yield break;}
            NetworkServer.SendToAll(new PauseWindowRequest {id=current,level=level,label=label});
            while(replies.Count!=peers && !Stopped) yield return null;
            if(Stopped) yield break;
            session.Pause.SetManual(false);
            while(session.Pause.IsPaused(level) && !Stopped) yield return null;
            current=null;
        }
        private IEnumerator Check(PauseWindowRequest request)
        {
            yield return new WaitForSecondsRealtime(.6f);
            if(Stopped) yield break;
            bool beforeValid=session.LocalActor!=null && session.Pause.IsPaused(request.level) && valid();
            var before=beforeValid?snapshot():null;
            yield return new WaitForSecondsRealtime(.6f);
            if(Stopped) yield break;
            bool result=beforeValid && session.LocalActor!=null && session.Pause.IsPaused(request.level) && valid() && before.SequenceEqual(snapshot());
            if(result) Verified++;
            NetworkClient.Send(new PauseWindowReply {id=request.id,valid=result,
                detail=$"Взаимодействие изменилось во время паузы: {request.label}, actor={session.LocalActorId}."});
        }
        private void Reject(string detail)=>NetworkServer.SendToAll(new PauseWindowAbort {detail=detail});
    }
}
