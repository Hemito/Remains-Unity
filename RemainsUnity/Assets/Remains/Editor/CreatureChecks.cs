using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The creature pipeline (CreatureAuthoring → CreatureCatalog → FactoryCreatureAdapter): every connected creature
    // has a loadable actor type, anatomy, atlas with the poses the game draws, an initial state matching AllData and a
    // death voice; seed 104 installs exactly the template placements of connected creatures, once; far creatures sleep.
    public static class CreatureChecks
    {
        private static readonly string[] Poses={"idle","walk","run","jump","down"};
        private static readonly string[] FamilyPoses={"idle","walk","run","jump","attack","down"};
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var catalog=CreatureCatalog.Load();
            Require(catalog.Creatures.Count>0,"каталог пуст");
            var exported=ExportedContentCatalog.Load();var voices=ActorDeathAudioCatalog.Load();
            foreach(var creature in catalog.Creatures)
            {
                var type=ActorTypes.Get(creature.actorType);
                Require(type.combatAi && type.naturalAttack!=null && type.presentation=="atlas","тип "+creature.actorType);
                type.naturalAttack.Validate(creature.actorType);type.perception.Validate();
                var art=AtlasAnimationData.Get(type.artwork);
                bool family=NpcCreatureBehaviours.IsFamily(type.behavior);
                foreach(string pose in family?FamilyPoses:Poses) Require(art.Clip(pose).count>0,$"{creature.actorType}: поза {pose}");
                if(family) type.motion.Validate(creature.actorType);
                Require(Anatomy.Movement(new ActorState {anatomy=creature.actorType}).width>0,"анатомия "+creature.actorType);
                var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
                float hp=float.Parse((string)unit.Element("comb").Attribute("hp"),System.Globalization.CultureInfo.InvariantCulture);
                var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=creature.actorType,facing=1},"level");
                Require(Mathf.Approximately(spawned.health,hp) && spawned.anatomy==creature.actorType,$"{creature.actorType}: здоровье {spawned.health} ≠ AllData {hp}");
                Require(voices.TrySelect(creature.actorType,0,out _),"голос смерти "+creature.actorType);
                Animates(creature.actorType,art);
                Require(type.attackSounds is {Length:>0} && type.attackSounds.All(key=>AudioClipSource.Load(key)!=null),"звуки касания "+creature.actorType);
                Require(type.hitMaterial is "flesh" or "metal" or "slime",$"{creature.actorType}: материал попадания {type.hitMaterial}");
            }

            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var room=FactoryContentSources.Require(world,"factory:0");
                var root=room.GetComponent<FactoryLevelRoot>();
                int expected=root.Objects.Count(o=>o.Source.Type.StartsWith("remains:obj:",StringComparison.Ordinal) &&
                    catalog.TryObject(o.Source.Type.Substring("remains:obj:".Length),out _));
                var batch=RoomContentBatch.Read(room,"factory:0");
                Require(batch.npcs!=null && batch.npcs.actors.Length==expected && expected>0,$"на заводе {batch.npcs?.actors.Length} существ из {expected}");
                Require(RoomContentInstaller.Apply(world,batch),"первая установка");
                int installed=world.actors.Count(a=>catalog.Creatures.Any(c=>c.actorType==a.actorType));
                Require(installed==expected && !RoomContentInstaller.Apply(world,batch),"повторная установка");
                // A world that installed an earlier placement version gets only what it lacks.
                bool V3(ActorState a)=>a.actorType=="bloodwing" || a.actorType=="slime" || a.actorType.StartsWith("bloat",StringComparison.Ordinal);
                bool V2(ActorState a)=>a.actorType.StartsWith("scorp",StringComparison.Ordinal);
                void Upgrade(string patch,Func<ActorState,bool> missing,string what)
                {
                    var older=StateJson.Copy(world);
                    int lacking=older.actors.RemoveAll(a=>missing(a));
                    older.contentPatches.Remove("factory:0:"+FactoryCreatureAdapter.Patch);older.contentPatches.Add("factory:0:"+patch);
                    int kept=older.actors.Count;
                    Require(lacking>0 && RoomContentInstaller.Apply(older,batch) && older.actors.Count==kept+lacking && older.actors.Count(a=>missing(a))==lacking,what);
                }
                Upgrade("base:factory-generated:creatures-v2",V3,"переход v2 → v3 добавляет только летунов и слизь");
                Upgrade("base:factory-generated:creatures-v1",a=>V2(a) || V3(a),"переход v1 → v3 добавляет скорпионов, летунов и слизь");
                Behaviours(world,room.CreateLayout(false));
                string saved=StateJson.ToJson(world);
                Require(StateJson.ToJson(StateJson.FromJson<WorldState>(saved))==saved,"сохранение мира с существами");
                var counts=string.Join(", ",world.actors.Where(a=>!a.player).GroupBy(a=>a.actorType).Select(g=>$"{g.Key} {g.Count()}"));

                var creature=world.actors.First(a=>catalog.Creatures.Any(c=>c.actorType==a.actorType));
                var player=new ActorState {id="player",player=true,connected=true,level=creature.level,x=creature.x+NpcActivity.HalfWidth+5,y=creature.y};
                world.actors.Add(player);
                Require(!NpcActivity.Awake(world,creature),"дальнее существо не спит");
                player.x=creature.x+5;Require(NpcActivity.Awake(world,creature),"ближнее существо спит");
                Debug.Log($"PASS существа: подключено {catalog.Creatures.Count} ({string.Join(", ",catalog.Creatures.Select(c=>c.actorType))}), пробелов {catalog.Gaps.Count}; завод seed104: {counts}; сон вдали.");
            }
            finally {FactoryContentSources.Clear();}
        }
        // A few seconds of each family on the real factory geometry with a target nearby: no NaN, never inside walls,
        // flyers move and change pose, a crawling slime stays on its floor.
        private static void Behaviours(WorldState world,RoomLayout room)
        {
            var controller=new Remains.Gameplay.NpcCreatureController();
            foreach(string kind in new[]{NpcCreatureBehaviours.Bat,NpcCreatureBehaviours.Bloat,NpcCreatureBehaviours.Slime})
            {
                var actor=StateJson.Copy(world.actors.First(a=>ActorTypes.Get(a.actorType).behavior==kind));
                var type=ActorTypes.Get(actor.actorType);var hull=Anatomy.Movement(actor);
                var settings=MovementSettings.Load();settings.walkSpeed=type.naturalAttack.approachSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);
                var ground=new Remains.Gameplay.MovementController(room,settings);
                var start=actor.Position;var poses=new System.Collections.Generic.HashSet<string>();
                Vector2 target=start+new Vector2(4,-1);
                bool crawling=false;float lowest=float.MaxValue;
                for(int i=0;i<600;i++)
                {
                    controller.Step(actor,type,null,target,room,ground,1f/60);
                    Require(float.IsFinite(actor.x) && float.IsFinite(actor.y),$"{kind}: нечисловое положение");
                    if(NpcCreatureBehaviours.Flies(kind)) Require(ActorTerrainMotion.IsClear(room,hull,actor.Position),$"{kind}: внутри стены в {actor.Position}");
                    poses.Add(actor.pose);
                    if(kind==NpcCreatureBehaviours.Slime && actor.creature.mode=="crawl") {crawling=true;lowest=Math.Min(lowest,actor.y);}
                }
                if(NpcCreatureBehaviours.Flies(kind)) Require(Vector2.Distance(start,actor.Position)>.5f,$"{kind}: не сдвинулся за 10 с ({start} → {actor.Position})");
                if(kind==NpcCreatureBehaviours.Bat) Require(poses.Contains("walk") && poses.Contains("idle"),$"летучая мышь: позы {string.Join(",",poses)}");
                if(crawling) Require(start.y-lowest<.5f,$"слизь упала с пола: {start.y} → {lowest}");
                Debug.Log($"Поведение {kind} ({actor.actorType}): {start} → {actor.Position}, позы {string.Join(",",poses)}, режим {actor.creature.mode}.");
            }
        }
        // The generated prefab draws different frames as the walk pose time advances.
        private static void Animates(string actorType,AtlasActorArtwork art)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<Remains.Presentation.AtlasActorVisual>("Assets/Remains/Prefabs/Actors/Creatures/"+actorType+".prefab");
            Require(prefab!=null,"нет префаба "+actorType);
            var visual=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var state=new ActorState {id="check",actorType=actorType,anatomy=actorType,pose="walk",facing=1};
                visual.Bind(state);var renderer=visual.GetComponent<SpriteRenderer>();
                var seen=new System.Collections.Generic.HashSet<Sprite>();
                for(int i=0;i<30;i++) {state.poseTime=i/30f;visual.Draw();seen.Add(renderer.sprite);}
                // A snapshot rewinding the same pose does not restart the drawn animation.
                if(art.Clip("walk").count>2)
                {
                    state.poseTime=.5f;visual.Draw();var before=renderer.sprite;
                    state.poseTime=0;visual.Draw();
                    Require(renderer.sprite==before,$"{actorType}: откат времени позы перезапустил анимацию");
                }
                int expected=Math.Min(art.Clip("walk").count,2);
                Require(renderer.enabled && seen.Count>=expected,$"{actorType}: за секунду ходьбы кадров {seen.Count}, в клипе {art.Clip("walk").count}");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Существа: "+message);}
    }
}
