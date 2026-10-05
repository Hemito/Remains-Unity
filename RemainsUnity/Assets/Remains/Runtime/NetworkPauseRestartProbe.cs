using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    public struct PauseRestartFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkPauseRestartProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private WorldState source;
        private bool finished,travelled,pauseRequested,loadedCorrectly;
        private float deadline,pausedAt,remaining,enteredAt;
        private string attack;
        private int resumedShots;
        private bool Host=>runtime.Options.Role=="host";
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=()=>default;
            if(Host && Resume)
            {
                source=value.Session.Saves.LoadWorld(false);
                if(source?.Find("npc:training-guard") is not {attackBusy:true}) throw new InvalidOperationException("Нет сохранённой атаки остановленного мира.");
                attack=source.Find("npc:training-guard").attackId;
            }
            value.Session.ServerReady+=()=>
            {
                if(!Resume) {value.Session.Pause.SetSettings(true,true);return;}
                var world=value.Session.ServerWorld;var npc=world.Find("npc:training-guard");
                loadedCorrectly=world.worldId==source.worldId && world.seed==source.seed && !npc.attackBusy && npc.attackId!=attack &&
                    npc.attackCooldown>=NpcAttackRules.CooldownSeconds && world.actors.Count==source.actors.Count &&
                    source.actors.All(saved=>world.Find(saved.id) is {} current && current.Position==saved.Position && current.health==saved.health &&
                        Remains.Serialization.StateJson.ToJson(current.inventory)==Remains.Serialization.StateJson.ToJson(saved.inventory)) &&
                    Remains.Serialization.StateJson.ToJson(world.containers)==Remains.Serialization.StateJson.ToJson(source.containers);
            };
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<PauseRestartFinish>(message=>Finish(message.success,message.detail));
            value.Session.Changed+=()=>{if(Host && Resume && enteredAt==0 && value.Session.LocalActor!=null) enteredAt=Time.unscaledTime;};
            value.Session.AttackReceived+=eventData=>
            {
                if(!Host || !Resume || value.Session.World?.Find(eventData.attacker) is not {player:false}) return;
                if(eventData.id==attack || enteredAt==0 || Time.unscaledTime-enteredAt<1.5f)
                {Complete(false,"После загрузки исполнилась старая атака либо пропущено время восстановления владельца.");return;}
                resumedShots++;
            };
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут перезапуска паузы: resume={Resume}, loaded={loadedCorrectly}, shots={resumedShots}.");return;}
            if(!Host) return;
            var session=runtime.Session;
            if(session.LocalActor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            var npc=session.World.Find("npc:training-guard");
            if(Resume)
            {
                if(!loadedCorrectly || session.Pause.IsPaused("factory:0") || session.Pause.PauseOnMenu || session.Pause.PauseOnInventory)
                {Complete(false,"Состояние мира изменилось при загрузке либо сохранились временные причины/настройки паузы.");return;}
                if(resumedShots>0) Complete(true,"Новый процесс загрузил тот же мир, сохранил позиции/HP/инвентари/контейнеры, сбросил паузу и отменил старую атаку; NPC продолжил работу после восстановления.");
                return;
            }
            if(!travelled) {travelled=true;session.Travel.RequestFactory();return;}
            if(session.LocalActor.level!="factory:0" || session.Travel.Pending) return;
            if(!pauseRequested && npc.attackBusy) {pauseRequested=true;attack=npc.attackId;session.Pause.SetManual(true);}
            if(!session.Pause.IsPaused("factory:0")) return;
            if(pausedAt==0) {pausedAt=Time.unscaledTime;remaining=npc.attackRemaining;}
            if(!npc.attackBusy || npc.attackId!=attack || npc.attackRemaining!=remaining || remaining<=0)
            {Complete(false,"Не удалось сохранить незавершённую атаку на паузе.");return;}
            if(Time.unscaledTime-pausedAt<1.5f) return;
            session.CommitWorld();
            var saved=session.Saves.LoadWorld(false);var savedNpc=saved.Find(npc.id);
            if(!savedNpc.attackBusy || savedNpc.attackId!=attack || Mathf.Abs(savedNpc.attackRemaining-remaining)>.001f)
            {Complete(false,"Сохранение потеряло остаток атаки остановленного мира.");return;}
            File.Copy(Path.Combine(session.Saves.DirectoryPath,"world.json"),runtime.Options.Report+".active-world.json",true);
            Complete(true,"Снимок занятого остановленного завода с незавершённой атакой записан для запуска новых процессов.");
        }
        private void Complete(bool success,string detail)
        {if(finished)return;NetworkServer.SendToAll(new PauseRestartFinish {success=success,detail=detail});}
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            if(Resume) success &= runtime.Session.LocalActor!=null && !runtime.Session.Pause.IsPaused("factory:0") &&
                !runtime.Session.Pause.PauseOnMenu && !runtime.Session.Pause.PauseOnInventory && !runtime.Hud.InventoryOpen && !runtime.Hud.PauseMenu.IsOpen;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
