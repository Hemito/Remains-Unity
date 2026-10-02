using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct JourneyPhase : NetworkMessage {public int value;}
    public struct JourneyReady : NetworkMessage { }
    public struct JourneyFrameReady : NetworkMessage {public int phase;}
    public struct JourneyFinish : NetworkMessage {public bool success;public string detail;}

    // One world and character set, ordinary commands only; no gameplay state injection.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkJourneyProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1,bodyGeneration;
        private float deadline,since,reconnectAt,nextHeal;
        private bool action,disconnected,reconnected,sawBody,finished,ready;
        private string worldId;
        private JourneyWeaponStage weapons;
        private JourneyInteractionStage interactions;
        private readonly JourneyHubRoute initialRoute=new();
        private readonly HashSet<int> readyPeers=new();
        private readonly List<int> completed=new();
        private readonly HashSet<int> captured=new();
        private readonly HashSet<string> frameAcks=new();
        private bool Frames=>runtime.Options.Smoke.Contains("-frames-");
        private static bool FramePhase(int value)=>value==2 || value==3 || value==17 || value==9;
        private bool FramesReady(int value)=>!Frames || !FramePhase(value) ||
            frameAcks.Count(key=>key.StartsWith(value+":"))==runtime.Options.ProbePeers;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+100;runtime.CommandSource=Command;
            weapons=new JourneyWeaponStage(value);
            interactions=new JourneyInteractionStage(value);
            if(Resume)
                worldId=value.Session.Saves.LoadWorld(false)?.worldId??throw new InvalidOperationException("Нет мира для продолжения journey.");
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<JourneyPhase>(m=>{phase=m.value;since=Time.unscaledTime;action=false;});
                NetworkClient.RegisterHandler<JourneyFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<JourneyReady>((connection,_)=>
            {if(value.Session.ConnectedActor(connection)!=null) readyPeers.Add(connection.connectionId);});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<JourneyFrameReady>((connection,message)=>
            {if(value.Session.ConnectedActor(connection)!=null) frameAcks.Add(message.phase+":"+connection.connectionId);});
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null || Resume) return default;
            if(phase==2 && Host && !FramesReady(2)) return default;
            if(phase>=12 && phase<=14) return weapons.Command(phase);
            if(phase>=15 && phase<=21) return interactions.Command(phase);
            if(phase>=3 && phase<=9 && !self.down && self.health<70 && Time.unscaledTime>=nextHeal && InventoryJournal.Count(self,"heal")>0)
            {nextHeal=Time.unscaledTime+1;return new PlayerCommand {heal=true};}
            if(phase==0)
            {
                float target=Host?5:Source?6:7+int.Parse(self.character.Split('-').Last());
                return initialRoute.Command(self,target);
            }
            if(phase==1 && Host) return new PlayerCommand {weapon="pistol",fire=!Victim.down,hasAim=true,aim=Victim.Position+Vector2.up*.55f};
            if((phase==2 || phase==7) && Host && Victim.down)
            {
                float delta=Victim.x-self.x;
                return new PlayerCommand {revive=Mathf.Abs(delta)<1.5f,movement=Vector2.right*(Mathf.Abs(delta)>1.2f?Mathf.Sign(delta):0)};
            }
            if(phase==5 && !runtime.Hud.InventoryOpen) return new PlayerCommand {inventory=true};
            if(phase>=6 && runtime.Hud.InventoryOpen) return new PlayerCommand {inventory=true};
            return default;
        }
        private void Advance(int next)
        {
            if(!FramesReady(phase)) return;
            completed.Add(phase);phase=next;since=Time.unscaledTime;action=false;
            NetworkServer.SendToAll(new JourneyPhase {value=next});
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут journey: phase={phase}, body={sawBody}, hp={Healer?.health}/{Victim?.health}.");return;}
            var session=runtime.Session;
            if(Source && disconnected && !reconnected && Time.unscaledTime>=reconnectAt)
            {reconnected=true;session.StartClient();return;}
            var self=session.LocalActor;if(self==null) return;
            string expectedMusic=self.level=="hub"?"hub":"factory";
            if(runtime.Music.CurrentTrack!=expectedMusic) {Finish(false,$"Музыка не соответствует локации: {self.level}, track={runtime.Music.CurrentTrack}.");return;}
            worldId??=session.World.worldId;
            if(session.World.worldId!=worldId) {Finish(false,"Journey сменил WorldId.");return;}
            var players=session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(phase<0 && Host && players.Length==runtime.Options.ProbePeers) {Advance(Resume?10:0);return;}
            if((phase==3 && Host || phase==4 && !Host || phase==8) && !action && !self.down &&
                !session.Travel.Pending && runtime.Hud.FactoryButtonRect.GetComponent<UnityEngine.UI.Button>().IsInteractable())
            {action=true;StartCoroutine(UiPointerProbe.Click(runtime.Hud.FactoryButtonRect));}
            if(phase==5 && !action && runtime.Hud.InventoryOpen)
            {action=true;StartCoroutine(Claim());}
            if(phase==6 && Source && !disconnected)
            {disconnected=true;reconnectAt=Time.unscaledTime+3;session.StopClient();return;}
            if(phase==9 || phase==10)
            {
                if(Time.unscaledTime-since<4 || players.Length!=runtime.Options.ProbePeers) return;
                if(!ValidateFinal()) {Finish(false,"Journey потерял предметы, тело, прогресс или сохранение персонажа.");return;}
                if(!ready) {ready=true;NetworkClient.Send(new JourneyReady());}
                if(Host && readyPeers.Count==runtime.Options.ProbePeers && FramesReady(phase))
                    NetworkServer.SendToAll(new JourneyFinish {success=true,detail=Resume?
                        "Перезапуск того же мира сохранил персонажей, лут и расход лечения.":
                        "Один мир: бой/поднятие, разделение уровней, общий подбор, отключённое тело/возвращение/поднятие, хаб и сохранение."});
                return;
            }
            if(!Host || Time.unscaledTime-since<.5f) return;
            if(phase>=15 && phase<=21)
            {
                try {int next=interactions.Next(phase);if(next!=phase) Advance(next);}
                catch(Exception error) {NetworkServer.SendToAll(new JourneyFinish {success=false,detail=error.Message});}
                return;
            }
            if(phase>=12 && phase<=14)
            {
                if(Time.unscaledTime-since<2) return;
                try {weapons.Verify(phase);}
                catch(Exception error) {NetworkServer.SendToAll(new JourneyFinish {success=false,detail=error.Message});return;}
                Advance(phase==14?1:phase+1);return;
            }
            switch(phase)
            {
                case 0:
                    if(players.Length==runtime.Options.ProbePeers && Mathf.Abs(Healer.x-5)<.1f && Mathf.Abs(Victim.x-6)<.1f &&
                        players.All(a=>Mathf.Abs(a.vx)<.1f) && players.Where(a=>a.id!=Healer.id && a.id!=Victim.id).All(a=>a.x>7)) Advance(12);break;
                case 1: if(Victim.down) Advance(2);break;
                case 2:
                    if(!Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4) Advance(3);break;
                case 3:
                    if(Healer.level=="factory:0" && players.Where(a=>a.id!=Healer.id).All(a=>a.level=="hub")) Advance(15);break;
                case 4:
                    if(players.Length==runtime.Options.ProbePeers && players.All(a=>a.level=="factory:0")) Advance(5);break;
                case 5:
                    if(players.All(a=>InventoryJournal.Count(a,"caps")==1) && session.World.pendingGrants.Count==0)
                    {bodyGeneration=Victim.generation;Advance(6);}break;
                case 6:
                    var body=session.ServerWorld.Find("player:session-client-1");
                    if(!body.connected) sawBody=body.down && body.level=="factory:0" && InventoryJournal.Count(body,"caps")==1;
                    if(sawBody && body.connected && body.generation>bodyGeneration && body.down) Advance(7);break;
                case 7: if(!Victim.down && InventoryJournal.Count(Healer,"heal")<=3) Advance(8);break;
                case 8:
                    if(players.Length==runtime.Options.ProbePeers && players.All(a=>a.level=="hub" && !a.down)) Advance(20);break;
            }
        }
        private IEnumerator Claim()
        {
            yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);
            yield return UiPointerProbe.Click(runtime.Hud.ContainerTabRect);
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0));
        }
        private void LateUpdate()
        {
            if(!Frames || finished || !FramePhase(phase) || captured.Contains(phase) ||
                runtime.Session.LocalActor==null || Time.unscaledTime-since<.25f) return;
            var world=runtime.Session.World;
            if(phase==2 && !Victim.down) return;
            if(phase==3 && (Healer.level!="factory:0" || world.actors.Any(a=>a.player && a.id!=Healer.id && a.level!="hub"))) return;
            if(phase==17 && world.props.Any(p=>p.held || p.y-p.anchorHeight>=.1f)) return;
            NetworkSceneRecord.Save(runtime,phase);captured.Add(phase);
            NetworkClient.Send(new JourneyFrameReady {phase=phase});
        }
        private bool ValidateFinal()
        {
            var session=runtime.Session;var players=session.World.actors.Where(a=>a.player && a.connected).ToArray();
            var saved=session.Saves.LoadCharacter();var local=session.LocalActor;
            return session.World.worldId==worldId && players.Length==runtime.Options.ProbePeers &&
                players.Select(a=>a.id).Distinct().Count()==players.Length && players.All(a=>a.level=="hub" && !a.down && InventoryJournal.Count(a,"caps")==1) &&
                session.World.containers.SelectMany(c=>c.items).Where(i=>i.definition=="caps").Sum(i=>i.count)==20-players.Length &&
                InventoryJournal.Count(Healer,"heal")<=3 && session.World.pendingGrants.Count==0 &&
                saved!=null && saved.id==local.id && InventoryJournal.Count(saved,"caps")==1 &&
                InventoryJournal.Count(saved,"heal")==InventoryJournal.Count(local,"heal");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,worldId=worldId,
                phase=phase,completed=completed.ToArray(),sawDisconnectedBody=sawBody,weaponsVerified=weapons.Verified,spearDamage=weapons.SpearDamage,
                telekinesisVerified=interactions.TelekinesisVerified,grenadeVerified=interactions.GrenadeVerified},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,sawDisconnectedBody,telekinesisVerified,grenadeVerified;public string detail,worldId;public int phase,weaponsVerified;public float spearDamage;public int[] completed;}
    }
}
