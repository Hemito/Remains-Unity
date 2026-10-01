using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    public struct FreezeProbeFinish : NetworkMessage {public bool success;public string detail;}
    // Only ordinary travel commands: no NPC or player state is injected.
    public sealed class NetworkFreezeProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,shots;
        private float deadline,frozenAt,remaining,poseTime;
        private string attackId;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=()=>default;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FreezeProbeFinish>(m=>Finish(m.success,m.detail));
            value.Session.AttackReceived+=attack=>{if(attack.id==attackId) shots++;};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут заморозки: phase={phase}, shots={shots}.");return;}
            if(runtime.Options.Role!="host") return;
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            var npc=session.World.Find("npc:training-guard");
            if(phase==0) {session.Travel.RequestFactory();phase=1;return;}
            if(phase==1 && self.level=="factory:0" && npc.attackBusy)
            {attackId=npc.attackId;session.Travel.RequestHub();phase=2;return;}
            if(phase==2 && self.level=="hub" && !session.Travel.Pending)
            {
                if(!npc.attackBusy || npc.attackId!=attackId || npc.attackRemaining<=0)
                {Complete(false,"Не удалось уйти до завершения атаки NPC.");return;}
                frozenAt=Time.unscaledTime;remaining=npc.attackRemaining;poseTime=npc.poseTime;phase=3;
            }
            if(phase==3)
            {
                if(LevelActivity.HasConnectedPlayer(session.World,"factory:0") || !npc.attackBusy || npc.attackId!=attackId ||
                    Mathf.Abs(npc.attackRemaining-remaining)>.001f || Mathf.Abs(npc.poseTime-poseTime)>.001f || shots!=0)
                {Complete(false,"NPC изменил ход атаки/анимации в пустой локации.");return;}
                if(Time.unscaledTime-frozenAt<2) return;
                var saved=session.Saves.LoadWorld(prepareForPlay:false)?.Find(npc.id);
                if(saved==null || !saved.attackBusy || saved.attackId!=attackId || saved.attackRemaining<=0)
                {Complete(false,"Сохранение не содержит незавершённую атаку пустого уровня.");return;}
                session.Travel.RequestFactory();phase=4;
            }
            if(phase==4 && self.level=="factory:0" && shots>0)
            {
                bool valid=shots==1 && session.World.actors.Where(a=>a.player && a.id!=self.id).All(a=>a.level=="hub" && a.health==100);
                Complete(valid,valid?"NPC замер на 2 секунды посреди атаки в пустом уровне; состояние записано, после возвращения та же атака завершилась один раз. Другие игроки остались в хабе.":"Повтор атаки или изменение игроков хаба.");
            }
        }
        private void Complete(bool success,string detail)
        {phase=5;NetworkServer.SendToAll(new FreezeProbeFinish {success=success,detail=detail});}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
