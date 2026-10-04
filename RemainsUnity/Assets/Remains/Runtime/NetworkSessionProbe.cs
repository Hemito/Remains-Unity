using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct SessionProbePhase : NetworkMessage { public int phase; }
    public struct SessionProbeAck : NetworkMessage { public int phase; }

    // No ActorState writes: commands enter the production loop, NPCs keep running.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkSessionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase=-1;
        private float phaseTime,deadline,nextSelfHeal;
        private bool acked,travelRequested,finished;
        private bool grenadeRequested,blastAfterTravel;
        private bool inventoryActionPending;
        private int inventoryHeals,inventoryWeaponChanges;
        private string grenadeAttack;
        private int grenadeReceipts,grenadeNumbers;
        private string completedActor;
        private string retainedContents,retainedActors;
        private int visualChangedFrame;
        private int capturePhase=-1;
        private readonly HashSet<int> acknowledgements=new();
        private bool Host=>runtime.Options.Role=="host";
        private static float Destination(string character)=>character switch
        {
            "session-host"=>-5,"session-client-1"=>-2,"session-client-2"=>1,"session-client-3"=>4,
            _=>throw new InvalidOperationException($"Неизвестный участник проверки: {character}.")
        };
        private static int Facing(string character)=>character is "session-host" or "session-client-2"?-1:1;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;
            // Duplicate only the cosmetic receipt; gameplay is never replayed here.
            value.Combat.DamageApplied+=result=>value.DamageFeedback.Publish(result);
            value.DamageFeedback.Received+=result=>{if(result.attack==grenadeAttack) grenadeReceipts++;};
            value.DamageNumbers.Shown+=result=>{if(result.attack==grenadeAttack) grenadeNumbers++;};
            value.Session.Changed+=()=>visualChangedFrame=Time.frameCount;
            runtime.CommandSource=Command;
            value.Session.AttackReceived+=attack=>
            {
                if(attack.weapon=="grenade" && attack.attacker=="player:session-host")
                {
                    grenadeAttack=attack.id;
                    blastAfterTravel=attack.level=="hub" && value.Session.World.Find(attack.attacker)?.level=="factory:0";
                }
            };
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<SessionProbePhase>(message=>
            {phase=message.phase;phaseTime=Time.unscaledTime;acked=false;travelRequested=false;if(phase==7) completedActor=value.Session.LocalActorId;});
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<SessionProbeAck>((connection,message)=>
            {
                if(message.phase==phase && value.Session.ConnectedActor(connection)!=null) acknowledgements.Add(connection.connectionId);
            });
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null) return default;
            if(phase==1 && Host && !grenadeRequested)
            {
                grenadeRequested=true;
                return new PlayerCommand {grenade=true,hasAim=true,aim=self.Position+Vector2.down*3};
            }
            if(phase==5 && self.level=="hub" && self.health<70 && !inventoryActionPending && Time.unscaledTime>=nextSelfHeal)
            {
                if(!runtime.Hud.InventoryOpen) return new PlayerCommand {inventory=true};
                inventoryActionPending=true;StartCoroutine(UseInventory());
            }
            if((phase!=3 && phase!=6) || self.level!="factory:0" || runtime.Session.Travel.Pending) return default;
            float destination=Destination(self.character);
            float delta=destination-self.x;
            float movement=Mathf.Abs(delta)>.05f?Mathf.Sign(delta):self.facing!=Facing(self.character)?Facing(self.character)*.1f:0;
            return new PlayerCommand { movement=new Vector2(movement,0),
                hasAim=true,aim=self.Position+new Vector2(Facing(self.character)*5,.8f),weapon="pistol" };
        }
        private void Advance(int next)
        {
            acknowledgements.Clear();
            NetworkServer.SendToAll(new SessionProbePhase {phase=next});
        }
        private IEnumerator UseInventory()
        {
            var actor=runtime.Session.LocalActor;
            foreach(int index in new[]{3,2})
            {

                yield return UiPointerProbe.Click(runtime.Hud.InventoryWeaponRect(index));
                string expected=index==3?"energy":"pistol";
                if(runtime.Session.LocalActor.weapon!=expected)
                {Finish(false,$"Кнопка оружия не применилась: expected={expected}, actual={runtime.Session.LocalActor.weapon}.");yield break;}
                inventoryWeaponChanges++;
            }
            int row=actor.inventory.FindIndex(item=>item.definition=="heal");
            if(row<0 || row>=8) {Finish(false,"Аптечка не найдена на первой странице проверочного инвентаря.");yield break;}
            float beforeHealth=actor.health;
            int beforeCount=actor.inventory.Where(item=>item.definition=="heal").Sum(item=>item.count);

            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(row));
            if(runtime.Session.LocalActor.health!=beforeHealth || runtime.Session.LocalActor.inventory.Where(item=>item.definition=="heal").Sum(item=>item.count)!=beforeCount)
            {Finish(false,"Выбор аптечки сам изменил здоровье или количество.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.UseItemRect);
            float until=Time.unscaledTime+5;
            while(runtime.Session.LocalActor.health==beforeHealth && Time.unscaledTime<until) yield return null;
            actor=runtime.Session.LocalActor;
            if(actor.health!=Mathf.Min(100,beforeHealth+35) || actor.inventory.Where(item=>item.definition=="heal").Sum(item=>item.count)!=beforeCount-1)
            {Finish(false,$"Лечение через инвентарь: hp={actor.health}, before={beforeHealth}, healBefore={beforeCount}.");yield break;}
            inventoryHeals++;
            if(runtime.Hud.InventoryOpen) runtime.Hud.ToggleInventory();
            nextSelfHeal=Time.unscaledTime+1;inventoryActionPending=false;
        }
        private void Update()
        {
            if(finished) return;
            // Completion was acknowledged by every peer before phase 7. A host exit
            // must not make a slower client's report depend on the live player count.
            if(phase==7) {if(Time.unscaledTime-phaseTime>1) Finish(true,"Обычная симуляция: UI-переходы в обе стороны, разделение группы, граната в исходной локации, независимое движение и видимость; выход всей группы и повторный вход без пересоздания NPC/контейнеров.");return;}
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: phase={phase}, level={runtime.Session.LocalActor?.level}, pending={runtime.Session.Travel.Pending}");return;}
            var session=runtime.Session;var self=session.LocalActor;
            if(self==null) return;
            if(runtime.Options.NetworkStress && Transport.active is not LatencySimulation)
            {Finish(false,"Тестовый транспорт задержек не активирован.");return;}
            var players=session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(players.Length!=runtime.Options.ProbePeers) return;
            if(phase<0) {if(Host) Advance(0);return;}
            if((phase==1 && Host && runtime.Grenades.Flights.Count>0 || phase==2 && !Host || phase==4 && Host || phase==5 && !Host || phase==6) && !travelRequested)
            {travelRequested=true;StartCoroutine(UiPointerProbe.Click(runtime.Hud.FactoryButtonRect));}
            if(Time.unscaledTime-phaseTime<1) return;
            bool ready=phase switch
            {
                0=>players.All(a=>a.level=="hub"),
                1=>blastAfterTravel && self.level==(Host?"factory:0":"hub") && players.Count(a=>a.level=="factory:0")==1 &&
                    players.Where(a=>a.character!="session-host").All(a=>a.health==30),
                2=>players.All(a=>a.level=="factory:0") && !session.Travel.Pending,
                3=>players.All(a=>a.level=="factory:0" && Mathf.Abs(a.x-Destination(a.character))<.15f && a.facing==Facing(a.character)),
                4=>self.level==(Host?"hub":"factory:0") && players.Count(a=>a.level=="hub")==1 && !session.Travel.Pending,
                5=>players.All(a=>a.level=="hub" && a.health>=70) && !session.Travel.Pending && !inventoryActionPending && (Host || inventoryHeals==2 && inventoryWeaponChanges==4),
                6=>players.All(a=>a.level=="factory:0" && Mathf.Abs(a.x-Destination(a.character))<.15f && a.facing==Facing(a.character)) && !session.Travel.Pending,
                _=>false
            };
            if(!ready) return;
            if(phase==1)
            {
                if(grenadeReceipts<runtime.Options.ProbePeers-1) return;
                int displayed=Host?0:runtime.Options.ProbePeers-1;
                if(runtime.Explosions.Shown!=(Host?0:1))
                {Finish(false,"Эффект взрыва показан не в своей локации или пропущен.");return;}
                if(grenadeReceipts!=runtime.Options.ProbePeers-1 || grenadeNumbers!=displayed)
                {Finish(false,$"Числа урона взрыва: receipts={grenadeReceipts}, displayed={grenadeNumbers}/{displayed}.");return;}
            }
            // Unity destroys objects and their owned materials at frame boundaries.
            if(Time.frameCount<=visualChangedFrame+2 || runtime.ReleasingUnusedAssets) return;
            if(!runtime.Geometry.LoadedLevels.OrderBy(l=>l).SequenceEqual(players.Select(a=>a.level).Distinct().OrderBy(l=>l)))
            {Finish(false,"Геометрия симуляции не соответствует занятым локациям.");return;}
            string contents=string.Join("|",session.World.containers.OrderBy(c=>c.id).Select(c=>c.id+":"+string.Join(",",c.items.OrderBy(i=>i.id).Select(i=>i.id+":"+i.count))));
            string actors=string.Join("|",session.World.actors.Where(a=>!a.player).OrderBy(a=>a.id).Select(a=>a.id+":"+a.health));
            if(phase==3) {retainedContents=contents;retainedActors=actors;}
            if(phase>=4 && (contents!=retainedContents || actors!=retainedActors))
            {Finish(false,"Выход/возвращение изменили NPC или содержимое контейнеров.");return;}
            if(players.Select(a=>a.id).Distinct().Count()!=players.Length) {Finish(false,"Повтор ID персонажа");return;}
            var expected=session.World.actors.Where(a=>a.level==self.level).Select(a=>a.id).OrderBy(id=>id).ToArray();
            var visible=FindObjectsByType<ActorVisual>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(v=>v.ActorId).OrderBy(id=>id).ToArray();
            if(!expected.SequenceEqual(visible)) {Finish(false,$"Неверная видимость: expected={string.Join(",",expected)}, actual={string.Join(",",visible)}");return;}
            // Transient effects must expire before counting retained room resources.
            if(FindObjectsByType<ExplosionVisual>(FindObjectsSortMode.None).Length>0) return;
            var projectiles=FindObjectsByType<ProjectileVisual>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            if(projectiles.Any(projectile=>projectile.Level!=self.level))
            {Finish(false,"Снаряд остался в представлении другой локации.");return;}
            if(projectiles.Length>0) return;
            int expectedSprites=session.World.actors.Where(a=>expected.Contains(a.id)).Sum(a=>(Remains.Content.ActorTypes.Get(a.actorType).hasWeapon?1:0)+(Remains.Content.ActorTypes.Get(a.actorType).presentation=="pony"?0:1))+session.World.props.Count(p=>p.level==self.level)+session.World.containers.Count(c=>c.level==self.level)+runtime.Grenades.Flights.Count(f=>f.state.level==self.level);
            var spriteViews=FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int spriteCount=spriteViews.Length;
            var rooms=FindObjectsByType<Remains.Content.RoomAuthoring>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var lighting=FindObjectsByType<RoomLighting>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int lightMaps=Resources.FindObjectsOfTypeAll<Texture2D>().Count(texture=>texture.name.StartsWith("Свет: "));
            int roomMaterials=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("RoomMaterial:"));
            if(spriteCount!=expectedSprites || rooms.Length!=1 || lighting.Length!=1 || lighting[0].gameObject!=rooms[0].gameObject || lightMaps!=1 || roomMaterials!=0)
            {Finish(false,$"Ресурсы локации не освобождены: sprites={spriteCount}/{expectedSprites}, rooms={rooms.Length}, lights={lighting.Length}, lightMaps={lightMaps}, legacyMaterials={roomMaterials}; objects={string.Join(",",spriteViews.Select(view=>view.name))}.");return;}
            if(!acked)
            {
                acked=true;
                if(runtime.Options.Smoke.Contains("-visual-") && (phase==1 || phase==3 || phase==4 || phase==6)) capturePhase=phase;
                else NetworkClient.Send(new SessionProbeAck {phase=phase});
            }
            if(Host && acknowledgements.Count==runtime.Options.ProbePeers) Advance(phase+1);
        }
        private void LateUpdate()
        {
            if(capturePhase<0) return;
            int captured=capturePhase;capturePhase=-1;
            NetworkSceneRecord.Save(runtime,captured);
            NetworkClient.Send(new SessionProbeAck {phase=captured});
        }
        private void Finish(bool success,string detail)
        {
            finished=true;
            string json=JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,peers=runtime.Options.ProbePeers,actor=completedActor??runtime.Session.LocalActorId,stress=runtime.Options.NetworkStress,inventoryHeals=inventoryHeals,inventoryWeaponChanges=inventoryWeaponChanges},true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,json);
            Debug.Log(json);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success,stress;public string detail,actor;public int phase,peers,inventoryHeals,inventoryWeaponChanges;}
    }
}
