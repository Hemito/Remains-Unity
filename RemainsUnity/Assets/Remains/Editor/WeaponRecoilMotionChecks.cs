using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponRecoilMotionChecks
    {
        private static readonly Func<string,bool> noPause=_=>false;
        private static readonly Func<ActorState,bool> noBlock=_=>false;
        private sealed class Fixture
        {
            public WorldState world=new() {worldId="recoil-world"};
            public WeaponDefinition definition;
            public WeaponRecoilMotion motion;
            public ActorState Actor=>world.Find("source");
            public Fixture(bool energy=false,IWeaponRecoilRule rule=null)
            {
                definition=Definition(energy?WeaponRecoilProfile.EnergyCalibration():WeaponRecoilProfile.PistolCalibration());
                world.actors.Add(new ActorState {id="source",owner="a",player=true,connected=true,level="hub",weapon=definition.id,
                    generation=2,lifecycleSequence=3,equippedWeaponItem="item",weaponSelectionRevision=4,
                    inventory={new ItemStack {id="untouched-stack",definition="p10",count=36}}});
                motion=new WeaponRecoilMotion(_=>definition,rule,_=>{});
                Step(0);
            }
            public AttackEvent Shot(string id,bool mirrored=false)=>new()
            {
                id=id,attacker=Actor.id,weapon=Actor.weapon,level=Actor.level,sourceOwner=Actor.owner,
                originX=7,originY=8,endX=20,endY=9,
                rangedPose=new RangedShotPose {worldId=world.worldId,weaponItem=Actor.equippedWeaponItem,
                    selectionRevision=Actor.weaponSelectionRevision,actorGeneration=Actor.generation,
                    actorLifecycle=Actor.lifecycleSequence,mirrored=mirrored,rootX=6,rootY=8,angleDegrees=4}
            };
            public bool Commit(string id,bool mirrored=false)=>motion.CommitShot(world,"a",Actor,Shot(id,mirrored),7);
            public void Step(float dt,long revision=7)=>motion.Step(world,"a",dt,noPause,revision,noBlock);
        }
        public static void Run()
        {
            int count=RunPure();
            Debug.Log($"Отдача владельца: PASS {count} — непрерывность/30–60–144 Гц, пределы, captured side, snapshot/context/pause/reject, модовая кривая и content fingerprint.");
        }
        // No Unity native calls, resource loads, network/session setup or gameplay state injection.
        public static int RunPure()
        {
            int checks=0;
            void Check(bool value,string context) {checks++;if(!value) throw new InvalidOperationException("Отдача: "+context);}
            void Reject(Action action,string context)
            {
                checks++;try {action();} catch(InvalidDataException) {return;} catch(InvalidOperationException) {return;} catch(ArgumentException) {return;}
                throw new InvalidOperationException("Отдача: принят недопустимый случай — "+context);
            }
            var standard=new SmoothWeaponRecoilRule();
            foreach(var profile in new[]{WeaponRecoilProfile.PistolCalibration(),WeaponRecoilProfile.EnergyCalibration()})
            {
                profile.Validate("calibration");
                var first=WeaponRecoilSampling.ValidateSample(standard,profile,0,"first");
                var peak=WeaponRecoilSampling.ValidateSample(standard,profile,profile.riseSeconds,"peak");
                Check(first.horizontal==0 && first.angle==0,"новая impulse скачком меняет trigger pose");
                Check(peak.horizontal==profile.horizontalPeak && peak.angle==profile.anglePeakDegrees,"заданные независимые пики потеряны");
                var positionEnd=WeaponRecoilSampling.ValidateSample(standard,profile,(double)profile.riseSeconds+profile.positionReturnSeconds,"position-end");
                Check(positionEnd.horizontal==0 && positionEnd.angle>0,"угол должен возвращаться отдельно от положения");
                foreach(double seam in new[]{0d,(double)profile.riseSeconds,(double)profile.riseSeconds+profile.positionReturnSeconds,profile.DurationSeconds})
                {
                    var left=standard.Sample(profile,Math.Max(0,seam-.000001));
                    var right=standard.Sample(profile,seam+.000001);
                    Check(Math.Abs(left.horizontal-right.horizontal)<.00001 && Math.Abs(left.angle-right.angle)<.001,"разрыв на границе кривой");
                }
                foreach(double time in new[]{.05,.1,.2,.3,.6})
                {
                    WeaponRecoilPose reference=null;
                    foreach(int fps in new[]{30,60,144})
                    {
                        var fixture=new Fixture(profile.horizontalPeak==.04f);
                        Check(fixture.Commit("fps"),"начальный выстрел не принят");
                        double elapsed=0;
                        while(elapsed<time)
                        {
                            float dt=(float)Math.Min(1d/fps,time-elapsed);
                            fixture.Step(dt);elapsed+=dt;
                        }
                        var sample=fixture.Actor.weaponRecoil;
                        if(reference==null) reference=sample.Copy();
                        Check(Math.Abs(reference.offsetX-sample.offsetX)<.000002 && Math.Abs(reference.angleDegrees-sample.angleDegrees)<.00005,
                            $"отдача зависит от FPS: {fps}, time={time}");
                        Check(fixture.Actor.poseTime==0,"сервис использовал/двигал body pose clock");
                    }
                }
            }
            var burst=new Fixture();
            var attack=burst.Shot("one");string attackJson=StateJson.ToJson(attack),inventory=StateJson.ToJson(burst.Actor.inventory);
            Check(burst.motion.CommitShot(burst.world,"a",burst.Actor,attack,7),"accepted shot отсутствует");
            burst.Step(burst.definition.recoil.riseSeconds);
            Check(burst.Actor.weaponRecoil.offsetX<0 && burst.Actor.weaponRecoil.angleDegrees>0,"правая сторона получила неправильный знак");
            float before=burst.Actor.weaponRecoil.angleDegrees;
            Check(burst.Commit("two") && burst.Actor.weaponRecoil.angleDegrees==before,"новая impulse сдвинула существующую pose в age0");
            burst.Step(burst.definition.recoil.riseSeconds);
            float expected=burst.Actor.weaponRecoil.angleDegrees;
            Check(!burst.Commit("two") && burst.Actor.weaponRecoil.angleDegrees==expected && burst.motion.ActiveImpulses==2,"duplicate добавил impulse");
            Check(burst.motion.Reject("two",burst.world,"a",7) && burst.motion.ActiveImpulses==1 && burst.Actor.weaponRecoil.angleDegrees>0 &&
                burst.Actor.weaponRecoil.angleDegrees<expected,"reject стёр чужую contribution либо оставил свою");
            Check(!burst.Commit("two"),"rejected ID переиграл impulse");
            Check(StateJson.ToJson(attack)==attackJson && StateJson.ToJson(burst.Actor.inventory)==inventory,"сервис переписал launch/боеприпас");
            burst.Step(2);
            Check(burst.Actor.weaponRecoil.offsetX==0 && burst.Actor.weaponRecoil.angleDegrees==0 && !burst.Commit("one"),"expired ID повторно запустил отдачу");
            var cap=new Fixture();
            for(int i=0;i<4;i++) cap.Commit("cap-"+i);
            cap.Step(cap.definition.recoil.riseSeconds);
            Check(cap.Actor.weaponRecoil.offsetX==-cap.definition.recoil.maxHorizontal && cap.Actor.weaponRecoil.angleDegrees==cap.definition.recoil.maxAngleDegrees,"серия превысила/не достигла явных caps");
            var mixed=new Fixture();mixed.Commit("right");mixed.Commit("left",true);mixed.Actor.weaponFlipY=true;
            mixed.Step(mixed.definition.recoil.riseSeconds);
            Check(mixed.Actor.weaponRecoil.offsetX==0 && mixed.Actor.weaponRecoil.angleDegrees==0,"знаки переопределились текущим flip вместо captured side");
            var mirrored=new Fixture();mirrored.Commit("left",true);mirrored.Step(mirrored.definition.recoil.riseSeconds);
            Check(mirrored.Actor.weaponRecoil.offsetX>0 && mirrored.Actor.weaponRecoil.angleDegrees<0,"зеркальная сторона потеряла отвод/подъём");

            var replaced=new Fixture();replaced.Commit("snapshot");replaced.Step(.02f);
            float angleBefore=replaced.Actor.weaponRecoil.angleDegrees;
            replaced.world=StateJson.Copy(replaced.world);replaced.Actor.weaponRecoil=WeaponRecoilPose.Neutral(7);
            replaced.Actor.pose="climb";replaced.Actor.poseTime=-1;replaced.Actor.x=94;
            replaced.Actor.extensions=new System.Collections.Generic.Dictionary<string,JToken> { ["mod-value"]=new JValue(17) };
            replaced.Step(0);
            Check(replaced.Actor.weaponRecoil.angleDegrees==angleBefore && replaced.motion.ActiveImpulses==1,"replacement same context потерял импульс");
            Check(replaced.Actor.x==94 && replaced.Actor.poseTime==-1 && (int)replaced.Actor.extensions["mod-value"]==17,"самплер поменял motion/unknown data");
            replaced.Step(.02f);
            Check(replaced.Actor.weaponRecoil.angleDegrees>angleBefore,"same-world clone не продолжил прежние часы");
            var remote=new ActorState {id="remote",owner="b",player=true,connected=true,weapon="pure-pistol",weaponRecoil=new WeaponRecoilPose {offsetX=.12f,angleDegrees=6,pauseRevision=2}};
            replaced.world.actors.Add(remote);string remoteJson=StateJson.ToJson(remote);replaced.Step(.01f);
            Check(StateJson.ToJson(remote)==remoteJson,"owner service пересчитал remote pose");
            int lookups=0;var cached=new Fixture();
            cached.motion=new WeaponRecoilMotion(_=>{lookups++;return cached.definition;});cached.Step(0);cached.Commit("cached");cached.Step(.02f);
            float cachedAngle=cached.Actor.weaponRecoil.angleDegrees;
            cached.definition.recoil.anglePeakDegrees=9;
            for(int i=0;i<1000;i++) cached.Step(0);
            Check(lookups==1 && cached.Actor.weaponRecoil.angleDegrees==cachedAngle,"покадровый шаг повторно разрешает/читает изменяемый профиль вместо frozen binding");

            foreach(var reset in new Action<ActorState>[]
            {
                a=>a.generation++,a=>a.lifecycleSequence++,a=>a.equippedWeaponItem="other-item",a=>a.weaponSelectionRevision++,
                a=>a.weapon="other-definition",a=>a.level="factory:0",a=>a.down=true,a=>a.connected=false,a=>a.heldItemId="ordinary"
            })
            {
                var fixture=new Fixture();fixture.Commit("reset");fixture.Step(.02f);reset(fixture.Actor);fixture.Step(0);
                Check(fixture.Actor.weaponRecoil.offsetX==0 && fixture.Actor.weaponRecoil.angleDegrees==0 && fixture.motion.ActiveImpulses==0,"контекст/down/absence/held не сбросил state");
            }
            var passive=new Fixture();passive.Actor.level="factory:0";passive.Commit("body");passive.Step(.02f);passive.Actor.connected=false;passive.Step(0);
            Check(passive.Actor.weaponRecoil.angleDegrees==0,"disconnected body продолжает отдачу");
            var owner=new Fixture();owner.Commit("handoff");owner.Step(.02f);owner.Actor.owner="b";owner.Step(0);
            Check(owner.motion.ActiveImpulses==0,"прежний owner хранит рабочую impulse после handoff");
            var nextOwner=new WeaponRecoilMotion(_=>owner.definition);nextOwner.Step(owner.world,"b",0,noPause,7,noBlock);
            Check(owner.Actor.weaponRecoil.angleDegrees==0,"новый owner продолжает saved impulse без transient clock");

            var pause=new Fixture();pause.Commit("pause");pause.Step(.02f);float unpaused=pause.Actor.weaponRecoil.angleDegrees;
            pause.motion.Step(pause.world,"a",0,level=>level=="factory:0",8,noBlock);
            Check(pause.Actor.weaponRecoil.angleDegrees==unpaused && pause.Actor.weaponRecoil.pauseRevision==8 && pause.motion.ActiveImpulses==1,"чужая pause revision сбросила активный уровень");
            pause.motion.Step(pause.world,"a",0,_=>true,9,noBlock);
            Check(pause.Actor.weaponRecoil.angleDegrees==0 && pause.Actor.weaponRecoil.pauseRevision==9 && pause.motion.ActiveImpulses==0,"своя пауза не нейтрализовала pose");
            pause.Step(.01f,10);Check(pause.Actor.weaponRecoil.angleDegrees==0 && !pause.Commit("pause"),"resume переиграл прежний shot ID");
            Check(pause.Commit("after-resume"),"новый shot после resume отклонён");
            pause.Step(.02f);pause.motion.Step(pause.world,"a",0,noPause,10,_=>true);
            Check(pause.Actor.weaponRecoil.angleDegrees==0 && pause.motion.ActiveImpulses==0,"reload blocker не сбросил recoil");
            var rejectionPause=new Fixture();bool pausedNow=false;
            rejectionPause.motion.Step(rejectionPause.world,"a",0,_=>pausedNow,7,noBlock);
            rejectionPause.Commit("pause-reject-one");rejectionPause.Commit("pause-reject-two");
            rejectionPause.motion.Step(rejectionPause.world,"a",.02f,_=>pausedNow,7,noBlock);pausedNow=true;
            rejectionPause.motion.Reject("pause-reject-one",rejectionPause.world,"a",8);
            Check(rejectionPause.Actor.weaponRecoil.angleDegrees==0 && rejectionPause.motion.ActiveImpulses==0,"reject callback после pause изменил замороженный контекст до Step");
            var saved=new Fixture();saved.Actor.weaponRecoil.angleDegrees=3;saved.Actor.weaponRecoil.offsetX=.1f;
            saved.Step(0);Check(saved.Actor.weaponRecoil.angleDegrees==0 && saved.Actor.weaponRecoil.offsetX==0,"новый binding сохраняет бесконечный saved offset");
            saved.Commit("world");saved.Step(.02f);saved.world.worldId="new-world";saved.Step(0);
            Check(saved.Actor.weaponRecoil.angleDegrees==0 && saved.Commit("world"),"WorldId reset не очистил прежний epoch/receipt");
            saved.Step(.02f);saved.motion.Clear();Check(saved.Actor.weaponRecoil.angleDegrees==0 && saved.motion.ActiveActors==0,"Clear не освободил live transient state");

            int warnings=0;var legacy=Definition(null);
            Check(WeaponRecoilProfile.ValidateForWeapon(legacy,"legacy",_=>warnings++)==null && warnings==1,"null legacy profile не имеет visible warning");
            var disabled=WeaponRecoilProfile.Disabled();disabled.Validate("disabled");
            Check(WeaponRecoilProfile.ValidateForWeapon(Definition(disabled),"disabled",_=>warnings++)==disabled && warnings==1,"explicit disabled вызвал fallback warning");
            var disabledFixture=new Fixture();disabledFixture.definition.recoil=disabled;
            Check(!disabledFixture.Commit("disabled") && disabledFixture.motion.ActiveImpulses==0,"disabled profile создал импульс");
            foreach(var bad in new[]
            {
                Changed(p=>p.version=2),Changed(p=>p.horizontalPeak=float.NaN),Changed(p=>p.maxHorizontal=.01f),
                Changed(p=>p.anglePeakDegrees=-1),Changed(p=>p.riseSeconds=0),Changed(p=>p.positionReturnSeconds=float.PositiveInfinity),
                Changed(p=>p.angleReturnSeconds=-1),Changed(p=>p.enabled=false)
            }) Reject(()=>bad.Validate("negative-profile"),"malformed profile");
            var nonProjectile=Definition(WeaponRecoilProfile.PistolCalibration());nonProjectile.projectileSpeed=0;
            Reject(()=>WeaponRecoilProfile.ValidateForWeapon(nonProjectile,"non-projectile"),"enabled non-projectile recoil");
            foreach(var bad in new[]{new BadRule(float.NaN,0),new BadRule(-1,0),new BadRule(1,0),new BadRule(0,20)})
                Reject(()=>WeaponRecoilSampling.ValidateSample(bad,WeaponRecoilProfile.PistolCalibration(),0,"preflight"),"invalid code rule before debit");
            Reject(()=>WeaponRecoilSampling.ValidateSample(new BadRule(.01f,1),WeaponRecoilProfile.PistolCalibration(),1,"tail"),"nonzero expired rule");
            var alternate=new Fixture(rule:new ReducedRule());alternate.Commit("mod-rule");alternate.Step(alternate.definition.recoil.riseSeconds);
            Check(Math.Abs(alternate.Actor.weaponRecoil.angleDegrees-alternate.definition.recoil.anglePeakDegrees*.5f)<.00001,"code mod rule не применяется");
            var lateBad=new Fixture(rule:new LateBadRule());lateBad.Commit("later-invalid");
            Reject(()=>lateBad.Step(.01f),"invalid later code rule output");
            foreach(float dt in new[]{-1f,float.NaN,float.PositiveInfinity}) Reject(()=>new Fixture().Step(dt),"invalid dt");
            var invalidShot=new Fixture();var badPose=invalidShot.Shot("bad-pose");badPose.rangedPose.rootX=float.NaN;
            Reject(()=>invalidShot.motion.CommitShot(invalidShot.world,"a",invalidShot.Actor,badPose,7),"invalid historical pose");
            var old=invalidShot.Shot("old-selection");old.rangedPose.selectionRevision--;
            Check(!invalidShot.motion.CommitShot(invalidShot.world,"a",invalidShot.Actor,old,7),"stale shot context создал impulse");

            string noMods=Path.Combine(Path.GetTempPath(),"RemainsRecoilMissingMods-"+Guid.NewGuid().ToString("N"));
            string Base(WeaponDefinition definition)=>StateJson.ToJson(new WeaponList {weapons=new[]{definition}});
            var baseDefinition=Definition(WeaponRecoilProfile.PistolCalibration());string json=Base(baseDefinition);
            var loaded=WeaponCatalogLoader.Load(json,noMods);
            Check(loaded.definitions[baseDefinition.id].recoil.anglePeakDegrees==4 && loaded.definitions[baseDefinition.id].recoil.enabled,"recoil profile не прошёл JSON catalog");
            baseDefinition.recoil.anglePeakDegrees=3;
            Check(loaded.fingerprint!=WeaponCatalogLoader.Load(Base(baseDefinition),noMods).fingerprint,"gameplay recoil change не меняет content fingerprint");
            var badCatalog=Definition(WeaponRecoilProfile.PistolCalibration());badCatalog.recoil.maxAngleDegrees=1;
            Reject(()=>WeaponCatalogLoader.Load(Base(badCatalog),noMods),"invalid mod/catalog profile опубликован");
            return checks;
        }
        private static WeaponDefinition Definition(WeaponRecoilProfile profile)=>new()
        {id="pure-pistol",range=20,cooldown=.2f,projectileSpeed=90,recoil=profile};
        private static WeaponRecoilProfile Changed(Action<WeaponRecoilProfile> change)
        {var profile=WeaponRecoilProfile.PistolCalibration();change(profile);return profile;}
        private sealed class BadRule : IWeaponRecoilRule
        {
            private readonly float horizontal,angle;
            public BadRule(float horizontal,float angle) {this.horizontal=horizontal;this.angle=angle;}
            public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double age)=>new() {horizontal=horizontal,angle=angle};
        }
        private sealed class ReducedRule : IWeaponRecoilRule
        {
            private readonly SmoothWeaponRecoilRule standard=new();
            public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double age)
            {var sample=standard.Sample(profile,age);sample.horizontal*=.5f;sample.angle*=.5f;return sample;}
        }
        private sealed class LateBadRule : IWeaponRecoilRule
        {
            public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double age)=>age==0?default:new() {angle=float.NaN};
        }
    }
}
