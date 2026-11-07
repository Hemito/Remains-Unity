using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct RevivalProbePhase : NetworkMessage {public int phase;}
    public struct RevivalProbeFinish : NetworkMessage { }
    // Actor state is read-only here. Movement, attacks and healing use ordinary commands.
    [DefaultExecutionOrder(10000)]
    public sealed class NetworkRevivalProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase,startDamage;
        private float since,deadline;
        private bool grenade,finished,advancing,damageCancelled,healingBeforeDamage,releaseProgress,movementProgress;
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        private bool PauseScenario=>runtime.Options.Smoke.Contains("-pause-");
        private NetworkPauseWindow pauseWindow;
        private bool pauseStarted,pauseRunning;
        private bool Host=>runtime.Options.Role=="host";
        private bool CaptureFrames=>runtime.Options.Smoke.Contains("-frames-");
        private bool capturedDown,capturedRevived;
        private string grenadeId;
        private bool holdBeforeBlast;
        private float healerHealthBeforeBlast,blastDamage;
        private int blastHits;
        private RevivalExpectation resumeExpectation;
        private bool failureCaptureRequested;
        private int healingVoices,healingVoice;
        private bool healingPlaying,healingMuted;
        private float healingVolume;
        private string healingTransaction,healingClip;
        private ActorState Victim=>runtime.Session.World?.Find("player:session-client-1");
        private ActorState Healer=>runtime.Session.World?.Find("player:session-host");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;runtime.CommandSource=Command;
            value.HealingAudio.HealingPlayed+=ObserveHealing;
            value.Combat.DamageApplied+=ObserveDamage;
            if(Resume && File.Exists(ExpectationPath)) resumeExpectation=JsonUtility.FromJson<RevivalExpectation>(File.ReadAllText(ExpectationPath));
            runtime.Grenades.Created+=flight=>
            {if(phase==5 && flight.state.attacker==Healer?.id) grenadeId=flight.state.id;};
            if(PauseScenario) pauseWindow=new NetworkPauseWindow(this,value.Session,value.Options.ProbePeers,PauseSnapshot,
                ()=>Victim!=null && Victim.down && InventoryJournal.Count(Healer,"heal")==5 &&
                    (!Host || runtime.Revival.Progress>0 && runtime.Revival.Progress<1 && !runtime.Revival.Pending),
                ()=>finished,reason=>Finish(false,reason));
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<RevivalProbePhase>(message=>
                {phase=message.phase;since=Time.unscaledTime;advancing=false;if(phase==5 && Host) {startDamage=Healer.damageSequence;healerHealthBeforeBlast=Healer.health;}});
                NetworkClient.RegisterHandler<RevivalProbeFinish>(_=>Finish(Validate(),Resume?"Перезапуск: точное здоровье, расход одной хилки и счётчики урона сохранены.":"Обычные команды: нокдаун, отмена E/расстоянием/уроном, поднятие и расход одной хилки; сохранение проверено."));
            };
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null) return default;
            var command=new PlayerCommand();
            if(Resume) return command;
            if(phase==0)
            {
                float destination=Host?25:self.id=="player:session-client-1"?23.8f:28+int.Parse(self.character.Split('-').Last());
                command.movement=Vector2.right*(Mathf.Abs(destination-self.x)>.05f?Mathf.Sign(destination-self.x):0);return command;
            }
            if(!Host) return command;
            float age=Time.unscaledTime-since;
            if(phase==1) {command.weapon="pistol";command.hasAim=true;command.aim=Victim.Position+Vector2.up*.55f;command.fire=!Victim.down;}
            if(phase==2) command.revive=age<.55f;
            if(phase==3) {command.movement=self.x<27.4f?Vector2.right:Vector2.zero;command.revive=true;}
            if(phase==4) command.movement=Mathf.Abs(self.x-25)>.05f?Vector2.right*Mathf.Sign(25-self.x):Vector2.zero;
            if(phase==5)
            {
                if(!grenade)
                {
                    if(!TryBlastAim(self,out var aim)) {Finish(false,"Нет броска с несмертельным уроном у точки поднятия.");return default;}
                    grenade=true;
                    return new PlayerCommand {grenade=true,hasAim=true,aim=aim};
                }
                if(grenadeId!=null && runtime.Grenades.Flights.Any(flight=>flight.state.id==grenadeId &&
                    flight.state.remaining>0 && flight.state.remaining<=1.15f)) holdBeforeBlast=true;
                // Keep E pressed through the blast; release only after the progress actually resets.
                command.revive=holdBeforeBlast && !damageCancelled;
            }
            if(phase==6) command.revive=true;
            if(command.revive) {command.hasAim=true;command.aim=InventorySource.BodyBounds(Victim).center;}
            return command;
        }
        private bool TryBlastAim(ActorState actor,out Vector2 aim)
        {
            aim=default;
            var stack=GrenadeItems.Next(actor);
            if(stack==null) return false;
            var weapon=WeaponCatalog.Get(stack.definition);
            var room=runtime.Geometry.Get(actor.level);
            for(int degrees=0;degrees<360;degrees+=30)
            {
                float angle=degrees*Mathf.Deg2Rad;
                var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                for(float reach=.5f;reach<=weapon.grenadeFullEffortDistance+2;reach+=.5f)
                {
                    var target=actor.BodyPosition+direction*reach;
                    if(!HeldItemPose.TryQuickLaunch(actor,target,room,out var launch)) continue;
                    var blast=GrenadeAim.Burst(room,launch.origin,launch.velocity,weapon.id);
                    float distance=Anatomy.BlastDistance(actor,blast);
                    // Even the highest damage roll here leaves this healer standing.
                    if(distance<weapon.range*.81f || distance>weapon.range*.9f) continue;
                    if(weapon.damage*1.3f*(2-2*distance/weapon.range)>=actor.health) continue;
                    aim=target;return true;
                }
            }
            return false;
        }
        private void ObserveDamage(DamageResult result)
        {
            if(!Host || phase!=5 || result.actor!=Healer?.id || result.attack!=grenadeId) return;
            blastHits++;blastDamage+=result.amount;
        }
        private void Next()
        {if(advancing) return;advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=phase+1});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут поднятия: phase={phase}, hp={Victim?.health}, healer={Healer?.health}, x={Healer?.x}/{Victim?.x}, progress={runtime.Revival.Progress}.");return;}
            if(!Host || advancing || runtime.Session.World?.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            if(pauseRunning) return;
            if(PauseScenario && phase==6 && !pauseStarted && runtime.Revival.Progress>=.3f)
            {pauseStarted=true;pauseRunning=true;StartCoroutine(PauseRevival());return;}
            if(Resume && phase==0)
            {
                if(!MatchesResume())
                {Finish(false,"Перезапуск потерял здоровье, расход предмета или историю урона.");return;}
                advancing=true;NetworkServer.SendToAll(new RevivalProbePhase {phase=7});return;
            }
            float age=Time.unscaledTime-since;
            if(phase>=2 && phase<=5 && (!Victim.down || Victim.health!=0 || InventoryJournal.Count(Healer,"heal")!=5))
            {Finish(false,$"Прерванное поднятие изменило цель или потратило предмет: phase={phase}, victimHP={Victim.health}, victimDown={Victim.down}, healerHP={Healer.health}, healerDamageSequence={Healer.damageSequence}, startDamage={startDamage}, healCount={InventoryJournal.Count(Healer,"heal")}, progress={runtime.Revival.Progress}, grenadeId={grenadeId ?? "не наблюдалась"}.");return;}
            if(phase==2 && runtime.Revival.Progress>0) releaseProgress=true;
            if(phase==3 && runtime.Revival.Progress>0) movementProgress=true;
            if(phase==5 && Healer.damageSequence==startDamage && runtime.Revival.Progress>.2f) healingBeforeDamage=true;
            if(phase==5 && blastHits==1 && Healer.damageSequence==startDamage+1 && runtime.Revival.Progress==0) damageCancelled=true;
            switch(phase)
            {
                case 0:
                    if(Mathf.Abs(Healer.x-25)<.1f && Mathf.Abs(Victim.x-23.8f)<.1f &&
                        runtime.Session.World.actors.Where(a=>a.player && a.id!=Healer.id && a.id!=Victim.id).All(a=>a.x>29)) Next();break;
                case 1: if(Victim.down) Next();break;
                case 2: if(age>2 && releaseProgress && runtime.Revival.Progress==0) Next();break;
                case 3: if(age>2 && movementProgress && Vector2.Distance(Healer.Position,Victim.Position)>2 && runtime.Revival.Progress==0) Next();break;
                case 4: if(Mathf.Abs(Healer.x-25)<.1f) Next();break;
                case 5:
                    if(damageCancelled && healingBeforeDamage && blastDamage>0 && !Healer.down && Healer.health>0 &&
                        Mathf.Abs(Healer.health-(healerHealthBeforeBlast-blastDamage))<.001f && runtime.Revival.Progress==0) Next();break;
                case 6: if(!Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4) Next();break;
                case 7: if(age>4) {advancing=true;NetworkServer.SendToAll(new RevivalProbeFinish());}break;
            }
        }
        private string ExpectationPath=>Path.Combine(runtime.Session.Saves.CharacterDirectoryPath,"revival-expectation.json");
        private bool MatchesResume()=>resumeExpectation!=null && resumeExpectation.world==runtime.Session.World?.worldId &&
            resumeExpectation.healer==Healer?.id && resumeExpectation.victim==Victim?.id &&
            Mathf.Abs(resumeExpectation.healerHealth-Healer.health)<.001f && resumeExpectation.victimHealth==Victim.health &&
            resumeExpectation.healerDamage==Healer.damageSequence && resumeExpectation.victimDamage==Victim.damageSequence &&
            resumeExpectation.healerMedkits==InventoryJournal.Count(Healer,"heal") && !Healer.down && !Victim.down;
        private bool Validate()
        {
            var saved=runtime.Session.Saves.LoadCharacter();var self=runtime.Session.LocalActor;
            return healingVoices==(Resume?0:1) && runtime.HealingAudio.Accepted==(Resume?0:1) && runtime.HealingAudio.Played==(Resume?0:1) &&
                (!Resume || MatchesResume()) && (Resume || !Host || damageCancelled && healingBeforeDamage && blastHits==1) &&
                (!CaptureFrames || capturedDown && capturedRevived) && Victim!=null && !Victim.down && Victim.health==35 && InventoryJournal.Count(Healer,"heal")==4 &&
                saved!=null && self!=null && saved.id==self.id && saved.health==self.health && saved.damageSequence==self.damageSequence &&
                InventoryJournal.Count(saved,"heal")==InventoryJournal.Count(self,"heal");
        }
        private void LateUpdate()
        {
            if(failureCaptureRequested)
            {
                failureCaptureRequested=false;
                try {NetworkSceneRecord.Save(runtime,100+phase);}
                catch(Exception error) {Debug.LogWarning($"Не удалось сохранить кадр сбоя поднятия: phase={phase}, grenadeId={grenadeId ?? "не наблюдалась"}, reason={error.Message}.",this);}
            }
            if(!CaptureFrames || finished || runtime.Session.LocalActor==null || Time.unscaledTime-since<.25f) return;
            if(phase==2 && !capturedDown && Victim.down)
            {if(!CheckWeaponVisible(false)) return;NetworkSceneRecord.Save(runtime,2);capturedDown=true;}
            if(phase==7 && !capturedRevived && !Victim.down && Victim.health==35)
            {if(!CheckWeaponVisible(true)) return;NetworkSceneRecord.Save(runtime,7);capturedRevived=true;}
        }
        private bool CheckWeaponVisible(bool visible)
        {
            var views=FindObjectsByType<Remains.Presentation.WeaponView>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            int matches=0;
            foreach(var view in views)
            {
                var group=view.GetComponentInParent<Remains.Presentation.ActorVisualGroup>(true);
                if(group==null || group.ActorId!=Victim.id) continue;
                matches++;
                if(view.gameObject.activeInHierarchy!=visible)
                {Finish(false,$"Неверная видимость целого оружия: actor={Victim.id}, down={Victim.down}, expected={visible}.");return false;}
            }
            if(matches!=1) {Finish(false,$"Ожидалось одно оружие пострадавшего: actor={Victim.id}, count={matches}.");return false;}
            return true;
        }
        private float[] PauseSnapshot()=>new[]{Victim.health,Healer.health,(float)InventoryJournal.Count(Healer,"heal"),
            Victim.x,Victim.y,Healer.x,Healer.y,runtime.Session.LocalActor.poseTime,
            Host?runtime.Revival.Progress:0,Host && runtime.Revival.Pending?1:0};
        private IEnumerator PauseRevival()
        {yield return pauseWindow.Run("поднятие союзника");pauseRunning=false;}
        private void ObserveHealing(HealingSoundCue cue,AudioClip clip)
        {
            var voice=runtime.Audio.GetComponentsInChildren<AudioSource>().LastOrDefault(v=>v.clip==clip && v.volume>0);
            if(Resume || cue.actor!=Healer?.id || cue.definition!="heal" || clip.name!="healing-item-use" || voice==null || !voice.isPlaying || healingVoices!=0)
            {Finish(false,"Прерванное/повторное поднятие создало звук либо подтверждённое поднятие потеряло настоящий голос.");return;}
            healingVoices++;healingVoice=voice.GetInstanceID();healingVolume=voice.volume;healingPlaying=voice.isPlaying;healingMuted=voice.mute;
            healingTransaction=cue.transaction;healingClip=clip.name;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            if(PauseScenario) {success &= pauseWindow.Verified==1;detail+=$" Проверенные паузы поднятия: {pauseWindow.Verified}.";}
            if(success && !Resume)
            {
                var expected=new RevivalExpectation {world=runtime.Session.World.worldId,healer=Healer.id,victim=Victim.id,
                    healerHealth=Healer.health,victimHealth=Victim.health,healerDamage=Healer.damageSequence,victimDamage=Victim.damageSequence,
                    healerMedkits=InventoryJournal.Count(Healer,"heal")};
                File.WriteAllText(ExpectationPath,JsonUtility.ToJson(expected,true));
            }
            failureCaptureRequested=!success && CaptureFrames && runtime.Session.LocalActor!=null;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,phase=phase,
                healingVoices=healingVoices,healingVoice=healingVoice,healingVolume=healingVolume,healingPlaying=healingPlaying,
                healingMuted=healingMuted,healingTransaction=healingTransaction,healingClip=healingClip,
                blastDamage=blastDamage,blastHits=blastHits,healingBeforeDamage=healingBeforeDamage,damageCancelled=damageCancelled},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        private void OnDestroy() {if(runtime!=null) {runtime.HealingAudio.HealingPlayed-=ObserveHealing;runtime.Combat.DamageApplied-=ObserveDamage;}}
        [Serializable] private sealed class RevivalExpectation
        {public string world,healer,victim;public float healerHealth,victimHealth;public int healerDamage,victimDamage,healerMedkits;}
        [Serializable] private sealed class Report {public bool success;public string detail;public int phase,healingVoices,healingVoice,blastHits;public float healingVolume,blastDamage;public bool healingPlaying,healingMuted,healingBeforeDamage,damageCancelled;public string healingTransaction,healingClip;}
    }
}
