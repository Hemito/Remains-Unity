using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Observation only. The existing shot-spread probe owns all commands/barriers/exit.
    [DefaultExecutionOrder(20000)]
    public sealed class WeaponShotEffectsFrameProbe : MonoBehaviour
    {
        [Serializable] public sealed class Epoch
        {
            public string id,owner,level,item,definition,pose;
            public int generation,lifecycle;
            public long selection,pauseRevision;
            public uint motion;
            public float x,y,vx,vy,health,mana;
            public bool local,climbing,crouched;
            public WeaponRecoilPose recoil;
        }
        [Serializable] public sealed class Rectangle
        {public float x,y,width,height;public Rectangle(Bounds bounds){x=bounds.min.x;y=bounds.min.y;width=bounds.size.x;height=bounds.size.y;}}
        [Serializable] public sealed class Draw
        {
            public int frame;
            public double time;
            public Epoch actor;
            public string viewId,sprite;
            public bool visible,casingObserved,casingVisible,reloadVisible,ownReloadActive,paused;
            public float rootX,rootY,angle,muzzleX,muzzleY,expectedMuzzleX,expectedMuzzleY,muzzleError,
                neutralRootX,neutralRootY,neutralMuzzleX,neutralMuzzleY,renderOffset,renderAngle,
                casingSocketX,casingSocketY,casingX,casingY,casingAge,casingSocketError;
            public Rectangle weaponBounds,casingBounds;
        }
        [Serializable] public sealed class Shot
        {
            public AttackEvent attack;
            public int frame,index,weaponIndex,casingCreatedBefore,casingCreatedAfter,casingCreatedDelta;
            public double time;
            public Epoch actor;
            public float historicMuzzleX,historicMuzzleY,historicMuzzleError,spreadDegrees,range,expectedRange,rangeTolerance,rangeError,
                preImpulseOffsetX,preImpulseAngle;
            public bool historicValid,preImpulseObserved;
            public Draw draw;
        }
        [Serializable] public sealed class Frame
        {public string label,shot,png,scene;public int width=1920,height=1080;public Draw draw;}
        [Serializable] public sealed class Report
        {
            public int schema=1,participants,frameRate,initialCasingCreated,finalCasingCreated,preparedCasings;
            public string smoke,role,world,localActor,localOwner,source,sourceOwner,saveDirectory,reportPath,returnKind;
            public bool success,complete,observationOnly=true,secondPistolPreImpulse,nonzeroPistol,nonzeroEnergy,neutralReturn;
            public string limitation="Наблюдатель не меняет inputs/актёров/инвентарь. Обычная серия после 12 выстрелов начинает reload: нейтральный конец при reload не доказывает свободное затухание всей кривой. PNG/readback могут увеличить длительность кадра. Original runtime не проверен.";
            public List<Shot> shots=new();public List<Frame> frames=new();public List<string> errors=new();
        }
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private readonly Report report=new();
        private readonly Queue<Shot> pending=new();
        private readonly HashSet<string> observed=new(StringComparer.Ordinal);
        private readonly Dictionary<string,WeaponRig> rigs=new(StringComparer.Ordinal);
        private readonly Dictionary<string,WeaponDefinition> definitions=new(StringComparer.Ordinal);
        private CasingEffectProfile casingProfile;
        private Shot latestPistol,latestEnergy;
        private string directory;
        private int previousCreated,pistolCount,energyCount;
        private bool failed,initialized;

        public void Initialize(PrototypeRuntime value,InteractionVisuals presentation)
        {
            Require(!initialized && value?.Options.Smoke?.StartsWith("shot-spread-frames-",StringComparison.Ordinal)==true &&
                !string.IsNullOrEmpty(value.Options.SaveDirectory) && !string.IsNullOrEmpty(value.Options.Report),
                "Наблюдателю эффектов нужны shot-spread-frames и отдельный каталог сохранения/отчёт.");
            runtime=value;visuals=presentation;
            Require(visuals?.Casing!=null && runtime.Hud!=null,"Нет actual WeaponView/CasingEffects/HUD для наблюдения выстрелов.");
            foreach(string weapon in new[] {"pistol","energy"})
            {rigs.Add(weapon,WeaponRigs.Get(weapon));definitions.Add(weapon,WeaponCatalog.Get(weapon));}
            Require(CasingEffectCatalog.TryGet("pistol",out casingProfile) && !CasingEffectCatalog.TryGet("energy",out _),
                "Серия требует явный pistol casing profile и отсутствие energy casing profile.");
            directory=Path.Combine(Path.GetFullPath(value.Options.SaveDirectory),"WeaponShotEffects",value.Options.Role);
            Directory.CreateDirectory(directory);
            report.smoke=value.Options.Smoke;report.role=value.Options.Role;report.participants=value.Options.ProbePeers;
            report.frameRate=value.Options.ProbeFrameRate;report.saveDirectory=Path.GetFullPath(value.Options.SaveDirectory);
            report.reportPath=Path.Combine(directory,"report.json");report.initialCasingCreated=previousCreated=visuals.Casing.CreatedCount;
            report.preparedCasings=visuals.Casing.PreparedCount;
            initialized=true;value.Session.AttackReceived+=Observe;
        }
        private Epoch CaptureEpoch(ActorState actor)=>new()
        {
            id=actor.id,owner=actor.owner,level=actor.level,item=actor.equippedWeaponItem,definition=actor.weapon,
            generation=actor.generation,lifecycle=actor.lifecycleSequence,selection=actor.weaponSelectionRevision,
            motion=actor.motionSequence,pose=actor.pose,x=actor.x,y=actor.y,vx=actor.vx,vy=actor.vy,health=actor.health,mana=actor.mana,
            climbing=actor.climbing,crouched=actor.crouched,local=actor.id==runtime.Session.LocalActorId,
            pauseRevision=runtime.Session.Pause.Revision,recoil=actor.weaponRecoil?.Copy()
        };
        private void Observe(AttackEvent attack)
        {
            if(failed || !initialized || attack?.weapon is not ("pistol" or "energy")) return;
            var actor=runtime.Session.World?.Find(attack.attacker);
            if(actor?.player!=true) return;
            Guard(()=>
            {
                Require(observed.Add(attack.id),"Повтор actual AttackReceived: "+attack.id);
                if(report.source==null)
                {
                    report.source=attack.attacker;report.sourceOwner=attack.sourceOwner;report.world=runtime.Session.World.worldId;
                    report.localActor=runtime.Session.LocalActorId;report.localOwner=runtime.Session.LocalOwner;
                }
                Require(attack.attacker==report.source && attack.sourceOwner==report.sourceOwner && report.world==runtime.Session.World.worldId,
                    "Серия эффектов получила другого источника/мир: "+attack.id);
                Require(report.shots.Count<24 && actor.level==attack.level,"Лишний запуск/другая локация: "+attack.id);
                var immutable=StateJson.Copy(attack);var captured=immutable.rangedPose;
                Require(captured!=null,"Нет исторической позы actual выстрела: "+attack.id);
                captured.Validate("наблюдение actual выстрела "+attack.id);
                var historic=new RangedWeaponPose(new Vector2(captured.rootX,captured.rootY),captured.angleDegrees,captured.mirrored,rigs[attack.weapon]);
                var ray=(immutable.End-immutable.Origin).normalized;var definition=definitions[attack.weapon];
                float flightRange=definition.FlightRange(immutable.speed);
                int created=visuals.Casing.CreatedCount;
                var shot=new Shot
                {
                    attack=immutable,frame=Time.frameCount,time=Time.unscaledTimeAsDouble,index=report.shots.Count,
                    weaponIndex=attack.weapon=="pistol"?++pistolCount:++energyCount,actor=CaptureEpoch(actor),
                    casingCreatedBefore=previousCreated,casingCreatedAfter=created,casingCreatedDelta=created-previousCreated,
                    historicMuzzleX=historic.Muzzle.x,historicMuzzleY=historic.Muzzle.y,historicMuzzleError=Vector2.Distance(historic.Muzzle,immutable.Origin),
                    spreadDegrees=Mathf.Atan2(historic.Direction.x*ray.y-historic.Direction.y*ray.x,Vector2.Dot(historic.Direction,ray))*Mathf.Rad2Deg,
                    range=Vector2.Distance(immutable.Origin,immutable.End),expectedRange=flightRange,rangeTolerance=Mathf.Max(.001f,flightRange*.00001f),
                    rangeError=Mathf.Abs(Vector2.Distance(immutable.Origin,immutable.End)-flightRange)
                };
                shot.historicValid=captured.worldId==report.world && shot.historicMuzzleError<=.0001f &&
                    Mathf.Abs(shot.spreadDegrees)<=definition.shotSpreadHalfAngleDegrees+.0001f && shot.rangeError<=shot.rangeTolerance;
                Require(shot.historicValid,"Captured ray/профиль разброса расходится с actual запуском: "+attack.id);
                Require(shot.casingCreatedDelta==(attack.weapon=="pistol"?1:0) && captured.hasCasing==(attack.weapon=="pistol"),
                    "Actual casing count не соответствует pistol1/energy0: "+attack.id);
                previousCreated=created;
                if(shot.actor.local)
                {
                    Require(EpochMatches(actor,captured,attack.weapon),"Local actual launch изменил epoch до наблюдения: "+attack.id);
                    var neutralActor=actor.Copy();neutralActor.weaponRecoil=WeaponRecoilPose.Neutral(runtime.Session.Pause.Revision);
                    var neutral=WeaponPose.SampleRanged(neutralActor);
                    shot.preImpulseObserved=true;shot.preImpulseOffsetX=captured.rootX-neutral.Root.x;
                    shot.preImpulseAngle=Mathf.DeltaAngle(neutral.Angle,captured.angleDegrees);
                    if(attack.weapon=="pistol" && shot.weaponIndex==2)
                        report.secondPistolPreImpulse=Mathf.Abs(shot.preImpulseOffsetX)>.00001f || Mathf.Abs(shot.preImpulseAngle)>.001f;
                }
                report.shots.Add(shot);pending.Enqueue(shot);
                if(attack.weapon=="pistol")latestPistol=shot;else latestEnergy=shot;
            });
        }
        private static bool EpochMatches(ActorState actor,RangedShotPose pose,string weapon)=>actor!=null && actor.weapon==weapon &&
            actor.equippedWeaponItem==pose.weaponItem && actor.weaponSelectionRevision==pose.selectionRevision &&
            actor.generation==pose.actorGeneration && actor.lifecycleSequence==pose.actorLifecycle;
        private void LateUpdate()
        {
            if(!initialized || failed || report.complete) return;
            Guard(()=>
            {
                // Only queued events and three explicit milestones sample/clone state.
                while(pending.Count>0 && pending.Peek().frame<Time.frameCount)
                {
                    var shot=pending.Dequeue();shot.draw=CaptureDraw(shot);
                    Require(shot.draw.visible && shot.draw.muzzleError<=.001f,"Actual drawn muzzle не совпал с текущей позой: "+shot.attack.id);
                    Require(shot.draw.casingObserved==(shot.attack.weapon=="pistol") &&
                        (shot.attack.weapon!="pistol" || shot.draw.casingVisible && shot.draw.casingSocketError<=.00001f),
                        "Нет actual casing/исторического socket либо energy породил гильзу: "+shot.attack.id);
                }
                ObserveMilestone(latestPistol,"pistol-recoil",pistolCount>=2,ref report.nonzeroPistol);
                ObserveMilestone(latestEnergy,"energy-recoil",energyCount>=1,ref report.nonzeroEnergy);
                if(report.shots.Count==24 && pending.Count==0 && !report.neutralReturn && latestEnergy!=null)
                {
                    var actor=runtime.Session.World?.Find(report.source);var sample=actor?.weaponRecoil;
                    if(sample!=null && sample.offsetX==0 && sample.angleDegrees==0 && EpochMatches(actor,latestEnergy.attack.rangedPose,"energy"))
                    {
                        var draw=CaptureDraw(latestEnergy);
                        Require(draw.visible && draw.muzzleError<=.001f && draw.renderOffset<=.001f && draw.renderAngle<=.001f,
                            "Нейтральная выборка не вернула фактически нарисованное оружие.");
                        report.returnKind=draw.reloadVisible || draw.ownReloadActive?"neutral-during-ordinary-reload":"observed-neutral-sample";
                        CaptureFrame("neutral-return",latestEnergy,draw);report.neutralReturn=true;
                    }
                }
                if(report.shots.Count==24 && pending.Count==0 && report.nonzeroPistol && report.nonzeroEnergy && report.neutralReturn)
                {
                    Require(pistolCount==12 && energyCount==12 && (!report.shots.Any(s=>s.actor.local) || report.secondPistolPreImpulse),
                        "Нет 12+12 shots или второй local pistol не захватил предыдущую отдачу.");
                    report.complete=report.success=true;SaveReport();
                }
            });
        }
        private void ObserveMilestone(Shot shot,string label,bool eligible,ref bool captured)
        {
            if(captured || !eligible || shot==null || shot.frame>=Time.frameCount) return;
            var actor=runtime.Session.World?.Find(shot.attack.attacker);var sample=actor?.weaponRecoil;
            if(!EpochMatches(actor,shot.attack.rangedPose,shot.attack.weapon) || sample==null ||
                sample.offsetX==0 && sample.angleDegrees==0) return;
            var draw=CaptureDraw(shot);
            if(draw.renderOffset<=.00001f && draw.renderAngle<=.001f) return;
            Require(draw.visible && draw.muzzleError<=.001f,"Nonzero sample не совпал с actual WeaponView: "+shot.attack.id);
            CaptureFrame(label,shot,draw);captured=true;SaveReport();
        }
        private Draw CaptureDraw(Shot shot)
        {
            var actor=runtime.Session.World?.Find(shot.attack.attacker);var view=visuals.FindWeapon(shot.attack.attacker);
            Require(actor!=null && view?.Muzzle!=null && view.Artwork?.sprite!=null,"Нет actual drawn weapon/actor: "+shot.attack.id);
            var expected=WeaponPose.SampleRanged(actor);var neutralActor=actor.Copy();neutralActor.weaponRecoil=WeaponRecoilPose.Neutral(runtime.Session.Pause.Revision);
            var neutral=WeaponPose.SampleRanged(neutralActor);Vector2 root=view.transform.position,muzzle=view.Muzzle.position;
            var draw=new Draw
            {
                frame=Time.frameCount,time=Time.unscaledTimeAsDouble,actor=CaptureEpoch(actor),viewId=view.Id,sprite=view.Artwork.sprite.name,
                visible=view.gameObject.activeInHierarchy && view.Artwork.enabled && !view.Artwork.forceRenderingOff,
                rootX=root.x,rootY=root.y,angle=view.transform.eulerAngles.z,muzzleX=muzzle.x,muzzleY=muzzle.y,
                expectedMuzzleX=expected.Muzzle.x,expectedMuzzleY=expected.Muzzle.y,muzzleError=Vector2.Distance(muzzle,expected.Muzzle),
                neutralRootX=neutral.Root.x,neutralRootY=neutral.Root.y,neutralMuzzleX=neutral.Muzzle.x,neutralMuzzleY=neutral.Muzzle.y,
                renderOffset=Vector2.Distance(root,neutral.Root),renderAngle=Mathf.Abs(Mathf.DeltaAngle(neutral.Angle,view.transform.eulerAngles.z)),
                weaponBounds=new Rectangle(view.Artwork.bounds),ownReloadActive=actor.id==runtime.Session.LocalActorId && runtime.Combat.Reload.Active,
                reloadVisible=visuals.ReloadAnimation.TryGet(actor,out _),paused=runtime.Session.Pause.IsPaused(actor.level)
            };
            if(visuals.Casing.TryGet(shot.attack.id,out var casing))
            {
                draw.casingObserved=true;draw.casingVisible=casing.Renderer!=null && casing.Renderer.enabled && casing.Renderer.gameObject.activeInHierarchy &&
                    !casing.Renderer.forceRenderingOff;draw.casingSocketX=casing.Socket.x;draw.casingSocketY=casing.Socket.y;
                draw.casingX=casing.Position.x;draw.casingY=casing.Position.y;draw.casingAge=casing.Age;
                draw.casingSocketError=Vector2.Distance(casing.Socket,new Vector2(shot.attack.rangedPose.casingX,shot.attack.rangedPose.casingY));
                draw.casingBounds=new Rectangle(casing.Renderer.bounds);
                Require(casing.ShotId==shot.attack.id && casing.WorldId==report.world && casing.Attacker==shot.attack.attacker &&
                    casing.SourceOwner==shot.attack.sourceOwner && casing.Level==shot.attack.level,"Гильза потеряла captured identity: "+shot.attack.id);
                var pose=shot.attack.rangedPose;var historic=new RangedWeaponPose(new Vector2(pose.rootX,pose.rootY),pose.angleDegrees,pose.mirrored,rigs[shot.attack.weapon]);
                Require(Vector2.Distance(historic.Socket(casingProfile.Socket),casing.Socket)<=.00001f,"Гильза создана не из исторического authored socket.");
            }
            return draw;
        }
        private void CaptureFrame(string label,Shot shot,Draw draw)
        {
            Require(runtime.WorldCamera!=null && runtime.LevelPresentationReady,"Actual room/camera не готова для эффекта.");
            string png=Path.Combine(directory,label+".png"),scene=Path.Combine(directory,label+".scene.json");
            var camera=runtime.WorldCamera;
            var record=new NetworkSceneRecord {phase=report.frames.Count,width=1920,height=1080,observer=runtime.Session.LocalActorId,
                level=runtime.Session.LocalActor.level,world=runtime.Session.World.Copy(),cameraPosition=camera.transform.position,
                cameraSize=camera.orthographicSize,background=camera.backgroundColor};
            // Prepared inactive casing slots intentionally have no sprite yet. Record
            // only real visible renderers, without treating those slots as fallback art.
            var renderers=FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                .Where(renderer=>renderer.enabled && !renderer.forceRenderingOff).ToArray();
            record.sprites=renderers.Select(renderer=>
            {
                Require(renderer.sprite!=null,"Активный renderer без Sprite: "+renderer.name);
                var sprite=renderer.sprite;var group=renderer.GetComponentInParent<ActorVisualGroup>();
                return new NetworkSpriteRecord {name=renderer.name,spriteName=sprite.name,textureName=sprite.texture.name,position=renderer.transform.position,
                    scale=renderer.transform.lossyScale,euler=renderer.transform.eulerAngles,rectX=sprite.rect.x,rectY=sprite.rect.y,rectWidth=sprite.rect.width,
                    rectHeight=sprite.rect.height,pivot=new Vector2(sprite.pivot.x/sprite.rect.width,sprite.pivot.y/sprite.rect.height),pixelsPerUnit=sprite.pixelsPerUnit,
                    order=renderer.sortingOrder,groupId=group?.ActorId,groupOrder=group?.Order??0,color=renderer.color,flipX=renderer.flipX,flipY=renderer.flipY,enabled=true};
            }).ToArray();
            record.hudText=runtime.Hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(text=>text.text).ToArray();
            File.WriteAllText(scene,StateJson.ToJson(record,true),new UTF8Encoding(false));
            NetworkFrameCapture.Save(camera,png,runtime.Hud.GetComponent<Canvas>(),1920,1080);
            report.frames.Add(new Frame {label=label,shot=shot.attack.id,png=png,scene=scene,draw=draw});
        }
        private void Guard(Action operation)
        {try {operation();} catch(Exception error) {failed=true;report.success=false;report.errors.Add(error.ToString());Debug.LogError("WeaponShotEffectsFrameProbe FAIL: "+error);SaveReport();}}
        private void SaveReport()
        {
            if(!initialized) return;
            report.finalCasingCreated=visuals.Casing.CreatedCount;
            File.WriteAllText(report.reportPath,StateJson.ToJson(report,true),new UTF8Encoding(false));
        }
        private void OnApplicationQuit()
        {
            if(initialized && !report.complete && !failed)
            {report.errors.Add("Обычный сценарий завершился раньше всех actual draw/casing/recoil milestones.");report.success=false;}
            if(initialized) SaveReport();
        }
        private void OnDestroy() {if(runtime?.Session!=null)runtime.Session.AttackReceived-=Observe;}
        private static void Require(bool condition,string reason) {if(!condition)throw new InvalidOperationException(reason);}
    }
}
