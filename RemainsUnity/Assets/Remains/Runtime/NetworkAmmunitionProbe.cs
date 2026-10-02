using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct AmmunitionProbeDone : NetworkMessage { }
    public struct AmmunitionProbeFinish : NetworkMessage { }

    // Tests issue gameplay commands only. No inventory, mana or magazine mutation.
    public sealed class NetworkAmmunitionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,energyShots;
        private float phaseTime,deadline,finishAt;
        private bool reloadRequested,sentDone,finished,configured;
        private readonly HashSet<int> completed=new();
        private string actorId;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;
            value.CommandSource=Command;
            value.Session.AttackReceived+=attack=>{if(attack.attacker==actorId && attack.weapon=="energy") energyShots++;};
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<AmmunitionProbeDone>((connection,message)=>
            {
                completed.Add(connection.connectionId);
                if(completed.Count==runtime.Options.ProbePeers) NetworkServer.SendToAll(new AmmunitionProbeFinish());
            });
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<AmmunitionProbeFinish>(message=>finishAt=Time.unscaledTime+1);
        }
        private void Next() {phase++;phaseTime=Time.unscaledTime;reloadRequested=false;}
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(actor==null) return default;
            var command=new PlayerCommand {hasAim=true,aim=actor.Position+Vector2.up*20};
            switch(phase)
            {
                case 1: command.weapon="energy";command.fire=true;break;
                case 2: command.weapon="energy";command.reload=!reloadRequested;reloadRequested=true;break;
                case 3: command.weapon="pistol";command.fire=WeaponAmmunition.Magazine(actor,"pistol").rounds>9;break;
                case 4:
                    command.weapon=Time.unscaledTime-phaseTime<.3f?"pistol":"energy";
                    command.reload=!reloadRequested;reloadRequested=true;break;
                case 5: command.weapon="pistol";command.reload=!reloadRequested;reloadRequested=true;break;
            }
            return command;
        }
        private void Update()
        {
            if(finished) return;
            if(finishAt>0)
            {
                if(Time.unscaledTime>=finishAt) Finish(true,runtime.Options.Smoke.Contains("resume")?
                    "После перезапуска сохранены магазины, резерв и журнал: повторной выдачи или расхода нет.":
                    "Магазины и резерв, пустой выстрел, отсутствие расхода маны, завершение/отмена перезарядки и сохранение проверены обычными командами.");
                return;
            }
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут боезапаса: phase={phase}, energyShots={energyShots}");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            actorId=actor.id;
            if(runtime.Options.NetworkStress && Transport.active is not LatencySimulation) {Finish(false,"Не включена симуляция задержек.");return;}
            if(NetworkServer.active && !configured) {configured=true;session.SetFriendlyFire(false);}
            if(session.World.friendlyFire) return;
            if(actor.mana!=100) {Finish(false,"Лазерный пистолет изменил ману.");return;}
            int energy=WeaponAmmunition.Magazine(actor,"energy").rounds;
            int pistol=WeaponAmmunition.Magazine(actor,"pistol").rounds;
            switch(phase)
            {
                case 0:
                    if(runtime.Options.Smoke.Contains("resume"))
                    {
                        if(energy!=15 || pistol!=12 || InventoryJournal.Count(actor,"batt")!=30 || InventoryJournal.Count(actor,"p10")!=33 || actor.inventoryUses.Count!=2)
                        {Finish(false,"Перезапуск изменил сохранённый боезапас/журнал.");return;}
                        phase=6;phaseTime=Time.unscaledTime;break;
                    }
                    if(energy!=15 || pistol!=12 || InventoryJournal.Count(actor,"batt")!=45 || InventoryJournal.Count(actor,"p10")!=36)
                    {Finish(false,"Неверное стартовое оснащение.");return;}
                    Next();break;
                case 1:
                    // Keep pulling an empty trigger to verify it cannot emit extra attacks.
                    if(Time.unscaledTime-phaseTime<5.5f) return;
                    if(energy!=0 || energyShots!=15 || InventoryJournal.Count(actor,"batt")!=45)
                    {Finish(false,$"Неверный расход: magazine={energy}, shots={energyShots}");return;}
                    Next();break;
                case 2:
                    if(runtime.Combat.Reload.Active || energy!=15) return;
                    if(InventoryJournal.Count(actor,"batt")!=30) {Finish(false,"Резерв батарей не равен 30 после перезарядки.");return;}
                    Next();break;
                case 3: if(pistol==9) Next();break;
                case 4:
                    if(Time.unscaledTime-phaseTime<2) return;
                    if(runtime.Combat.Reload.Active || pistol!=9 || InventoryJournal.Count(actor,"p10")!=36)
                    {Finish(false,"Отмена перезарядки изменила боезапас.");return;}
                    Next();break;
                case 5:
                    if(runtime.Combat.Reload.Active || pistol!=12) return;
                    if(InventoryJournal.Count(actor,"p10")!=33) {Finish(false,"Неверный резерв после частичной перезарядки.");return;}
                    Next();break;
                case 6:
                    if(Time.unscaledTime-phaseTime<4 || sentDone) return;
                    var saved=session.Saves.LoadCharacter();
                    if(saved==null || WeaponAmmunition.Magazine(saved,"energy").rounds!=15 || WeaponAmmunition.Magazine(saved,"pistol").rounds!=12 ||
                        InventoryJournal.Count(saved,"batt")!=30 || InventoryJournal.Count(saved,"p10")!=33 || saved.inventoryUses.Count!=2)
                    {Finish(false,"Сохранение не содержит подтверждённый боезапас.");return;}
                    sentDone=true;NetworkClient.Send(new AmmunitionProbeDone());break;
            }
        }
        private void Finish(bool success,string detail)
        {
            finished=true;
            if(success && !runtime.Options.Smoke.Contains("resume"))
            {
                bool audio=runtime.Audio.LocalShots==18 && runtime.Audio.RemoteShots==18*(runtime.Options.ProbePeers-1) && runtime.Audio.ReloadStarts==3 && runtime.Audio.RemoteReloadStarts==3*(runtime.Options.ProbePeers-1) && runtime.Audio.RemoteReloadStops==3*(runtime.Options.ProbePeers-1);
                success &= audio;detail+=audio?" Звуковые события: 18 своих выстрелов, выстрелы других игроков и 3 начала перезарядки подтверждены (беззвучный режим).":" Неверное количество звуковых событий.";
            }
            string json=JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,actor=actorId,stress=runtime.Options.NetworkStress,
                localShotAudio=runtime.Audio.LocalShots,remoteShotAudio=runtime.Audio.RemoteShots,reloadAudio=runtime.Audio.ReloadStarts,remoteReloadAudio=runtime.Audio.RemoteReloadStarts,remoteReloadStops=runtime.Audio.RemoteReloadStops},true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,json);
            Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report { public bool success,stress;public string detail,actor;public int phase,localShotAudio,remoteShotAudio,reloadAudio,remoteReloadAudio,remoteReloadStops; }
    }
}
