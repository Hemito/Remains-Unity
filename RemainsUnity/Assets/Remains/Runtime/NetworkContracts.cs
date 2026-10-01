using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ContractPhaseMessage : NetworkMessage { public int phase; public string item; }
    public struct ContractAckMessage : NetworkMessage { public string actor; public float health; }

    public sealed class NetworkContracts : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1;
        private float phaseStarted,deadline;
        private bool acted,disconnected,reconnecting,acked;
        private string clientId;
        private int originalGeneration;
        private float npcHealth;
        private int initialGrenades;
        private bool sawFlight;
        private float ackedAt;
        private string sharedItem;
        private int weaponStep;
        private readonly List<string> passed=new();

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+100;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<ContractPhaseMessage>(message=>{sharedItem=message.item;SetPhase(message.phase);});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ContractAckMessage>((connection,message)=>
            {
                if(message.actor==clientId && message.health<=65) {acked=true;ackedAt=Time.unscaledTime;}
            });
        }

        private void SetPhase(int next)
        {
            if(phase==next) return;
            phase=next;phaseStarted=Time.unscaledTime;acted=false;
        }
        private void Advance(int next)
        {
            if(next==0) sharedItem=runtime.Session.World.container.Find(i=>i.definition=="caps")?.id;
            SetPhase(next);NetworkServer.SendToAll(new ContractPhaseMessage{phase=next,item=sharedItem});
        }

        private void Update()
        {
            if(Time.unscaledTime>deadline) { Finish(false,$"Таймаут этапа {phase}");return; }
            bool host=runtime.Options.Smoke=="contracts-host";
            if(!host && disconnected)
            {
                if(!reconnecting && Time.unscaledTime-phaseStarted>3)
                { reconnecting=true;runtime.Session.StartClient(); }
                if(reconnecting && NetworkClient.isConnected && runtime.Session.LocalActor is { connected:true } restored)
                {
                    if(restored.id==clientId && restored.down && restored.health<=65)
                    {
                        if(!acted)
                        {
                            acted=true;passed.Add("Возвращение в то же тело с накопленным уроном");
                            NetworkClient.Send(new ContractAckMessage{actor=restored.id,health=restored.health});
                            phaseStarted=Time.unscaledTime;
                        }
                        if(Time.unscaledTime-phaseStarted>1) Finish(true,"Клиент подтвердил сохранение тела и накопленный урон");
                    }
                }
                return;
            }
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null || !NetworkClient.isConnected || session.World==null) return;
            if(self.level=="hub") {session.ReturnToFactory();return;}
            if(phase<0)
            {
                if(!host || session.World.actors.Count(a=>a.player && a.connected)<2) return;
                if(session.World.actors.Exists(a=>a.player && a.connected && a.level!="factory:0")) return;
                clientId=session.World.actors.First(a=>a.player && a.id!=self.id).id;
                originalGeneration=session.World.Find("npc:training-guard").generation;
                Advance(0);
            }
            if(!host) ClientStep();else HostStep();
        }

        private void ClientStep()
        {
            var session=runtime.Session;
            if(phase==0 && !acted)
            {
                acted=true;
                session.RequestClaim(sharedItem);
            }
            if(phase==1) session.LocalActor.x=-5;
            if(phase==6) session.LocalActor.x=9;
            if(phase==5 && !disconnected)
            {
                clientId=session.LocalActor.id;
                disconnected=true;acted=false;phaseStarted=Time.unscaledTime;
                session.StopClient();
            }
        }

        private void HostStep()
        {
            var session=runtime.Session;var self=session.LocalActor;
            var npc=session.World.Find("npc:training-guard");
            switch(phase)
            {
                case 0:
                    if(!acted)
                    {
                        acted=true;
                        session.RequestClaim(sharedItem);
                    }
                    int total=session.World.actors.Sum(a=>a.inventory.Where(i=>i.definition=="caps").Sum(i=>i.count));
                    if(total==20 && session.World.pendingGrants.Count==0)
                    { passed.Add("Общий подбор: два запроса, одна выдача после backup ACK");Advance(6); }
                    break;
                case 6:
                    // Establish the initial owner explicitly; spawn positions must
                    // not determine whether the handoff test exercises a transfer.
                    self.x=-5;
                    if(npc.owner==session.World.Find(clientId)?.owner)
                    {originalGeneration=npc.generation;Advance(1);}
                    break;
                case 1:
                    self.x=6;
                    if(npc.owner==session.LocalOwner && npc.generation>originalGeneration)
                    { passed.Add("NPC сменил владельца и поколение после движения игроков");Advance(2); }
                    break;
                case 2:
                    self.x=-3;
                    var prop=session.World.props[0];
                    runtime.Telekinesis.Step(true,acted?self.Position+Vector2.up*2:prop.Position,Time.deltaTime);
                    acted=true;
                    if(prop.y>1 && self.mana<100)
                    {
                        runtime.Telekinesis.Step(false,prop.Position,Time.deltaTime);
                        passed.Add("Телекинез: согласованный захват, движение, расход маны");
                        initialGrenades=self.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count);
                        Advance(3);
                    }
                    break;
                case 3:
                    if(!acted) { acted=true;runtime.Interactions.ThrowGrenade(self.Position+Vector2.right*4); }
                    if(runtime.Grenades.Flights.Count>0) sawFlight=true;
                    if(sawFlight && runtime.Grenades.Flights.Count==0 && self.inventory.Where(i=>i.definition=="grenade").Sum(i=>i.count)==initialGrenades-1)
                    { passed.Add("Граната: расход предмета, полёт, запал, взрыв");npcHealth=npc.health;Advance(4); }
                    break;
                case 4:
                    self.x=npc.x-1;
                    string testedWeapon=weaponStep==0?"spear":weaponStep==1?"crowbar":"energy";
                    if(!acted) { runtime.Combat.Weapon=testedWeapon;acted=runtime.Combat.Fire(self,npc.Position+new Vector2(0,0.8f)); }
                    if(npc.health<npcHealth)
                    {
                        passed.Add(testedWeapon+": реальное попадание уменьшило здоровье NPC");
                        npcHealth=npc.health;acted=false;weaponStep++;
                        if(weaponStep==3) Advance(5);
                    }
                    break;
                case 5:
                    var body=session.World.Find(clientId);
                    if(body!=null && !body.connected && body.owner==session.LocalOwner)
                    {
                        disconnected=true;
                        if(body.health>65 && !acted)
                        { runtime.Combat.Weapon="pistol";acted=runtime.Combat.Fire(self,body.Position+new Vector2(0,0.2f)); }
                        if(body.down && body.health<=65 && !passed.Contains("Отключённое тело получило урон у нового владельца"))
                            passed.Add("Отключённое тело получило урон у нового владельца");
                    }
                    if(acked && disconnected && Time.unscaledTime-ackedAt>2) Finish(true,"Сетевые контракты выполнены двумя независимыми процессами");
                    break;
            }
        }

        private void Finish(bool success,string detail)
        {
            enabled=false;
            string json=JsonUtility.ToJson(new Report { success=success,detail=detail,passed=passed.ToArray(),phase=phase,
                world=runtime.Session.World?.worldId },true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,json);
            Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report { public bool success;public string detail,world;public string[] passed;public int phase; }
    }
}
