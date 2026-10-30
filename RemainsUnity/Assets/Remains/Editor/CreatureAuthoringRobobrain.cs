using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Robobrains (object robot → UnitRobobrain, a UnitAIRobot) in the creature pass: a walking robot with a gun on the
    // raiders' state machine in its robot variant (RaiderSettings.robot). Its gun is the first of its AllData list
    // (getXmlWeapon): robolaser with ch 0.5, else robospark — one actor type per gun, chosen evenly by placement, which
    // is that same half. Its picture is the built vector sheet (creature-sheets-v5): rows stay, walk (20 frames) and die
    // (10); the gun turns about setWeaponPos (X, Y − 40 px). Numbers are AllData (unit robobrain: phis, move speed/accel/
    // jump 0, comb hp/armor), runSpeed = 2 × speed (UnitRobobrain), aiDist 1000 px, aiAttackOch 100 frames; the body is
    // searchable (no trup 0) with its LootGen robobrain loot.
    public static partial class CreatureAuthoring
    {
        private const string RobobrainClass="UnitRobobrain";
        private static readonly string[] RobobrainGuns={"robolaser","robospark"};

        //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunRobobrainsBatch
        public static void RunRobobrainsBatch()
        {
            try {RunRobobrains();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunRobobrains()=>RunFamily(RobobrainClass,NpcCreatureBehaviours.Robobrain,"Робомозг",Robobrains);

        private static string Robobrains(CreatureSources.TemplateObject template,JObject[] rows,HashSet<string> owned,List<CreatureEntry> entries,List<string> types)
        {
            foreach(string unitId in template.units)
            {
                var made=entries.Where(e=>e.sourceId==unitId).ToArray();
                if(made.Length>0) {types.AddRange(made.Select(e=>e.actorType));continue;}
                var row=rows.FirstOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==unitId);
                if(row==null) return "нет определения unit "+unitId+" в выгрузке";
                var unit=XElement.Parse((string)row["rawXml"]);
                var list=unit.Elements("w").Select(w=>(string)w.Attribute("id")).ToArray();
                if(!list.SequenceEqual(RobobrainGuns) || (string)unit.Elements("w").First().Attribute("ch")!="0.5")
                    return "список оружия робомозга изменился: "+string.Join(", ",list);
                var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset).AllAssets().Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
                string missing=RobobrainGuns.Select(g=>$"weapon:{g}:grade:0").FirstOrDefault(id=>!weapons.Contains(id));
                if(missing!=null) return $"нет оружия {missing} (FirearmAuthoring)";
                var sheet=VectorSheet(unitId,out string folder);
                if(sheet==null) return "векторный вид не собран в лист (Tools/build_creature_sheets.py)";
                var labels=((JArray)sheet["rows"]).OfType<JObject>().Select(r=>(string)r["label"]).ToArray();
                if(!labels.SequenceEqual(new[]{"stay","walk","die"})) return "в листе нет рядов stay, walk, die: "+string.Join(", ",labels);
                var dies=((JArray)row["sounds"]).OfType<JObject>().Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal))
                    .Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
                foreach(var entry in GenerateRobobrain(unitId,unit,sheet,folder,dies,owned)) {entries.Add(entry);types.Add(entry.actorType);}
            }
            return null;
        }

        private static List<CreatureEntry> GenerateRobobrain(string unitId,XElement unit,JObject sheet,string folder,string[] dies,HashSet<string> owned)
        {
            var phis=unit.Element("phis");var move=unit.Element("move");var comb=unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1);
            float walk=Num(move,"speed")*FramesPerSecond/PixelsPerMetre;

            var texture=CopySheet(unitId,Path.Combine(folder,(string)sheet["sheet"]),(string)sheet["sheetSha256"],true,owned);
            int cellX=(int)sheet["cellWidth"],cellY=(int)sheet["cellHeight"];
            float scale=(float)sheet["pixelsPerSourcePixel"],regX=(float)sheet["registrationX"],regY=(float)sheet["registrationY"];
            var counts=((JArray)sheet["rows"]).OfType<JObject>().Select(r=>(int)r["frames"]).ToArray();
            // setWeaponPos: weaponX = X, weaponY = Y − 40 — the same point on every frame.
            AtlasActorClip Clip(string pose,int row,bool loop)=>new() {pose=pose,row=row,column=0,count=counts[row],framesPerSecond=FramesPerSecond,
                loop=loop && counts[row]>1,loopStartFrame=0,weaponSockets=Enumerable.Range(0,counts[row]).Select(_=>new AtlasWeaponSocketPoint {x=regX,y=regY-40*scale}).ToArray()};
            const int Stay=0,Walk=1,Die=2;
            var clips=new[]{Clip("idle",Stay,false),Clip("walk",Walk,true),Clip("run",Walk,true),Clip("jump",Stay,false),Clip("down",Die,false)};
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+unitId+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre*scale,pivotX=regX/cellX,pivotY=1-regY/cellY,facesLeft=false,clips=clips};
            var atlas=SaveAtlas(unitId,texture,art,owned);
            var prefab=SavePrefab(unitId,texture,atlas,false,owned);

            var raider=new RaiderSettings
            {
                attacker=RaiderAttacker.Ranged,robot=true,walkSpeed=walk,jumpSpeed=Num(move,"jump",0)*FramesPerSecond/PixelsPerMetre,
                acceleration=Num(move,"accel",5)*FramesPerSecond/PixelsPerMetre,holdDistance=1000/PixelsPerMetre,burstFrames=100,
                weaponInTeeth=Num(comb,"krep",0)==1,stroll=true,climbsLadders=false,slowness=0
            };
            var result=new List<CreatureEntry>();
            foreach(string gun in RobobrainGuns)
            {
                string id=unitId+"-"+gun,weapon=$"weapon:{gun}:grade:0";
                var definition=new ActorTypeDefinition
                {
                    id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                    combatAi=true,hasWeapon=true,atlasWeaponSocket=true,bodyInventory=true,canUseDoors=true,bodyMassKg=mass,telekinesisMovable=true,
                    // Unit vision × 1000 px (default 1), Unit.detecting (80 px) behind, the gun's height as its eye.
                    perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=80/PixelsPerMetre,recognitionSeconds=.3f,memorySeconds=4,
                        hearingDistance=vision*1000/PixelsPerMetre,eyeHeight=40/PixelsPerMetre,targetHeight=.7f},
                    behavior=NpcCreatureBehaviours.Robobrain,raider=raider,hitMaterial="metal",hitProtection=armor,deathBlastScale=.5f,
                    activateTrap=(int)Num(unit.Element("param"),"acttrap",2)
                };
                string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
                var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
                if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
                type.SetDefinition(definition);type.SetPrefab(prefab);
                if(created) AssetDatabase.CreateAsset(type,actorPath);
                EditorUtility.SetDirty(type);
                var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

                // Its gun (no magazine) and its LootGen robobrain loot, searchable on the body.
                Directory.CreateDirectory(SpawnFolder);
                string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
                var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
                if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
                spawn.Configure(id,id,hp,weapon);
                if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);
                string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
                CreatureLootAuthoring.Install(inventoryPath,id,unit,spawn,new ContainerLootTable {id=id+"-gun",version="1",
                    entries=new[]{new ContainerLootEntry {id="gun",definition=weapon,minimum=1,maximum=1,weaponState=new WeaponItemState {rounds=0}}}});

                pendingAnatomy[id]=Anatomy(id,art,width,height);
                result.Add(new CreatureEntry {sourceId=unitId,actorType=id,family=NpcCreatureBehaviours.Robobrain,spawn=spawn,
                    notes=new[]{"ствол "+gun,"телепорт (isPort), побудка клеток роботов (robocellActivate), реплики и оглушение (shok) не перенесены",
                        "в добыче нет случайного компонента (L_COMPM) и детали чемпиона (impgen)"}});
            }
            sounds[unitId]=dies;
            return result;
        }
    }
}
