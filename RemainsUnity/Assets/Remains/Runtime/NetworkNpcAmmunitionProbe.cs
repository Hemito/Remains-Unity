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
    public struct NpcAmmunitionObserved : NetworkMessage {public string item;public int shots;public bool paused;}
    public struct NpcAmmunitionFinish : NetworkMessage {public bool success;public string detail;}
    // Explicit small-ammunition fixture; the actual owner/controller/network/journal perform every action.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkNpcAmmunitionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private readonly HashSet<string> shots=new();
        private float deadline,pausedAt,pausedRemaining,emptyAt;
        private string pausedAction,item,capture;
        private bool traveled,alerted,pauseSent,pausedVerified,resumed,reloaded,sent,finished;
        private bool Host=>runtime.Options.Role=="host";
        private bool Source=>runtime.Options.ProbePeers==1 || runtime.Options.Character=="session-client-1";
        private ActorState Npc=>runtime.Session.World?.Find("npc:training-guard");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+55;value.CommandSource=Command;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<NpcAmmunitionFinish>(m=>Finish(m.success,m.detail));
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<NpcAmmunitionObserved>((connection,message)=>
            {
                var player=value.Session.ConnectedActor(connection);var npc=value.Session.ServerWorld.Find("npc:training-guard");
                var weapon=WeaponInventory.Selected(npc);
                bool valid=player!=null && npc.owner==player.owner && weapon?.id==message.item &&
                    weapon.weaponState.rounds==0 && weapon.weaponState.revision==4 && InventoryJournal.Count(npc,"p10")==0 &&
                    npc.inventoryUses.Count==1 && npc.inventoryUses[0].debits.Sum(d=>d.count)==2 &&
                    !npc.attackBusy && message.shots==3 && message.paused;
                if(valid)
                {
                    value.Session.CommitWorld();var saved=value.Session.Saves.LoadWorld(false).Find(npc.id);var savedItem=WeaponInventory.Selected(saved);
                    valid=savedItem?.id==message.item && savedItem.weaponState.rounds==0 && savedItem.weaponState.revision==4 &&
                        saved.inventoryUses.Count==1 && InventoryJournal.Count(saved,"p10")==0;
                }
                NetworkServer.SendToAll(new NpcAmmunitionFinish {success=valid,detail=valid?
                    "NPC израсходовал один патрон, переждал паузу перезарядки, перенёс два патрона из резерва и выпустил ещё два реальных выстрела. Пустой магазин/резерв и один расход сохранены.":
                    $"Разошёлся боезапас NPC: owner={npc.owner}/{player?.owner}, item={weapon?.id}, rounds={weapon?.weaponState?.rounds}, revision={weapon?.weaponState?.revision}, shots={message.shots}, uses={npc.inventoryUses.Count}."});
            });
            value.Session.AttackReceived+=attack=>
            {
                if(attack.attacker!="npc:training-guard") return;
                if(attack.weapon!="pistol" || !shots.Add(attack.id)) {Finish(false,"Неверное оружие или повтор NPC-выстрела.");return;}
                if(shots.Count==1 && Source) capture="first-shot";
                if(shots.Count>3) Finish(false,"NPC стреляет после исчерпания всего боезапаса.");
            };
        }
        private PlayerCommand Command()
        {
            var actor=runtime.Session.LocalActor;
            if(!Source || alerted || actor?.level!="factory:0") return default;
            // The fixture guard faces away from the entrance. A real shot away from it alerts hearing.
            alerted=true;return new PlayerCommand {weapon="pistol",fire=true,hasAim=true,aim=actor.Position+new Vector2(-5,.8f)};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут боезапаса: source={Source}, shots={shots.Count}, pause={pausedVerified}, reload={reloaded}, npc={Npc?.attackBusy}/{Npc?.npcWeaponAction?.kind}/{Npc?.attackRemaining}.");return;}
            var session=runtime.Session;var player=session.LocalActor;
            if(player==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(!Source) return;
            if(!traveled) {traveled=true;session.Travel.RequestFactory();return;}
            var npc=Npc;if(player.level!="factory:0" || npc==null || npc.owner!=session.LocalOwner) return;
            var selected=WeaponInventory.Selected(npc);if(selected==null) {Finish(false,"Потерян предмет оружия стенда.");return;}
            item ??=selected.id;if(item!=selected.id) {Finish(false,"Оружие заменено копией посередине цикла.");return;}
            if(npc.attackBusy && npc.npcWeaponAction?.kind==NpcWeaponActions.Reload && !pauseSent)
            {pauseSent=true;session.Pause.SetManual(true);}
            if(pauseSent && session.Pause.IsPaused(npc.level) && !resumed)
            {
                if(pausedAt==0)
                {pausedAt=Time.unscaledTime;pausedRemaining=npc.attackRemaining;pausedAction=npc.attackId;capture="reload-paused";}
                if(!npc.attackBusy || npc.npcWeaponAction?.kind!=NpcWeaponActions.Reload || npc.attackId!=pausedAction ||
                    Mathf.Abs(npc.attackRemaining-pausedRemaining)>.001f || selected.weaponState.rounds!=0 || InventoryJournal.Count(npc,"p10")!=2 || npc.inventoryUses.Count!=0)
                {Finish(false,"Пауза исполнила или изменила перезарядку/резерв.");return;}
                if(Time.unscaledTime-pausedAt>.65f) {pausedVerified=true;resumed=true;session.Pause.SetManual(false);}
            }
            if(resumed && selected.weaponState.rounds==2 && InventoryJournal.Count(npc,"p10")==0 && !reloaded)
            {reloaded=true;capture="reloaded";}
            if(shots.Count==3 && selected.weaponState.rounds==0 && !npc.attackBusy)
            {
                if(emptyAt==0) {emptyAt=Time.unscaledTime;capture="empty";}
                if(Time.unscaledTime-emptyAt>3.5f && !sent)
                {
                    if(!pausedVerified || !reloaded || npc.inventoryUses.Count!=1) {Finish(false,"Не подтверждены настоящая перезарядка и однократный расход.");return;}
                    sent=true;NetworkClient.Send(new NpcAmmunitionObserved {item=item,shots=shots.Count,paused=pausedVerified});
                }
            }
        }
        private void LateUpdate()
        {
            if(capture==null || Npc==null) return;var camera=Camera.main;var saved=camera.transform.position;
            try {camera.transform.position=new Vector3(Npc.x,Npc.y+3,saved.z);NetworkFrameCapture.Save(camera,runtime.Options.Report+".npc-ammo-"+capture+".png",runtime.Hud.GetComponent<Canvas>());}
            finally {camera.transform.position=saved;capture=null;}
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,source=Source,shots=shots.Count,
                paused=pausedVerified,reloaded=reloaded,item=item,owner=Npc?.owner,rounds=WeaponInventory.Selected(Npc)?.weaponState?.rounds ?? -1},true));
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success,source,paused,reloaded;public int shots,rounds;public string detail,item,owner;}
    }
}
