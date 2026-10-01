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
    public struct JourneyFinish : NetworkMessage {public bool success;public string detail;}

    // One world and character set, ordinary commands only; no gameplay state injection.
    public sealed class NetworkJourneyProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1,bodyGeneration;
        private float deadline,since,reconnectAt,nextHeal;
        private bool action,disconnected,reconnected,sawBody,finished,ready;
        private string worldId;
        private readonly HashSet<int> readyPeers=new();
        private readonly List<int> completed=new();
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.Character=="session-client-1";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+100;runtime.CommandSource=Command;
            if(Resume)
                worldId=value.Session.Saves.LoadWorld(false)?.worldId??throw new InvalidOperationException("Нет мира для продолжения journey.");
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<JourneyPhase>(m=>{phase=m.value;since=Time.unscaledTime;action=false;});
                NetworkClient.RegisterHandler<JourneyFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<JourneyReady>((connection,_)=>
            {if(value.Session.ConnectedActor(connection)!=null) readyPeers.Add(connection.connectionId);});
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null || Resume) return default;
            if(phase>=3 && !self.down && self.health<70 && Time.unscaledTime>=nextHeal && InventoryJournal.Count(self,"heal")>0)
            {nextHeal=Time.unscaledTime+1;return new PlayerCommand {heal=true};}
            if(phase==0)
            {
                float target=Host?3:Source?1.8f:7+int.Parse(self.character.Split('-').Last());
                return new PlayerCommand {movement=Vector2.right*(Mathf.Abs(self.x-target)>.05f?Mathf.Sign(target-self.x):0)};
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
                if(Host && readyPeers.Count==runtime.Options.ProbePeers)
                    NetworkServer.SendToAll(new JourneyFinish {success=true,detail=Resume?
                        "Перезапуск того же мира сохранил персонажей, лут и расход лечения.":
                        "Один мир: бой/поднятие, разделение уровней, общий подбор, отключённое тело/возвращение/поднятие, хаб и сохранение."});
                return;
            }
            if(!Host || Time.unscaledTime-since<.5f) return;
            switch(phase)
            {
                case 0:
                    if(players.Length==runtime.Options.ProbePeers && Mathf.Abs(Healer.x-3)<.1f && Mathf.Abs(Victim.x-1.8f)<.1f &&
                        players.Where(a=>a.id!=Healer.id && a.id!=Victim.id).All(a=>a.x>7)) Advance(1);break;
                case 1: if(Victim.down) Advance(2);break;
                case 2:
                    if(!Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4) Advance(3);break;
                case 3:
                    if(Healer.level=="factory:0" && players.Where(a=>a.id!=Healer.id).All(a=>a.level=="hub")) Advance(4);break;
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
                    if(players.Length==runtime.Options.ProbePeers && players.All(a=>a.level=="hub" && !a.down)) Advance(9);break;
            }
        }
        private IEnumerator Claim()
        {
            yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0));
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
                phase=phase,completed=completed.ToArray(),sawDisconnectedBody=sawBody},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success,sawDisconnectedBody;public string detail,worldId;public int phase;public int[] completed;}
    }
}
