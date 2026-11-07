using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ProjectileHandoffFinish : NetworkMessage {public string id;}
    public struct ProjectileHandoffReceipt : NetworkMessage {public string id;}
    // Ordinary movement/weapon commands and a real client disconnect. No trajectory mutation.
    public sealed class NetworkProjectileHandoffProbe : MonoBehaviour
    {
        private const float MinimumFreeFlight=10;
        private PrototypeRuntime runtime;
        private bool traveled,fired,disconnected,reconnected,finished,transferred;
        private bool finishSent,closing;
        private string flightId;
        private float deadline,reconnectAt,finishTime,retained,expectedDistance,finishedDistance;
        private float readySince;
        private int steadyFrames;
        private Vector2 shotOrigin,shotEnd;
        private int launches,generation;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            value.Session.AttackReceived+=ObserveLaunch;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ProjectileHandoffReceipt>((connection,message)=>
            {
                if(finishSent && message.id==flightId && value.Session.ConnectedActor(connection)?.id=="player:session-client-1")
                    BeginFinish();
            });
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<ProjectileHandoffFinish>(message=>
            {
                if(Host || closing) return;
                var flight=value.Session.World?.projectiles.Find(p=>p.launch.id==message.id);
                if(flight==null || !flight.finished || launches!=1) Finish(false,"После подключения отсутствует завершённый полёт или повторён запуск.");
                else {NetworkClient.Send(new ProjectileHandoffReceipt {id=message.id});BeginFinish();}
            });
        }
        private void BeginFinish()
        {if(closing) return;closing=true;StartCoroutine(FinishAfterDelivery());}
        private IEnumerator FinishAfterDelivery()
        {
            yield return new WaitForSecondsRealtime(1);
            Finish(true,"Незавершённый свободный полёт передан один раз, завершён и сохранён; подключение не повторяет выстрел.");
        }
        private PlayerCommand Command()
        {
            var session=runtime.Session;var actor=session.LocalActor;
            if(Host || fired || actor?.level!="factory:0" || session.World.actors.Count(a=>a.player && a.connected && a.level==actor.level)!=2) return default;
            if(actor.weapon!="pistol") return new PlayerCommand {weapon="pistol"};
            // Deferred asset unloading after entry can advance an entire short flight in a single frame.
            if(readySince==0) readySince=Time.unscaledTime;
            steadyFrames=Time.unscaledDeltaTime<=1f/30?steadyFrames+1:0;
            if(Time.unscaledTime-readySince<2 || steadyFrames<12) return default;
            var aim=FindAim(actor,out float free);
            if(free<MinimumFreeFlight)
            {
                Finish(false,$"У точки входа нет свободного участка для передачи снаряда: actor={actor.Position}, найдено {free:F2} м.");
                return default;
            }
            fired=true;
            return new PlayerCommand {fire=true,hasAim=true,aim=aim};
        }
        private Vector2 FindAim(ActorState actor,out float bestDistance)
        {
            var room=runtime.Geometry.Get(actor.level);
            var definition=WeaponCatalog.Get(actor.weapon);
            var copy=actor.Copy();copy.Position=actor.BodyPosition;
            var anchor=WeaponPose.RangedAnchor(copy);
            var best=anchor;bestDistance=0;
            for(int degrees=0;degrees<360;degrees+=15)
            {
                var aim=anchor+Direction(degrees)*100;
                WeaponPose.Aim(copy,aim,room);
                var pose=WeaponPose.SampleRanged(copy);
                if(ShotObstacles.FirstContact(room,WeaponPose.RangedAnchor(copy),pose.Muzzle).HasValue) continue;
                float distance=definition.FlightRange(definition.ProjectileSpeedRange.x);
                // Reserve room for the authored pistol spread before issuing the real command.
                float free=Mathf.Min(FreeDistance(room,pose.Muzzle,pose.Angle,distance),
                    FreeDistance(room,pose.Muzzle,pose.Angle-definition.shotSpreadHalfAngleDegrees,distance),
                    FreeDistance(room,pose.Muzzle,pose.Angle+definition.shotSpreadHalfAngleDegrees,distance));
                if(free>bestDistance) {bestDistance=free;best=aim;}
            }
            return best;
        }
        private static Vector2 Direction(float degrees)=>new(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad));
        private static float FreeDistance(RoomLayout room,Vector2 origin,float degrees,float distance)
            =>distance*ShotObstacles.Limit(room,origin,origin+Direction(degrees)*distance);
        private void ObserveLaunch(AttackEvent attack)
        {
            if(attack.weapon!="pistol" || attack.attacker!="player:session-client-1") return;
            launches++;flightId=attack.id;shotOrigin=attack.Origin;shotEnd=attack.End;
            float range=Vector2.Distance(shotOrigin,shotEnd);
            var room=runtime.Geometry.Get(attack.level);
            float limit=ShotObstacles.Limit(room,shotOrigin,shotEnd);
            expectedDistance=range*limit;
            if(expectedDistance<MinimumFreeFlight || AttackContacts.VisualLimit(runtime.Session.World.actors,attack,
                runtime.Session.World.friendlyFire,limit)<limit)
                Finish(false,$"Реальный выстрел не попал в подготовленный свободный участок: {shotOrigin}→{shotEnd}, {expectedDistance:F2} м.");
        }
        private void Update()
        {
            if(finished || closing) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: fired={fired}, disconnected={disconnected}, transferred={transferred}, id={flightId}.");return;}
            if(finishSent) return;
            if(!Host && disconnected && !reconnected && Time.unscaledTime>=reconnectAt)
            {reconnected=true;session.StartClient();return;}
            if(session.LocalActor==null) return;
            if(!traveled && session.World.actors.Count(a=>a.player && a.connected)==2)
            {traveled=true;session.Travel.RequestFactory();}
            if(!Host)
            {
                var flight=session.World.projectiles.Find(p=>p.launch.id==flightId);
                if(!disconnected && flight is {finished:true})
                {Finish(false,$"Клиент не получил промежуточный прогресс до конца полёта: {flight.traveled:F2} м.");return;}
                if(!disconnected && flight is {finished:false} && flight.traveled>2)
                {
                    retained=flight.traveled;generation=flight.generation;
                    disconnected=true;reconnectAt=Time.unscaledTime+1;session.StopClient();
                }
                return;
            }
            var canonical=session.ServerWorld.projectiles.Find(p=>p.launch.id==flightId);
            if(canonical==null) return;
            if(canonical.generation==1)
            {
                if(canonical.finished) {Finish(false,$"Снаряд завершился у исходного владельца: {canonical.traveled:F2} м.");return;}
                retained=Mathf.Max(retained,canonical.traveled);generation=canonical.generation;
            }
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
            finishedDistance=canonical.traveled;
            if(launches!=1 || canonical.generation!=generation || Mathf.Abs(finishedDistance-expectedDistance)>.02f ||
                session.ServerWorld.projectiles.Count(p=>p.launch.id==flightId)!=1 || saved==null || !saved.finished || saved.traveled!=canonical.traveled)
            {Finish(false,"Повтор запуска/владения или несохранённое завершение полёта.");return;}
            finishSent=true;NetworkServer.SendToAll(new ProjectileHandoffFinish {id=flightId});
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,id=flightId,
                launches=launches,retained=retained,generation=generation,transferred=transferred,disconnected=disconnected,reconnected=reconnected,
                shotOrigin=shotOrigin,shotEnd=shotEnd,expectedDistance=expectedDistance,finishedDistance=finishedDistance},true));
            Debug.Log(detail);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report
        {public bool success,transferred,disconnected,reconnected;public string detail,id;public int launches,generation;public float retained,expectedDistance,finishedDistance;public Vector2 shotOrigin,shotEnd;}
    }
}
