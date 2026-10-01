using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ExtensionProbePhase : NetworkMessage { }
    public struct ExtensionProbeAck : NetworkMessage { }
    public struct ExtensionProbeFinish : NetworkMessage { }
    public sealed class NetworkExtensionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private int initialReserve;
        private bool active,fired,reloaded,acked,finished,finishing;
        private readonly HashSet<int> acknowledgements=new();
        private bool Host=>runtime.Options.Role=="host";
        private static IDictionary<string,JToken> Extra(string value)=>new Dictionary<string,JToken>
        { ["example.mod:metadata"]=new JObject {["owner"]=value,["nested"]=new JArray("данные",true,17),["date"]="2026-10-01T00:00:00Z"} };
        private static bool Intact(ExtensibleState state,string owner)=>state.extensions!=null && state.extensions.TryGetValue("example.mod:metadata",out var value) &&
            JToken.DeepEquals(value,Extra(owner)["example.mod:metadata"]);
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка расширений требует отдельного профиля.");
            // Fixture adds opaque metadata before loading; gameplay below uses normal commands.
            var actor=new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,player=true,level="hub",extensions=Extra(value.Options.Character)};
            WeaponAmmunition.Initialize(actor,true);
            foreach(var item in actor.inventory) item.extensions=Extra(item.id);
            foreach(var magazine in actor.magazines) magazine.extensions=Extra(magazine.weapon);
            initialReserve=InventoryJournal.Count(actor,"p10");value.Session.Saves.SaveCharacter(actor);
            value.Session.ServerReady+=()=>
            {
                value.Session.ServerWorld.extensions=Extra("world");
                value.Session.CommitWorld();
                NetworkServer.RegisterHandler<ExtensionProbeAck>((connection,_)=>acknowledgements.Add(connection.connectionId));
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ExtensionProbePhase>(_=>active=true);
                NetworkClient.RegisterHandler<ExtensionProbeFinish>(_=>Finish(true,"Дополнительные поля мира, персонажа, предметов и магазинов сохранены после передачи, выстрела, перезарядки и записи профиля."));
            };
        }
        private PlayerCommand Command()
        {
            if(!active || runtime.Session.LocalActor==null) return default;
            var actor=runtime.Session.LocalActor;
            if(!fired) {fired=true;return new PlayerCommand {weapon="pistol",fire=true,hasAim=true,aim=actor.Position+Vector2.up*10};}
            if(!reloaded && WeaponAmmunition.Magazine(actor,"pistol").rounds==11) {reloaded=true;return new PlayerCommand {reload=true};}
            return default;
        }
        private void Update()
        {
            if(finished || finishing) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут расширений: active={active}, fired={fired}, reloaded={reloaded}, acknowledgements={acknowledgements.Count}.");return;}
            var actor=session.LocalActor;if(actor==null) return;
            if(Host && !active && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {active=true;NetworkServer.SendToAll(new ExtensionProbePhase());}
            if(!active || !reloaded || WeaponAmmunition.Magazine(actor,"pistol").rounds!=12 || InventoryJournal.Count(actor,"p10")!=initialReserve-1) return;
            if(!acked)
            {
                if(!Intact(session.World,"world") || !Intact(actor,runtime.Options.Character) || actor.inventory.Any(i=>!Intact(i,i.id)) || actor.magazines.Any(m=>!Intact(m,m.weapon)))
                {Finish(false,"Дополнительные данные потеряны в живом состоянии.");return;}
                acked=true;session.SaveLocalCharacter();
                var saved=session.Saves.LoadCharacter();
                if(InventoryJournal.Count(saved,"p10")!=initialReserve-1 || !Intact(saved,runtime.Options.Character) || saved.inventory.Any(i=>!Intact(i,i.id)))
                {Finish(false,"Дополнительные данные потеряны в файле персонажа.");return;}
                NetworkClient.Send(new ExtensionProbeAck());
            }
            if(Host && acknowledgements.Count==runtime.Options.ProbePeers)
            {finishing=true;NetworkServer.SendToAll(new ExtensionProbeFinish());}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
