using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // A new OS process loads a copied, complete ordinary save. This probe never
    // creates a world, relocates actors, disables enemies or edits saved content.
    [DefaultExecutionOrder(10000)]
    public sealed class WarehouseSaveReloadProbe : MonoBehaviour
    {
        private const string Level="factory:0",Prefix=Level+":original:plant:40:";
        [Serializable] private sealed class Expected {public int schema;public string world,snapshot,character;}
        [Serializable] private sealed class Report
        {
            public bool success,selectedSnapshotVerified,sourceContentVerified,loadedContentVerified,bodyVerified;
            public string detail,world,snapshot,actor,owner,level;
            public int generation,props,containers,doors,npcs;
            public float sourceHealth,loadedHealth,finalHealth;
            public Vector2 sourcePosition,loadedPosition,finalPosition;
        }
        private PrototypeRuntime runtime;private Expected witness;private WorldState source;private ActorState expected;
        private Report report;private bool finished,captured;private float deadline,entered=-1;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;
            if(value.Options.Role!="host" || value.Options.ProbePeers!=1 || string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Загрузка склада требует копию сохранения и один скрытый хост.");
            string path=Path.Combine(value.Session.Saves.WorldDirectoryPath,"warehouse-reload-expected.json");
            witness=StateJson.FromJson<Expected>(File.ReadAllText(path));
            var selected=value.Session.Saves.SelectedSnapshot();source=value.Session.Saves.LoadWorld(false);
            if(witness?.schema!=1 || selected==null || source==null || witness.character!=value.Options.Character ||
                selected.Id!=witness.snapshot || selected.WorldId!=witness.world || source.worldId!=witness.world)
                throw new InvalidOperationException("Загрузка склада выбрала другой исходный снимок или профиль; новый мир не создаётся.");
            expected=source.actors.Single(a=>a.player && a.character==witness.character);
            if(expected.level!=Level || expected.down || expected.health<=0 || !Counts(source) ||
                !RoomContentProbePersistence.UnchangedOnReinstall(source,Level))
                throw new InvalidOperationException("Исходная копия не содержит живого игрока в рейде или полный сохранённый склад.");
            report=new Report {world=witness.world,snapshot=witness.snapshot,actor=expected.id,
                selectedSnapshotVerified=true,sourceContentVerified=true,sourceHealth=expected.health,sourcePosition=expected.Position};
            value.CommandSource=()=>default;
            value.Session.ServerReady+=()=>
            {
                try
                {
                    var loaded=value.Session.ServerWorld;
                    File.WriteAllText(value.Options.Report+".loaded-start-world.json",StateJson.ToJson(loaded,true));
                    if(!Counts(loaded) || !Preserved(source,loaded))
                        throw new InvalidOperationException("Подготовка мира заменила сохранённое содержимое склада.");
                    report.loadedContentVerified=true;
                }
                catch(Exception error) {Finish(false,error.Message);}
            };
        }
        private static bool Counts(WorldState world)=>world.props.Count(p=>p.level==Level)==29 &&
            world.containers.Count(c=>c.level==Level)==52 && world.doors.Count(d=>d.level==Level)==17 &&
            world.actors.Count(a=>!a.player && a.level==Level)==22 &&
            world.props.Count(p=>p.id.StartsWith(Prefix,StringComparison.Ordinal))==17 &&
            world.containers.Count(c=>c.id.StartsWith(Prefix,StringComparison.Ordinal))==16 &&
            world.doors.Count(d=>d.id.StartsWith(Prefix,StringComparison.Ordinal))==2 &&
            world.actors.Count(a=>a.id.StartsWith(Prefix,StringComparison.Ordinal))==9;
        private static bool Preserved(WorldState before,WorldState after)
        {
            foreach(var container in before.containers.Where(c=>c.id.StartsWith(Prefix,StringComparison.Ordinal)))
                if(!PreservedProp(container,after.containers.Single(c=>c.id==container.id))) return false;
            foreach(var prop in before.props.Where(p=>p.id.StartsWith(Prefix,StringComparison.Ordinal)))
                if(!PreservedProp(prop,after.props.Single(p=>p.id==prop.id))) return false;
            foreach(var door in before.doors.Where(d=>d.id.StartsWith(Prefix,StringComparison.Ordinal)))
                if(StateJson.ToJson(door)!=StateJson.ToJson(after.doors.Single(d=>d.id==door.id))) return false;
            foreach(var npc in before.actors.Where(a=>a.id.StartsWith(Prefix,StringComparison.Ordinal)))
            {
                var loaded=after.Find(npc.id);
                if(loaded==null || loaded.actorType!=npc.actorType || loaded.anatomy!=npc.anatomy || loaded.health!=npc.health ||
                    loaded.mana!=npc.mana || loaded.lifecycleSequence!=npc.lifecycleSequence ||
                    StateJson.ToJson(loaded.inventory)!=StateJson.ToJson(npc.inventory) ||
                    StateJson.ToJson(loaded.injuries)!=StateJson.ToJson(npc.injuries)) return false;
            }
            return true;
        }
        private static bool PreservedProp<T>(T before,T after) where T:PropState
        {
            // Connection IDs are session-local. Verify the explicit startup reset,
            // then compare every durable field, including extensions and inventory.
            if(after.owner!=null || after.held || after.generation!=before.generation+1) return false;
            var recovered=StateJson.FromJson<T>(StateJson.ToJson(before));
            recovered.owner=null;recovered.held=false;recovered.generation=before.generation+1;
            return StateJson.ToJson(recovered)==StateJson.ToJson(after);
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут загрузки сохранённого склада.");
                var actor=runtime.Session.LocalActor;if(actor==null || !report.loadedContentVerified) return;
                if(entered<0)
                {
                    if(actor.id!=expected.id || actor.character!=expected.character || actor.level!=Level || actor.down ||
                        actor.health!=expected.health || Vector2.Distance(actor.Position,expected.Position)>.001f ||
                        StateJson.ToJson(actor.inventory)!=StateJson.ToJson(expected.inventory))
                        throw new InvalidOperationException($"Вход изменил живое тело: actor={actor.id}, level={actor.level}, HP={actor.health}/{expected.health}, position={actor.Position}/{expected.Position}.");
                    report.bodyVerified=true;report.owner=actor.owner;report.generation=actor.generation;
                    report.loadedHealth=actor.health;report.loadedPosition=actor.Position;entered=Time.unscaledTime;
                }
                if(actor.id!=report.actor || actor.owner!=report.owner || actor.generation!=report.generation ||
                    actor.level!=Level || actor.down || actor.health<=0)
                    throw new InvalidOperationException("После загрузки живого игрока сменили, лишили управления или отправили в хаб.");
                // Beyond the party-defeat cadence. Genuine enemy simulation remains active.
                if(Time.unscaledTime-entered>=5 && captured) Finish(true,"Новый процесс загрузил исходный рейд, сохранил живое тело и содержимое склада; NPC не отключались, возврата в хаб нет.");
            }
            catch(Exception error) {Finish(false,error.Message);}
        }
        private void LateUpdate()
        {
            if(finished || captured || entered<0 || Time.unscaledTime-entered<3) return;
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".warehouse-reloaded.png",runtime.Hud.GetComponent<Canvas>());captured=true;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;report??=new Report();report.success=success;report.detail=detail;
            var actor=runtime.Session.LocalActor;var world=runtime.Session.World;
            report.level=actor?.level;report.finalHealth=actor?.health??0;report.finalPosition=actor?.Position??Vector2.zero;
            report.props=world?.props.Count(p=>p.level==Level)??0;report.containers=world?.containers.Count(c=>c.level==Level)??0;
            report.doors=world?.doors.Count(d=>d.level==Level)??0;report.npcs=world?.actors.Count(a=>!a.player && a.level==Level)??0;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(report,true));Debug.Log("Склад reload: "+success+", "+detail);
            StartCoroutine(Quit(success));
        }
        private IEnumerator Quit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
    }
}
