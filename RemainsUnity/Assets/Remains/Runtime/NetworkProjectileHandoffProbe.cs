using System;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ProjectileHandoffFinish : NetworkMessage {public string id;}
    // Ordinary movement/weapon commands and a real client disconnect. No trajectory mutation.
    public sealed class NetworkProjectileHandoffProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private bool traveled,fired,disconnected,reconnected,finished,transferred;
        private string flightId;
        private float deadline,reconnectAt,finishTime,retained;
        private int launches,generation;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            value.Session.AttackReceived+=attack=>
            {if(attack.weapon=="pistol" && attack.attacker=="player:session-client-1") {launches++;flightId=attack.id;}};
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<ProjectileHandoffFinish>(message=>
            {
                var flight=value.Session.World?.projectiles.Find(p=>p.launch.id==message.id);
                if(flight==null || !flight.finished || launches!=1) Finish(false,"После подключения отсутствует завершённый полёт или повторён запуск.");
                else Finish(true,"Незавершённый свободный полёт передан один раз, завершён и сохранён; подключение не повторяет выстрел.");
            });
        }
        private PlayerCommand Command()
        {
            var session=runtime.Session;var actor=session.LocalActor;
            if(Host || fired || actor?.level!="factory:0" || session.World.actors.Count(a=>a.player && a.connected && a.level==actor.level)!=2) return default;
            fired=true;
            return new PlayerCommand {weapon="pistol",fire=true,hasAim=true,aim=actor.Position+Vector2.up*100};
        }
        private void Update()
        {
            if(finished) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: fired={fired}, disconnected={disconnected}, transferred={transferred}, id={flightId}.");return;}
            if(!Host && disconnected && !reconnected && Time.unscaledTime>=reconnectAt)
            {reconnected=true;session.StartClient();return;}
            if(session.LocalActor==null) return;
            if(!traveled && session.World.actors.Count(a=>a.player && a.connected)==2)
            {traveled=true;session.Travel.RequestFactory();}
            if(!Host)
            {
                var flight=session.World.projectiles.Find(p=>p.launch.id==flightId);
                if(!disconnected && flight is {finished:false} && flight.traveled>2)
                {
                    retained=flight.traveled;generation=flight.generation;
                    disconnected=true;reconnectAt=Time.unscaledTime+1;session.StopClient();
                }
                return;
            }
            var canonical=session.ServerWorld.projectiles.Find(p=>p.launch.id==flightId);
            if(canonical==null) return;
            if(canonical.generation==1) {retained=Mathf.Max(retained,canonical.traveled);generation=canonical.generation;}
            else if(!transferred)
            {
                if(canonical.finished || canonical.owner!=session.LocalOwner || canonical.traveled<retained || retained<=0)
                {Finish(false,"Передача потеряла прогресс, выбрала неверного владельца или произошла после завершения.");return;}
                transferred=true;generation=canonical.generation;
            }
            if(!transferred || !canonical.finished) return;
            if(finishTime==0) finishTime=Time.unscaledTime;
            if(Time.unscaledTime-finishTime<4 || session.World.actors.Count(a=>a.player && a.connected)!=2) return;
            var saved=session.Saves.LoadWorld()?.projectiles.Find(p=>p.launch.id==flightId);
            float range=Vector2.Distance(canonical.launch.Origin,canonical.launch.End);
            if(launches!=1 || canonical.generation!=generation || Mathf.Abs(canonical.traveled-range)>.001f ||
                session.ServerWorld.projectiles.Count(p=>p.launch.id==flightId)!=1 || saved==null || !saved.finished || saved.traveled!=canonical.traveled)
            {Finish(false,"Повтор запуска/владения или несохранённое завершение полёта.");return;}
            NetworkServer.SendToAll(new ProjectileHandoffFinish {id=flightId});
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,id=flightId,
                launches=launches,retained=retained,generation=generation,transferred=transferred,disconnected=disconnected,reconnected=reconnected},true));
            Debug.Log(detail);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report
        {public bool success,transferred,disconnected,reconnected;public string detail,id;public int launches,generation;public float retained;}
    }
}
