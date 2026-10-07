using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Networking;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Native resource/renderer checks. The detached reference shares the same
    // graphic frame, but has its own ID and intentionally has no support binding.
    public static class ActorBodyConsumerChecks
    {
        [Serializable] private sealed class Observation
        {
            public string pose,outcome,texture,movementSound;
            public int frame,facing,partIndex,pixelX,pixelY;
            public float anchorX,anchorY,bodyX,bodyY,shift,contactX,contactY,surfaceX,surfaceY,surfaceWidth,surfaceHeight;
            public bool bound,footProfile,grounded,contact,anchorSupported,bodySupported,consumersChecked;
        }
        [Serializable] private sealed class Evidence
        {
            public bool success;
            public string detail,room;
            public int cases,distinctShiftCases,elevationContrastCases;
            public float minimumShift=float.MaxValue,maximumShift;
            public List<Observation> observations=new();
            public List<DamageResult> damageResults=new();
            public float damageOriginShift,damagePostDownBodyX,damagePostDownBodyY;
            public int damageAppliedEvents,damageSequence,damageReceipts;
        }
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();WeaponCatalog.Load();PonyVisual.Prepare();FootSupportCatalog.Load();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWarehouseDraft.Path);
            Require(prefab!=null,"Нет сохранённого склада для проверки body consumers: "+FactoryWarehouseDraft.Path);
            var room=prefab.GetComponent<RoomAuthoring>().CreateLayout(false);
            string original=StateJson.ToJson(room);var evidence=new Evidence {room=room.id};
            var actor=FindActor(room);
            ActorState shiftedWitness=null;
            var root=new GameObject("Проверка общей регистрации тела");
            try
            {
                var pony=new GameObject("Составная пони");pony.transform.SetParent(root.transform,false);
                var visual=pony.AddComponent<PonyVisual>();visual.Bind(actor);
                foreach(string pose in new[]{"slopeStandUp","slopeStandDown","slopeWalkUp","slopeWalkDown"})
                foreach(int facing in new[]{-1,1})
                {
                    var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose);
                    Require(clip!=null,"Нет настоящего slope clip: "+pose);
                    for(int frame=0;frame<clip.frames.Length;frame++)
                    {
                        actor.pose=pose;actor.facing=facing;actor.vx=pose.StartsWith("slopeWalk",StringComparison.Ordinal)?facing*.2f:0;
                        actor.poseTime=(frame+.1f)/clip.fps;
                        var support=ActorBodySupport.Sample(actor);
                        var observation=Observe(actor,room,support);evidence.observations.Add(observation);
                        evidence.minimumShift=Mathf.Min(evidence.minimumShift,observation.shift);
                        evidence.maximumShift=Mathf.Max(evidence.maximumShift,observation.shift);
                        if(observation.shift>.01f)
                        {
                            evidence.distinctShiftCases++;
                            if(shiftedWitness==null) shiftedWitness=actor.Copy();
                        }
                        if(observation.anchorSupported && !observation.bodySupported) evidence.elevationContrastCases++;
                        Require(support.IsBound && support.HasFootProfile && support.Grounded && support.HasContact,
                            "Нет подтверждённой конечной опоры: "+Context(actor,support));
                        // A true contact can already lie at the conservative anchor. All
                        // frames still exercise consumers; discrimination belongs to the set.
                        try
                        {
                            string state=StateJson.ToJson(actor);
                            CheckDraw(actor,visual);CheckAnatomyAndBounds(actor);CheckCamera(actor,root);
                            Require(StateJson.ToJson(actor)==state,"Отрисовка/камера/анатомия изменили сохраняемые поля actor.");
                            CheckWeaponsAndQuickThrow(actor);CheckElevation(actor,room);
                        }
                        catch(Exception error) {throw new InvalidOperationException(error.Message+" "+Context(actor,support),error);}
                        observation.consumersChecked=true;evidence.cases++;
                    }
                }
                Require(evidence.cases==72,"Изменился явный набор настоящих slope frames/facings: "+evidence.cases);
                Require(evidence.distinctShiftCases>0,"Набор не содержит различимого body shift >.01m; consumers не получили независимый registration witness.");
                Require(evidence.elevationContrastCases>0,"Набор не различает grounded anchor и неподдержанный fitted body для проверки подъёма оружия.");
                CheckDamageOrigin(shiftedWitness,room,evidence);
                CheckBoundAudio(actor,room);CheckAtlas(room,actor.Position);CheckClimbExit(actor,visual);
                Require(StateJson.ToJson(room)==original,"Consumers изменили геометрию сохранённого склада.");
                evidence.success=true;
            }
            catch(Exception error) {evidence.detail=error.Message;throw;}
            finally {ActorBodySupport.Unbind(actor);UnityEngine.Object.DestroyImmediate(root);SaveEvidence(evidence);}
            Debug.Log($"Body consumers: PASS — {evidence.cases} настоящих slope frames/facings; shift>.01m={evidence.distinctShiftCases}, anchor/body elevation contrasts={evidence.elevationContrastCases}, shift range={evidence.minimumShift:R}..{evidence.maximumShift:R}m. Составной renderer, анатомия, camera envelope/target, muzzle/casing/held/quickG читают одно тело; {evidence.damageAppliedEvents} чисел урона сохраняют историческую body регистрацию при hit/down без повторов. Anchor/save неизменны, atlas и climb явно остаются на anchor. Это native Editor проверка, не ordinary network маршрут.");
        }

        private static Observation Observe(ActorState actor,RoomLayout room,ActorBodySupportSample support)
        {
            var hull=Anatomy.Movement(actor);var surface=support.SurfaceBounds;
            return new Observation {pose=support.Pose,frame=support.Frame,facing=support.Facing,outcome=support.Outcome.ToString(),
                anchorX=support.Anchor.x,anchorY=support.Anchor.y,bodyX=support.BodyPosition.x,bodyY=support.BodyPosition.y,
                shift=Vector2.Distance(support.Anchor,support.BodyPosition),contactX=support.ContactPoint.x,contactY=support.ContactPoint.y,
                surfaceX=surface.x,surfaceY=surface.y,surfaceWidth=surface.width,surfaceHeight=surface.height,
                texture=support.Texture,movementSound=support.MovementSound,partIndex=support.PartIndex,pixelX=support.PixelX,pixelY=support.PixelY,
                bound=support.IsBound,footProfile=support.HasFootProfile,grounded=support.Grounded,contact=support.HasContact,
                anchorSupported=ActorTerrainMotion.Supported(room,hull,support.Anchor),bodySupported=ActorTerrainMotion.Supported(room,hull,support.BodyPosition)};
        }
        private static string Context(ActorState actor,ActorBodySupportSample support)
            =>$"actor={actor.id}, pose={support.Pose}, frame={support.Frame}, facing={support.Facing}, anchor=({support.Anchor.x:R},{support.Anchor.y:R}), body=({support.BodyPosition.x:R},{support.BodyPosition.y:R}), shift={Vector2.Distance(support.Anchor,support.BodyPosition):R}, outcome={support.Outcome}, surface={support.SurfaceBounds}, part={support.PartIndex}, pixel=({support.PixelX},{support.PixelY}).";
        private static void SaveEvidence(Evidence evidence)
        {
            try
            {
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/body-consumers"));
                Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"verification.json"),StateJson.ToJson(evidence,true));
            }
            catch(Exception error)
            {
                Debug.LogError("Не записано свидетельство body consumers: "+error.Message);
                if(evidence.success) throw;
            }
        }

        private static ActorState FindActor(RoomLayout room)
        {
            var hull=Anatomy.Movement("pony");
            foreach(var surface in room.platforms)
            {
                if(surface.slopeDirection==0) continue;
                for(int step=4;step<=12;step++)
                {
                    float x=surface.x+surface.width*step/16;
                    var actor=new ActorState {id="body-consumer",player=true,connected=true,level="factory:0",pose="slopeStandUp",
                        Position=new Vector2(x,RoomSupport.Top(surface,x,hull.width))};
                    if(!ActorTerrainMotion.IsClear(room,hull,actor.Position) || !ActorTerrainMotion.Supported(room,hull,actor.Position)) continue;
                    ActorBodySupport.Bind(actor,room);var sample=ActorBodySupport.Sample(actor);
                    if(sample.Grounded && sample.HasContact && (sample.BodyPosition-actor.Position).sqrMagnitude>.0001f) return actor;
                    ActorBodySupport.Unbind(actor);
                }
            }
            throw new InvalidOperationException("Body consumers: нет clear/supported конечного уклона в настоящем складе; геометрия не заменена.");
        }
        private static ActorState Detached(ActorState actor)
        {var reference=actor.Copy();reference.id="detached-body-consumer";return reference;}
        private static void CheckDraw(ActorState actor,PonyVisual visual)
        {
            visual.DrawPose(actor.poseTime);
            Near(visual.transform.position,actor.BodyPosition,"Renderer root остался на movement anchor");
            Require(visual.DrawnClip==ActorPose.Clip(actor) && visual.DrawnFrame==PonyMotionFrames.Sample(actor,actor.poseTime),"Renderer использует другую фактическую позу/фазу.");
            Require(visual.TryGetDrawnBounds(out var bounds),"Нет actual drawn bounds после DrawPose.");
            var expected=PonyVisual.WorldBounds(actor);
            Near(bounds.center,expected.center,"Actual renderer и prepared WorldBounds имеют разную регистрацию");
            Near(bounds.size,expected.size,"Actual renderer и prepared WorldBounds имеют разные части");
        }
        private static void CheckAnatomyAndBounds(ActorState actor)
        {
            var reference=Detached(actor);var delta=actor.BodyPosition-reference.BodyPosition;
            foreach(string region in new[]{"head","torso","legs"})
            {
                bool current=Anatomy.TryRegion(actor,region,out var center,out float radius);
                bool previous=Anatomy.TryRegion(reference,region,out var oldCenter,out float oldRadius);
                Require(current && previous,"Нет настоящей области: "+region);
                Near(center,oldCenter+delta,"Область повреждения оторвалась от fitted body: "+region);
                Require(radius==oldRadius,"Body fit изменил радиус области: "+region);
            }
            var camera=PonyVisual.CameraBounds(actor);var oldCamera=PonyVisual.CameraBounds(reference);
            Near(camera.center,oldCamera.center+(Vector3)delta,"Camera envelope остался на anchor");
            Require(camera.size==oldCamera.size,"Body fit изменил общий camera envelope.");
        }
        private static void CheckCamera(ActorState actor,GameObject root)
        {
            var camera=root.GetComponentInChildren<Camera>();
            if(camera==null) {var view=new GameObject("Камера тела");view.transform.SetParent(root.transform,false);camera=view.AddComponent<Camera>();}
            camera.orthographic=true;camera.orthographicSize=5;camera.aspect=16f/9;
            // Wide camera limits isolate the target from legitimate room-edge clamps.
            var follower=new CameraFollower(camera,new RoomLayout {id="body-camera-limit",left=-100,right=100,bottom=-100,top=100});
            camera.transform.position=new Vector3(actor.x,actor.y+1,-10);follower.Step(actor,10);
            Near((Vector2)camera.transform.position,actor.BodyPosition+Vector2.up,"Camera target остался на anchor");
        }
        private static void CheckWeaponsAndQuickThrow(ActorState actor)
        {
            var reference=Detached(actor);var delta=actor.BodyPosition-reference.BodyPosition;
            var direction=new Vector2(actor.facing*4,1);
            foreach(string weapon in new[]{"pistol","energy","spear","crowbar"})
            {
                actor.weapon=reference.weapon=weapon;
                WeaponPose.Aim(actor,WeaponPose.Origin(actor)+direction);WeaponPose.Aim(reference,WeaponPose.Origin(reference)+direction);
                Near(WeaponPose.Mount(actor),WeaponPose.Mount(reference)+delta,"Weapon root остался на anchor: "+weapon);
                if(WeaponRigs.Get(weapon).HasMuzzle)
                {
                    var current=WeaponPose.SampleRanged(actor);var old=WeaponPose.SampleRanged(reference);
                    Near(current.Muzzle,old.Muzzle+delta,"Дуло оторвалось от body: "+weapon);
                    Near(current.Socket(new Vector2(.13f,.07f)),old.Socket(new Vector2(.13f,.07f))+delta,"Captured casing socket оторвался от body");
                    var captured=current.Capture(actor,"consumer-world");var saved=new Vector2(captured.rootX,captured.rootY);
                    float time=actor.poseTime;actor.poseTime+=.04f;
                    Require(new Vector2(captured.rootX,captured.rootY)==saved,"Поздняя body фаза изменила captured shot");actor.poseTime=time;
                }
            }
            actor.heldItemId=reference.heldItemId="consumer-held";
            var aim=HeldItemPose.Anchor(actor)+direction;
            Require(HeldItemPose.Aim(actor,aim) && HeldItemPose.Aim(reference,aim-delta),"Не готова реальная 2D held pose.");
            Near(HeldItemPose.Mount(actor),HeldItemPose.Mount(reference)+delta,"Удерживаемый предмет остался на anchor");
            var held=HeldItemPose.Capture(actor,aim);string before=StateJson.ToJson(actor);
            Require(HeldItemPose.TryQuickLaunch(actor,aim,null,out var quick),"Не готова detached quickG selection.");
            Require(StateJson.ToJson(actor)==before,"QuickG изменил текущую selection/held pose/inventory/anchor.");
            // Compare a separately aimed grenade, not an alias under the bound actor ID.
            var grenade=Detached(actor);grenade.weapon="grenade";grenade.heldItemId=null;
            grenade.Position=actor.BodyPosition;
            Require(HeldItemPose.Aim(grenade,aim),"Не готова контрольная grenade pose.");
            var expected=HeldItemPose.Capture(grenade,aim);
            Near(quick.origin,expected.origin,"QuickG потерял fitted body из-за alias clone");
            Near(quick.velocity,expected.velocity,"QuickG изменил импульс/цель из-за body registration");
            Require(Vector2.Distance(held.origin,HeldItemPose.Mount(actor))<.0001f,"Capture и held sprite имеют разные origins.");
            actor.heldItemId=null;
        }
        private static void CheckAtlas(RoomLayout room,Vector2 anchor)
        {
            var actor=new ActorState {id="atlas-body-consumer",anatomy="raider",actorType="raider",Position=anchor};
            ActorBodySupport.Bind(actor,room);
            try
            {
                var sample=ActorBodySupport.Sample(actor);
                Require(sample.IsBound && !sample.HasFootProfile && sample.BodyPosition==anchor,"Atlas получил чужую foot capability/body offset.");
                var reference=Detached(actor);
                Near(AtlasWeaponSockets.Anchor(actor),AtlasWeaponSockets.Anchor(reference),"Atlas socket изменился без foot capability.");
            }
            finally {ActorBodySupport.Unbind(actor);}
        }
        private static void CheckElevation(ActorState actor,RoomLayout room)
        {
            actor.weapon="pistol";actor.heldItemId=null;actor.weaponLift=0;
            Require(ActorBodySupport.Sample(actor).Grounded && ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),
                "Проверка подъёма потеряла physical anchor support.");
            var elevation=new WeaponElevation();elevation.Step(actor,room,true,WeaponRigs.RaiseDelay+.01f);
            Require(actor.weaponLift==WeaponRigs.RaiseHeight,"Подъём оружия проверил опору по fitted body вместо physical anchor.");
            elevation.Step(actor,room,false,0);Require(actor.weaponLift==0,"Отпускание подъёма оставило старую высоту оружия.");
        }
        private static void CheckDamageOrigin(ActorState source,RoomLayout room,Evidence evidence)
        {
            Require(source!=null,"Нет actual shifted frame для исторической квитанции урона.");
            var victim=source.Copy();victim.id="body-damage-consumer";
            var rules=new PrototypeDamageRule();float amount=rules.Calculate("pistol",0);
            amount=new TypeHitProtectionRule().Resolve(victim,new AttackEvent {weapon="pistol"},amount);
            Require(amount>0 && float.IsFinite(amount),"Настоящий pistol profile не даёт положительный урон проверяемой пони.");
            victim.health=amount*2;
            ActorBodySupport.Bind(victim,room);FloatingDamageNumber number=null;
            try
            {
                var sample=ActorBodySupport.Sample(victim);var expected=sample.BodyPosition;var anchor=victim.Position;
                evidence.damageOriginShift=Vector2.Distance(expected,anchor);
                Require(sample.Grounded && sample.HasContact && evidence.damageOriginShift>.01f,
                    "Квитанция урона не получила различимый actual body witness: "+Context(victim,sample));
                var receiver=new DamageReceiver(rules);DamageResult result=default;int applied=0;
                receiver.Applied+=value=>{result=value;applied++;evidence.damageResults.Add(value);};
                var hit=new AttackEvent {id="body-hit",attacker="other-body",level=victim.level,weapon="pistol"};
                string untouched=StateJson.ToJson(victim);
                Require(!receiver.Apply(victim,hit,false) && applied==0 && StateJson.ToJson(victim)==untouched,
                    "Промах изменил slope victim или создал число урона.");
                Require(receiver.Apply(victim,hit,true) && !victim.down && victim.health==amount &&
                    applied==1 && victim.damageSequence==1 && victim.resolvedAttacks.Count==1 && result.attack==hit.id &&
                    result.actor==victim.id && result.level==victim.level && result.generation==victim.generation && result.amount==amount,
                    "Первый slope hit не дал одну точную квитанцию фактического урона.");
                Near(new Vector2(result.x,result.y),expected,"Квитанция nonlethal hit осталась на movement anchor");
                string afterHit=StateJson.ToJson(victim);
                Require(!receiver.Apply(victim,hit,true) && applied==1 && StateJson.ToJson(victim)==afterHit,
                    "Повтор slope hit изменил здоровье/receipts/sequence или число урона.");
                var down=new AttackEvent {id="body-down",attacker=hit.attacker,level=victim.level,weapon="pistol"};
                Require(receiver.Apply(victim,down,true) && victim.down && victim.pose=="down" && victim.health==0 &&
                    applied==2 && victim.damageSequence==2 && victim.resolvedAttacks.Count==2 && result.attack==down.id && result.amount==amount,
                    "Второй slope hit не подтвердил однократный нокаут.");
                Require(victim.Position==anchor && victim.BodyPosition==anchor,
                    "Нокаут изменил movement anchor либо сохранил living foot fit.");
                Near(new Vector2(result.x,result.y),expected,"Нокаут потерял историческое тело до смены down позы");
                string afterDown=StateJson.ToJson(victim);var historical=result;
                Require(!receiver.Apply(victim,down,true) && applied==2 && StateJson.ToJson(victim)==afterDown &&
                    result.x==historical.x && result.y==historical.y,"Повтор нокаута изменил историческую квитанцию.");
                var prefab=Resources.Load<FloatingDamageNumber>("Presentation/DamageNumber");
                Require(prefab!=null,"Нет настоящего Presentation/DamageNumber для body origin.");
                number=UnityEngine.Object.Instantiate(prefab);number.Initialize(historical);
                Near(number.transform.position,(Vector3)(expected+Vector2.up*1.7f),"Actual число урона использовало поздний down anchor");
                evidence.damagePostDownBodyX=victim.BodyPosition.x;evidence.damagePostDownBodyY=victim.BodyPosition.y;
                evidence.damageAppliedEvents=applied;evidence.damageSequence=victim.damageSequence;evidence.damageReceipts=victim.resolvedAttacks.Count;
            }
            finally {ActorBodySupport.Unbind(victim);if(number!=null) UnityEngine.Object.DestroyImmediate(number.gameObject);}
        }
        private static void CheckBoundAudio(ActorState source,RoomLayout room)
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var actor=source.Copy();actor.id="bound-audio-consumer";actor.owner="local";actor.pose="slopeWalkUp";
            actor.poseTime=0;actor.vx=.2f;actor.heldItemId=null;
            ActorBodySupport.Bind(actor,room);
            var root=new GameObject("Проверка голоса подтверждённой опоры");SimulationGeometry geometry=null;
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                geometry=new SimulationGeometry(_=>room);geometry.Reconcile(world);
                var camera=root.AddComponent<Camera>();var playback=root.AddComponent<GameplayAudio>();
                playback.Initialize(session,new WeaponReloadController(session),camera);
                var audio=root.AddComponent<MovementAudio>();audio.Initialize(session,geometry,playback);
                var step=typeof(MovementAudio).GetMethod("LateUpdate",hidden);
                typeof(MovementAudio).GetMethod("Reconcile",hidden).Invoke(audio,null);step.Invoke(audio,null);
                actor.poseTime=.18f;step.Invoke(audio,null);
                Require(root.GetComponentsInChildren<AudioSource>().Length==0,"Изменение graphic/body phase без anchor движения создало шаг.");
                actor.Position=ActorTerrainMotion.Move(room,Anatomy.Movement(actor),actor.Position,new Vector2(.002f,-.001f),false);
                actor.poseTime=.24f;var sample=ActorBodySupport.Sample(actor);
                Require(sample.HasContact,"Тест голоса потерял настоящий sampled contact.");step.Invoke(audio,null);
                var voices=(IList)typeof(GameplayAudio).GetField("voices",hidden).GetValue(playback);
                var sources=root.GetComponentsInChildren<AudioSource>();
                Require(voices.Count==1 && sources.Length==1 && audio.LocalContacts==1 && audio.Landings==0,
                    "Bound аудио не создало один действительный шаг или подменило его landing.");
                var position=(Vector2)voices[0].GetType().GetField("position").GetValue(voices[0]);
                Near(position,sample.ContactPoint,"Голос поставлен на устаревший anchor/body вместо фактического контакта");
                var settings=JsonUtility.FromJson<MovementAudio.Settings>(Resources.Load<TextAsset>("Presentation/movement-audio").text);
                var bank=Array.Find(settings.banks,b=>b.id==sample.MovementSound);
                Require(bank!=null && sources[0].clip==Resources.Load<AudioClip>(bank.clips[0]) && sources[0].volume>0,
                    "Actual AudioSource не использует material/clips подтверждённого контакта.");
                step.Invoke(audio,null);Require(voices.Count==1,"Повторный confirmed sample продублировал шаг.");
            }
            finally {ActorBodySupport.Unbind(actor);geometry?.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckClimbExit(ActorState actor,PonyVisual visual)
        {
            actor.heldItemId=null;actor.climbing=true;actor.pose="climb";actor.poseTime=.2f;
            var sample=ActorBodySupport.Sample(actor);
            Require(!sample.HasContact && sample.BodyPosition==actor.Position,"Ladder body fit не выключился.");
            CheckDraw(actor,visual);
        }
        private static void Near(Vector3 actual,Vector3 expected,string reason)
        {Require((actual-expected).sqrMagnitude<.00000004f,$"{reason}: actual={actual}, expected={expected}.");}
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException(reason);}
    }
}
