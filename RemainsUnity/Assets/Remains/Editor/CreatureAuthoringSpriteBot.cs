using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Spritebots (UnitSpriteBot) in the creature pass: a flying robot with its own gun. Its picture is the built vector
    // sheet (Tools/build_creature_sheets.py, creature-sheets-v2): the inner clip's frame 1 (calm, green eye) and frame 2
    // (hostile after zlo, red eye) as rows state1 and state2. The gun is new Weapon(this, "robolaser") (FirearmAuthoring
    // CreatureGuns), held at setWeaponPos (X, Y − 1: the unit's registration) and turned to the aim. Numbers are AllData
    // (unit spritebot: phis, move speed, comb, vision, acttrap); runSpeed = 1.5 × speed (UnitSpriteBot constructor).
    public static partial class CreatureAuthoring
    {
        private const string SpriteBotClass="UnitSpriteBot",SpriteBotGun="weapon:robolaser:grade:0";

        // Only the spritebots over the existing catalog, as RunRaiders.
        //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunSpriteBotsBatch
        public static void RunSpriteBotsBatch()
        {
            try {RunSpriteBots();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunSpriteBots()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var rows=((JArray)catalog["definitions"]).OfType<JObject>().ToArray();
            var previous=AssetDatabase.LoadAssetAtPath<CreatureCatalog>(CatalogPath)??throw new InvalidOperationException("Нет каталога существ: сначала полная генерация.");
            var owned=new HashSet<string>(previous.Creatures.Select(c=>c.actorType),StringComparer.Ordinal);
            var template=CreatureSources.FactoryObjects(catalog).Single(o=>o.className==SpriteBotClass && o.problem==null);
            var made=new List<CreatureEntry>();var types=new List<string>();
            string reason=SpriteBots(template,rows,owned,made,types);
            if(reason!=null) throw new InvalidOperationException("Спрайтбот не подключён: "+reason);
            var entries=previous.Creatures.Where(c=>c.family!=NpcCreatureBehaviours.SpriteBot).Concat(made).ToArray();
            var rules=previous.Objects.Where(r=>r.objectId!=template.objectId).Append(new CreatureObjectRule {objectId=template.objectId,actorTypes=types.ToArray()}).ToArray();
            var gaps=previous.Gaps.Where(g=>g.sourceId!=template.objectId).ToArray();
            var generated=made.Select(e=>e.actorType).ToHashSet(StringComparer.Ordinal);
            WriteAnatomy(generated,owned,generated);
            WriteDeathAudio(made,owned,partial:true);
            previous.Configure(entries,rules,gaps);EditorUtility.SetDirty(previous);AssetDatabase.SaveAssets();
            foreach(var entry in made) Debug.Log($"Спрайтбот {entry.sourceId} → {entry.actorType}: {string.Join("; ",entry.notes)}.");
        }

        private static string SpriteBots(CreatureSources.TemplateObject template,JObject[] rows,HashSet<string> owned,List<CreatureEntry> entries,List<string> types)
        {
            foreach(string unitId in template.units)
            {
                var made=entries.FirstOrDefault(e=>e.sourceId==unitId);
                if(made!=null) {types.Add(made.actorType);continue;}
                var row=rows.FirstOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==unitId);
                if(row==null) return "нет определения unit "+unitId+" в выгрузке";
                var sheet=VectorSheet(unitId,out string folder);
                if(sheet==null) return "векторный вид не собран в лист (Tools/build_creature_sheets.py)";
                var labels=((JArray)sheet["rows"]).OfType<JObject>().Select(r=>(string)r["label"]).ToArray();
                if(!labels.SequenceEqual(new[]{"state1","state2"})) return "в листе нет рядов state1 (спокоен) и state2 (враждебен): "+string.Join(", ",labels);
                if(!AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset).AllAssets().Any(a=>a.Id==SpriteBotGun))
                    return $"нет оружия {SpriteBotGun} (FirearmAuthoring)";
                var dies=((JArray)row["sounds"]).OfType<JObject>().Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal))
                    .Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
                var entry=GenerateSpriteBot(unitId,XElement.Parse((string)row["rawXml"]),sheet,folder,dies,owned);
                entries.Add(entry);types.Add(entry.actorType);
            }
            return null;
        }

        private static CreatureEntry GenerateSpriteBot(string id,XElement unit,JObject sheet,string folder,string[] dies,HashSet<string> owned)
        {
            var phis=unit.Element("phis");var move=unit.Element("move");var comb=unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1),ear=Num(comb,"ear",1);
            float walk=Num(move,"speed")*FramesPerSecond/PixelsPerMetre;

            var texture=CopySheet(id,Path.Combine(folder,(string)sheet["sheet"]),(string)sheet["sheetSha256"],true,owned);
            int cellX=(int)sheet["cellWidth"],cellY=(int)sheet["cellHeight"];
            float scale=(float)sheet["pixelsPerSourcePixel"],regX=(float)sheet["registrationX"],regY=(float)sheet["registrationY"];
            if(texture.width!=cellX || texture.height!=cellY*2) throw new InvalidDataException($"Спрайтбот {id}: лист {texture.width}×{texture.height} не два ряда клеток {cellX}×{cellY}.");
            // setWeaponPos: weaponX = X, weaponY = Y − 1 — the gun turns about the unit's registration, a source pixel up.
            var socket=new AtlasWeaponSocketPoint {x=regX,y=regY-scale};
            AtlasActorClip Clip(string pose,int row)=>new() {pose=pose,row=row,column=0,count=1,framesPerSecond=FramesPerSecond,
                weaponSockets=new[]{new AtlasWeaponSocketPoint {x=socket.x,y=socket.y}}};
            const int Calm=0,Hostile=1;
            // No die clip (trup 0: the original leaves no body, expl() bursts it): the lying pose is the calm frame turned over.
            var clips=new[]{Clip("idle",Calm),Clip("walk",Calm),Clip("run",Calm),Clip("jump",Calm),
                Clip(Remains.Gameplay.NpcCreatureController.Hostile,Hostile),Clip("attack",Hostile),Clip("down",Calm)};
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+id+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre*scale,pivotX=regX/cellX,pivotY=1-regY/cellY,facesLeft=false,clips=clips};
            var atlas=SaveAtlas(id,texture,art,owned);

            var definition=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=true,atlasWeaponSocket=true,bodyInventory=false,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=true,
                // The calm vision (0.3 × 1000 px) and Unit.detecting (80 px) behind; ear 0 — it does not hear shots.
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=80/PixelsPerMetre,recognitionSeconds=.3f,
                    memorySeconds=4,hearingDistance=ear*vision*1000/PixelsPerMetre,eyeHeight=height*.5f,targetHeight=.25f},
                behavior=NpcCreatureBehaviours.SpriteBot,hitMaterial="metal",hitProtection=armor,deathBlastScale=.5f,
                activateTrap=(int)Num(unit.Element("param"),"acttrap",2),
                // No accel in AllData: the velocity is set at each decision; the acceleration only coasts it during a shot.
                motion=new CreatureMotionSettings {walkSpeed=walk,runSpeed=walk*1.5f,acceleration=Num(move,"accel",1)*FramesPerSecond*FramesPerSecond/PixelsPerMetre}
            };
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
            if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
            type.SetDefinition(definition);type.SetPrefab(SavePrefab(id,texture,atlas,true,owned));
            if(created) AssetDatabase.CreateAsset(type,actorPath);
            EditorUtility.SetDirty(type);
            var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

            // The gun is its initial inventory (no magazine); the body holds nothing to search, its loot drops on death.
            Directory.CreateDirectory(SpawnFolder);
            string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
            var inventory=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(inventoryPath);bool fresh=inventory==null;
            if(fresh) inventory=ScriptableObject.CreateInstance<ContainerLootAsset>();
            inventory.Configure(new ContainerLootTable {id=id+"-gun",version="1",entries=new[]{new ContainerLootEntry {id="gun",definition=SpriteBotGun,minimum=1,maximum=1,
                weaponState=new WeaponItemState {rounds=0}}}});
            if(fresh) AssetDatabase.CreateAsset(inventory,inventoryPath);else EditorUtility.SetDirty(inventory);
            string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
            if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
            spawn.Configure(id,id,hp,SpriteBotGun);spawn.SetInventory(inventory);
            if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);

            pendingAnatomy[id]=Anatomy(id,art,width,height);
            sounds[id]=dies;
            return new CreatureEntry {sourceId=id,actorType=id,family=NpcCreatureBehaviours.SpriteBot,spawn=spawn,
                deathLoot=CreatureDeathLootAuthoring.Install(id,unit),deathLootHeight=height,
                notes=new[]{"ствол robolaser","разозлившись, в оригинале видит на 20 м (vision 1); здесь дальность зрения одна на тип — 6 м",
                    "разброс скорости каждого спрайтбота (maxSpeed ± 1) не перенесён","тела нет в оригинале (trup 0); здесь лежит перевёрнутый корпус, как у турели"}};
        }
    }
}
