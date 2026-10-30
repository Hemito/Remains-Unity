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
    // Spider mines (UnitMsp) in the creature pass: a walking mine that runs at its target and explodes. Its picture is the
    // built vector sheet (creature-sheets-v3): the inner clip's rows stay (asleep), wake (woken or searching, its lamp
    // lit), walk (8 frames) and jump. Its death is a real explosion (UnitMsp.dropLoot → explosion(dam, tipDamage, 150)):
    // the explosive weapon <id>-blast — comb damage and damage type, a 150 px radius — sounded as the charges'. Numbers
    // are AllData (unit msp: phis, move speed/accel/jump, comb hp/armor/damage/tipdam, acttrap); no loot (its dropLoot is
    // only the blast) and no corpse to search (trup 0).
    public static partial class CreatureAuthoring
    {
        private const string SpiderMineClass="UnitMsp";
        private const float SpiderMineBlastRadius=150;

        //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunSpiderMinesBatch
        public static void RunSpiderMinesBatch()
        {
            try {RunSpiderMines();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunSpiderMines()=>RunFamily(SpiderMineClass,NpcCreatureBehaviours.SpiderMine,"Мина-паук",SpiderMines);

        private static string SpiderMines(CreatureSources.TemplateObject template,JObject[] rows,HashSet<string> owned,List<CreatureEntry> entries,List<string> types)
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
                if(!labels.SequenceEqual(new[]{"stay","wake","walk","jump"})) return "в листе нет рядов stay, wake, walk, jump: "+string.Join(", ",labels);
                var entry=GenerateSpiderMine(unitId,XElement.Parse((string)row["rawXml"]),sheet,folder,owned);
                entries.Add(entry);types.Add(entry.actorType);
            }
            return null;
        }

        private static CreatureEntry GenerateSpiderMine(string id,XElement unit,JObject sheet,string folder,HashSet<string> owned)
        {
            var phis=unit.Element("phis");var move=unit.Element("move");var comb=unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1),ear=Num(comb,"ear",1);
            float speed=Num(move,"speed")*FramesPerSecond/PixelsPerMetre;

            // The blast: Unit.explosion(dam, tipDamage, 150) — comb damage of its damage type within 150 px of X, Y − 3 px.
            string blast=id+"-blast";
            var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset)??throw new InvalidOperationException("Нет каталога оружия.");
            var serialized=new SerializedObject(weapons);
            MineContentAuthoring.Store(new WeaponDefinition {id=blast,damageType=DamageTypes.FromOriginal((int)Num(comb,"tipdam",4)),explosive=true,damage=Num(comb,"damage"),
                range=SpiderMineBlastRadius/PixelsPerMetre,cooldown=1,holdingArea=new HoldingArea()},serialized.FindProperty("weapons"));
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(weapons);
            HazardAuthoring.WriteAudio(new List<JObject> {new() {["weapon"]=blast,["clip"]="grenade-explosion",["kind"]="explosion",["minGain"]=1,["maxGain"]=1}});

            var texture=CopySheet(id,Path.Combine(folder,(string)sheet["sheet"]),(string)sheet["sheetSha256"],true,owned);
            int cellX=(int)sheet["cellWidth"],cellY=(int)sheet["cellHeight"];
            float scale=(float)sheet["pixelsPerSourcePixel"],regX=(float)sheet["registrationX"],regY=(float)sheet["registrationY"];
            var counts=((JArray)sheet["rows"]).OfType<JObject>().Select(r=>(int)r["frames"]).ToArray();
            AtlasActorClip Clip(string pose,int row,bool loop)=>new() {pose=pose,row=row,column=0,count=counts[row],framesPerSecond=FramesPerSecond,loop=loop && counts[row]>1,loopStartFrame=0};
            const int Stay=0,Wake=1,Walk=2,Jump=3;
            // No die clip (trup 0: the original leaves no body, the blast takes it): the lying pose is the asleep frame turned over.
            var clips=new[]{Clip("idle",Stay,false),Clip(Remains.Gameplay.NpcCreatureController.Woken,Wake,false),Clip("walk",Walk,true),Clip("run",Walk,true),
                Clip("jump",Jump,false),Clip("down",Stay,false)};
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+id+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre*scale,pivotX=regX/cellX,pivotY=1-regY/cellY,facesLeft=false,clips=clips};
            var atlas=SaveAtlas(id,texture,art,owned);

            var definition=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=false,bodyInventory=false,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=true,
                // Unit vision and ear (defaults 1: 1000 px), Unit.detecting (80 px) behind.
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=80/PixelsPerMetre,recognitionSeconds=.3f,
                    memorySeconds=4,hearingDistance=ear*vision*1000/PixelsPerMetre,eyeHeight=height*.5f,targetHeight=.25f},
                behavior=NpcCreatureBehaviours.SpiderMine,hitMaterial="metal",hitProtection=armor,deathExplosion=blast,
                activateTrap=(int)Num(unit.Element("param"),"acttrap",2),
                motion=new CreatureMotionSettings {walkSpeed=speed,runSpeed=speed,acceleration=Num(move,"accel",1)*FramesPerSecond*FramesPerSecond/PixelsPerMetre,
                    jumpSpeed=Num(move,"jump",0)*FramesPerSecond/PixelsPerMetre}
            };
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
            if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
            type.SetDefinition(definition);type.SetPrefab(SavePrefab(id,texture,atlas,true,owned));
            if(created) AssetDatabase.CreateAsset(type,actorPath);
            EditorUtility.SetDirty(type);
            var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

            Directory.CreateDirectory(SpawnFolder);
            string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
            if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
            spawn.Configure(id,id,hp,null);spawn.SetInventory(null);
            if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);

            pendingAnatomy[id]=Anatomy(id,art,width,height);
            sounds[id]=Array.Empty<string>();
            return new CreatureEntry {sourceId=id,actorType=id,family=NpcCreatureBehaviours.SpiderMine,spawn=spawn,
                notes=new[]{$"взрыв {blast}: урон {Num(comb,"damage")}, радиус {SpiderMineBlastRadius} px",
                    "отбрасывание (otbros 20) и разрушение блоков (destroy 30) взрыва не перенесены",
                    "пробуждение подъёмом телекинезом, прыжок через уступ на полвысоты и побудка соседей (budilo) не перенесены",
                    "тела нет в оригинале (trup 0); здесь лежит перевёрнутый корпус, как у турели"}};
        }
    }
}
