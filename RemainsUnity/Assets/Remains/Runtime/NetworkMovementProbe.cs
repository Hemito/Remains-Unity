using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class NetworkMovementProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private MovementController movement;
        private RoomLayout hub,factory;
        private float deadline,phaseStarted,spawnY;
        private int phase;
        private int transitionLifecycle;
        private float completedAt;
        private string completedActor,completedWorld;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+30;
            hub=RoomLayout.Load("hub");factory=RoomLayout.Load();
        }

        private void Update()
        {
            if(phase==5)
            {
                if(Time.unscaledTime-completedAt>=1) Finish(true,"Новый персонаж виден в хабе; стояние, переход на завод и спуск по лестнице без проваливания");
                return;
            }
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут проверки появления: этап {phase}");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.World.actors.Count(a=>a.player && a.connected)<2) return;
            if(phase==0)
            {
                if(actor.level!="hub" || !RoomSupport.IsSupported(hub,actor.Position)) {Finish(false,"Новый персонаж не появился на полу хаба");return;}
                movement=new MovementController(hub);spawnY=actor.y;phaseStarted=Time.unscaledTime;phase=1;
            }
            if(phase==1)
            {
                movement.Step(actor,Vector2.zero,false,Time.deltaTime);
                if(Mathf.Abs(actor.y-spawnY)>.001f) {Finish(false,"Проваливание при стоянии в хабе");return;}
                if(Time.unscaledTime-phaseStarted<1) return;
                var visual=FindObjectsByType<PonyVisual>(FindObjectsSortMode.None).FirstOrDefault(v=>v.ActorId==actor.id);
                var viewport=Camera.main.WorldToViewportPoint(actor.Position+Vector2.up*.8f);
                if(visual==null || !visual.gameObject.activeInHierarchy || !visual.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled) ||
                    viewport.x<0 || viewport.x>1 || viewport.y<0 || viewport.y>1 || viewport.z<=0)
                {Finish(false,$"Персонаж отсутствует в кадре: actor={actor.id}, viewport={viewport}");return;}
                transitionLifecycle=actor.lifecycleSequence+1;
                session.ReturnToFactory();phase=2;
                return;
            }
            if(phase==2)
            {
                if(actor.lifecycleSequence<transitionLifecycle) return;
                if(session.World.actors.Any(a=>a.player && a.connected && a.level!="factory:0")) return;
                actor.x=4;actor.y=0;actor.vx=actor.vy=0;actor.revision++;session.PublishActor(actor);
                movement=new MovementController(factory);phaseStarted=Time.unscaledTime;phase=3;
            }
            if(phase==3)
            {
                actor.x=4;
                movement.Step(actor,Vector2.up,false,Time.deltaTime);
                if(Time.unscaledTime-phaseStarted<2) return;
                if(Mathf.Abs(actor.y-3.25f)>.001f) {Finish(false,$"Верх лестницы недостижим: position={actor.Position}, level={actor.level}, lifecycle={actor.lifecycleSequence}");return;}
                phaseStarted=Time.unscaledTime;phase=4;
            }
            if(phase==4)
            {
                actor.x=4;
                movement.Step(actor,Vector2.down,false,Time.deltaTime);
                if(actor.y<0) {Finish(false,"Проваливание ниже пола при спуске");return;}
                if(Time.unscaledTime-phaseStarted<3) return;
                if(actor.y!=0 || actor.vy!=0) {Finish(false,"Персонаж не остановился на нижнем полу");return;}
                phase=5;completedAt=Time.unscaledTime;completedActor=actor.id;completedWorld=session.World.worldId;
            }
        }

        private void Finish(bool success,string detail)
        {
            enabled=false;
            string report=JsonUtility.ToJson(new Report {success=success,detail=detail,actor=completedActor??runtime.Session.LocalActorId,
                world=completedWorld??runtime.Session.World?.worldId,phase=phase},true);
            if(runtime.Options.Report!=null) File.WriteAllText(runtime.Options.Report,report);
            Debug.Log(report);Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report {public bool success;public string detail,actor,world;public int phase;}
    }
}
