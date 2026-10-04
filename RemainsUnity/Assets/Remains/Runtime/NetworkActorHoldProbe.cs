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
    public struct ActorHoldProbePhase : NetworkMessage {public int phase;}
    public struct ActorHoldProbeDone : NetworkMessage {public bool success;public string detail;}
    public struct ActorHoldProbeObserved : NetworkMessage {public int phase;}
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkActorHoldProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {public bool success,elevated,player,observer,lifted,lowered,released,handoff,ownerRetained,saved,itemsPreserved;public int victimDecisions;public string detail;}
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline;
        private bool traveled,finished,weightRejected,lifted,lowered,released,handoff,saved,capturePending;
        private string originalOwner,items;
        private Vector2 above,below;
        private int observedPhase;
        private float healthBefore;
        private bool Host=>runtime.Options.Role=="host";
        private bool Elevated=>runtime.Options.Smoke.Contains("-elevated-");
        private bool Player=>runtime.Options.Smoke.Contains("-player-");
        private ActorState Body(WorldState world)=>world?.Find(Player?"player:session-client-1":"npc:training-guard");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+70;value.CommandSource=Command;
            if(Elevated)
            {
                var previous=value.Session.Rules;var settings=TelekinesisSettings.Load();settings.maximumMass=80;
                value.Session.ConfigureRules(new GameplayRuleSet(previous.Damage,previous.Knockdown,previous.PartyDefeat,
                    previous.Loot,previous.Pause,previous.Deposit,new MassLimitedTelekinesisRule(settings),
                    previous.ProximityOwnership,previous.DoorAccess));
                Debug.Log("Изолированная проверка переноса актёров использует профиль 80 кг; обычный профиль 30 кг не изменён.");
            }
            value.Session.Notice+=message=>{if(message.StartsWith("Слишком тяжело: 55 кг")) weightRejected=true;};
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ActorHoldProbeObserved>((connection,message)=>
            {
                if(value.Session.ConnectedActor(connection)?.id=="player:session-client-1")
                    observedPhase=Mathf.Max(observedPhase,message.phase);
            });
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ActorHoldProbePhase>(message=>phase=message.phase);
                NetworkClient.RegisterHandler<ActorHoldProbeDone>(message=>
                {
                    if(!Host && Elevated)
                    {
                        Observe();
                        bool valid=lifted && lowered && released && (Player?bodyOwnerRetained:handoff) &&
                            ItemsPreserved() && (!Player || runtime.Combat.VictimDecidedHits>0);
                        Finish(message.success && valid,message.detail+$" Наблюдатель: подъём={lifted}, спуск={lowered}, отпускание={released}, вещи={ItemsPreserved()}, решения урона={runtime.Combat.VictimDecidedHits}.");
                    }
                    else Finish(message.success,message.detail);
                });
            };
        }
        private bool bodyOwnerRetained;
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;var body=Body(runtime.Session.World);
            if(self==null || body==null || finished || self.level!="factory:0") return default;
            if(phase==1)
            {
                float destination=Host?1:9;
                if(Mathf.Abs(self.x-destination)>.08f) return new PlayerCommand {movement=Vector2.right*Mathf.Sign(destination-self.x)};
            }
            // The held player's movement commands must not fight body motion.
            if(!Host) return Player && phase>=3 && phase!=6?new PlayerCommand {movement=Vector2.right}:default;
            var center=new TelekinesisTarget(body).Center;
            if(phase==2 && !Elevated) return new PlayerCommand {telekinesis=true,hasAim=true,aim=center};
            if(phase==2 && !Player && !body.down) return new PlayerCommand {weapon="pistol",hasAim=true,aim=body.Position+Vector2.up*.65f,
                fire=true,reload=WeaponInventory.Available(self,"pistol") && WeaponAmmunition.Magazine(self,"pistol").rounds==0};
            if(phase==3) return new PlayerCommand {telekinesis=true,hasAim=true,aim=center};
            if(phase==4) return new PlayerCommand {telekinesis=true,hasAim=true,aim=above};
            if(phase==5) return new PlayerCommand {telekinesis=true,hasAim=true,aim=below};
            if(phase==7) return new PlayerCommand {telekinesis=true,hasAim=true,aim=new TelekinesisTarget(body).Center,
                weapon="pistol",fire=true};
            return default;
        }
        private void SetPhase(int next) {phase=next;NetworkServer.SendToAll(new ActorHoldProbePhase {phase=next});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Complete(false,$"Таймаут переноса тела: phase={phase}, body={Body(runtime.Session.World)?.Position}.");return;}
            var self=runtime.Session.LocalActor;
            if(self==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=2) return;
            if(!traveled) {traveled=true;runtime.Session.Travel.RequestFactory();}
            if(phase>=1 && items==null) {items=Remains.Serialization.StateJson.ToJson(Body(runtime.Session.World).inventory);originalOwner=Body(runtime.Session.World).owner;PlanTargets();}
            if(!Host) {if(Elevated) Observe();return;}
            var client=runtime.Session.ServerWorld.Find("player:session-client-1");var body=Body(runtime.Session.ServerWorld);
            if(client==null || body==null) return;
            if(phase==0 && self.level=="factory:0" && client.level=="factory:0") SetPhase(1);
            else if(phase==1 && Mathf.Abs(self.x-1)<.15f && Mathf.Abs(client.x-9)<.15f)
            {
                if(body.owner!=client.owner) return;
                originalOwner=body.owner;SetPhase(2);
            }
            else if(phase==2 && !Elevated && weightRejected)
                Complete(body.hold==null,"Обычный профиль: выбран рейдер 55 кг, показан отказ при пределе 30 кг; тело не захвачено.");
            else if(phase==2 && Elevated && (Player || body.down))
            {
                items=Remains.Serialization.StateJson.ToJson(body.inventory);
                PlanTargets();healthBefore=body.health;
                SetPhase(3);
            }
            else if(phase==3 && ActorHoldRules.Active(body) && body.owner==(Player?client.owner:self.owner))
            {handoff=!Player && originalOwner!=body.owner;bodyOwnerRetained=Player && originalOwner==body.owner;SetPhase(4);}
            else if(phase==4 && observedPhase>=4 && Vector2.Distance(new TelekinesisTarget(body).Center,above)<.12f)
            {lifted=true;capturePending=true;SetPhase(5);}
            else if(phase==5 && observedPhase>=5 && Vector2.Distance(new TelekinesisTarget(body).Center,below)<.12f)
            {lowered=true;SetPhase(Player?7:6);}
            else if(phase==7 && body.health<healthBefore && body.health>0) SetPhase(6);
            else if(phase==6 && body.hold==null && ActorTerrainMotion.Supported(runtime.Geometry.Get(body.level),Anatomy.Movement(body),body.Position))
            {
                released=true;runtime.Session.CommitWorld();
                var disk=runtime.Session.Saves.LoadWorld().Find(body.id);
                saved=disk.hold==null && disk.down==body.down && disk.health==body.health && disk.Position==body.Position &&
                    Remains.Serialization.StateJson.ToJson(disk.inventory)==items;
                Complete(lifted && lowered && released && (Player?bodyOwnerRetained:handoff) && saved && self.mana<100,
                    Player?"Тестовый профиль 80 кг: подключённый игрок сохраняет владельца; захват, подъём/спуск при его вводе движения, попадание с решением пострадавшего, отпускание и сохранение.":
                    "Тестовый профиль 80 кг: обычная стрельба, нокдаун рейдера, захват того же тела, передача владельца, подъём/спуск через балку, отпускание, здоровье/вещи и сохранение.");
            }
        }
        private void PlanTargets()
        {
            var body=Body(runtime.Session.World);var surface=runtime.Geometry.Get(body.level).platforms.First(s=>s.oneWay && s.width>2);
            float height=surface.y+surface.height+Anatomy.Movement(body).height*.5f;
            above=new Vector2(surface.x+1,height+1);below=new Vector2(surface.x+1,height-1);
        }
        private bool ItemsPreserved()=>items!=null && Remains.Serialization.StateJson.ToJson(Body(runtime.Session.World)?.inventory)==items;
        private void Observe()
        {
            var body=Body(runtime.Session.World);if(body==null) return;
            if(ActorHoldRules.Active(body))
            {
                if(body.owner==(Player?originalOwner:runtime.Session.World.Find("player:session-host").owner))
                {bodyOwnerRetained=Player;handoff=!Player;}
                if(!lifted && Vector2.Distance(new TelekinesisTarget(body).Center,above)<.2f)
                {lifted=true;NetworkClient.Send(new ActorHoldProbeObserved {phase=4});}
                if(lifted && !lowered && Vector2.Distance(new TelekinesisTarget(body).Center,below)<.2f)
                {lowered=true;NetworkClient.Send(new ActorHoldProbeObserved {phase=5});}
            }
            if(lowered && body.hold==null && ActorTerrainMotion.Supported(runtime.Geometry.Get(body.level),Anatomy.Movement(body),body.Position)) released=true;
        }
        private void LateUpdate()
        {
            if(!capturePending || !runtime.Options.Smoke.Contains("-frames-")) return;
            capturePending=false;
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".held.png",runtime.Hud?.GetComponent<Canvas>());
        }
        private void Complete(bool success,string detail)
        {
            if(!Host) {Finish(false,detail);return;}
            NetworkServer.SendToAll(new ActorHoldProbeDone {success=success,detail=detail});
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,elevated=Elevated,
                player=Player,observer=!Host,lifted=lifted,lowered=lowered,released=released,handoff=handoff,
                ownerRetained=bodyOwnerRetained,saved=saved,itemsPreserved=ItemsPreserved(),victimDecisions=runtime.Combat.VictimDecidedHits,detail=detail},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
    }
}
