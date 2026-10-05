using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Runtime
{
    public struct ContainerWindowReady : NetworkMessage { }
    public struct ContainerWindowFinish : NetworkMessage { }
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkContainerWindowProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,since,walkX;
        private bool traveled,finished,pointerCheck,ready;
        private string weapon="pistol";
        private int localAttacks;
        private readonly HashSet<int> peers=new();
        private readonly HashSet<int> captured=new();
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;value.CommandSource=Command;
            value.Session.AttackReceived+=attack=>{if(attack.attacker==value.Session.LocalActorId) localAttacks++;};
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<ContainerWindowFinish>(_=>Finish(true,"E-окно отдельно от инвентаря; целая стопка, клики без выстрела, пустая вкладка брони без изменения вещей, ходьба/бег/дальность и порядок Escape проверены."));
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ContainerWindowReady>((connection,_)=>peers.Add(connection.connectionId));
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;if(actor==null) return default;
            if(actor.weapon!=weapon) return new PlayerCommand {weaponItem=actor.inventory.Single(i=>i.definition==weapon).id};
            if(phase is 1 or 5 or 7 && actor.level=="factory:0" && !runtime.Session.Travel.Pending && !runtime.Hud.ContainerOpen)
            {
                var target=InteractionSelection.Select(runtime.Session.World,actor,runtime.Session.Rules,runtime.Session.Travel.Points(actor.level));
                if(target.kind==InteractionKind.Container && target.id==ContainerState.PrototypeId) return new PlayerCommand {interact=true};
                var box=runtime.Session.World.containers.Single(c=>c.id==ContainerState.PrototypeId);
                return new PlayerCommand {movement=new Vector2(Math.Sign(box.x-actor.x),0)};
            }
            if(phase is 3 or 4 or 6) return new PlayerCommand {movement=Vector2.right,sprint=phase==4};
            var mouse=Mouse.current;
            if(pointerCheck && mouse?.name=="SessionProbeMouse" && mouse.leftButton.isPressed)
                return new PlayerCommand {fire=true,hasAim=true,aim=actor.Position+Vector2.left*5,hasPointer=true,pointer=mouse.position.ReadValue()};
            return default;
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут окна контейнера: phase="+phase);return;}
            var actor=runtime.Session.LocalActor;if(actor==null) return;
            var world=runtime.Session.World;
            if(!traveled && world.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
            {traveled=true;runtime.Session.Travel.RequestFactory();phase=1;}
            if(phase==1 && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.InventoryOpen || runtime.Hud.BlocksGameplayInput || runtime.Hud.PauseMenu.IsOpen)
                {Finish(false,"E открыл личный инвентарь или заблокировал движение.");return;}
                phase=2;StartCoroutine(CheckClicks());
            }
            if(phase==3 && Time.unscaledTime-since>.15f)
            {
                if(!runtime.Hud.ContainerOpen || actor.x-walkX<.1f) {Finish(false,"Ходьба у контейнера закрыла окно или не двигала персонажа.");return;}
                phase=4;
            }
            if(phase==4 && !runtime.Hud.ContainerOpen) phase=5;
            if(phase==5 && runtime.Hud.ContainerOpen) phase=6;
            if(phase==6 && !runtime.Hud.ContainerOpen)
            {
                var box=world.containers.Single(c=>c.id==ContainerState.PrototypeId);
                if(Vector2.Distance(actor.Position,box.Position)<=2) {Finish(false,"Окно закрылось до выхода за дальность.");return;}
                phase=7;
            }
            if(phase==7 && runtime.Hud.ContainerOpen)
            {
                runtime.Hud.ToggleMenu();
                if(runtime.Hud.ContainerOpen || runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Escape не закрыл контейнер первым действием.");return;}
                runtime.Hud.ToggleInventory();runtime.Hud.ToggleMenu();
                if(runtime.Hud.InventoryOpen || runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Первый Escape не закрыл личный инвентарь.");return;}
                runtime.Hud.ToggleMenu();
                if(!runtime.Hud.PauseMenu.IsOpen) {Finish(false,"Второй Escape не открыл меню.");return;}
                runtime.Hud.ToggleMenu();phase=8;
            }
            if(phase==8 && !ready) {ready=true;NetworkClient.Send(new ContainerWindowReady());}
            if(NetworkServer.active && peers.Count==runtime.Options.ProbePeers && phase==8)
            {phase=9;NetworkServer.SendToAll(new ContainerWindowFinish());}
        }
        private IEnumerator CheckClicks()
        {
            yield return null;yield return null;
            var actor=runtime.Session.LocalActor;
            int rounds=WeaponAmmunition.Magazine(actor,"pistol").rounds;
            int grenades=InventoryJournal.Count(actor,"grenade");
            pointerCheck=true;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerWindowRect,new Vector2(-42,107));
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0));
            weapon="spear";yield return null;yield return null;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            if(runtime.Combat.IsMeleeActive) {Finish(false,"Клик по пустой клетке начал удар копьём.");yield break;}
            weapon="grenade";yield return null;yield return null;
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(8));
            pointerCheck=false;
            while(runtime.Session.World.pendingGrants.Count!=0 || runtime.Session.World.actors.SelectMany(a=>a.inventory).Where(i=>i.definition=="caps").Sum(i=>i.count)<20) yield return null;
            actor=runtime.Session.LocalActor;
            var caps=runtime.Session.World.actors.SelectMany(a=>a.inventory).Where(i=>i.definition=="caps").ToArray();
            if(actor.inventory.Single(i=>i.definition=="pistol").weaponState.rounds!=rounds || InventoryJournal.Count(actor,"grenade")!=grenades || localAttacks!=0 ||
                caps.Length!=1 || caps[0].count!=20)
            {Finish(false,"Клик окна потратил боеприпасы/гранату либо целая стопка не выдана ровно одному игроку.");yield break;}
            string beforeArmor=StateJson.ToJson(actor.inventory),held=actor.equippedWeaponItem;
            runtime.Hud.ToggleInventory();
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.Armor));
            if(!runtime.Hud.InventoryOpen || !runtime.Hud.BlocksGameplayInput || runtime.Hud.PersonalCategory!=InventoryCategory.Armor)
            {Finish(false,"Указатель не открыл пустую вкладку брони или разблокировал игровое управление.");yield break;}
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkSceneRecord.Save(runtime,20);
            yield return UiPointerProbe.Click(runtime.Hud.PersonalCategoryRect(InventoryCategory.All));
            actor=runtime.Session.LocalActor;
            if(runtime.Hud.PersonalCategory!=InventoryCategory.All || StateJson.ToJson(actor.inventory)!=beforeArmor || actor.equippedWeaponItem!=held)
            {Finish(false,"Переключение вкладки брони изменило вещи/оружие или не вернулось в общий список.");yield break;}
            runtime.Hud.ToggleInventory();
            if(runtime.Hud.InventoryOpen || !runtime.Hud.ContainerOpen || runtime.Hud.PauseMenu.IsOpen)
            {Finish(false,"Закрытие личного окна не вернуло отдельное окно контейнера.");yield break;}
            weapon="pistol";walkX=actor.x;since=Time.unscaledTime;phase=3;
        }
        private void LateUpdate()
        {
            if(runtime.Options.Smoke.Contains("-frames-") && phase==2 && !captured.Contains(phase))
            {captured.Add(phase);NetworkSceneRecord.Save(runtime,phase);}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase;}
    }
}
