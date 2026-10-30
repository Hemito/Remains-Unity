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
    // Rollers (UnitRoller, tr 1 — the factory's: the plasma roller2 is biome 6) in the creature pass: a rolling ball that
    // touches its target. Its picture is the built vector sheet (creature-sheets-v4): the inner clip's frame 1 (calm) and
    // frame 2 (charged, 24 frames) as rows state1 and state2, the registration the ball's centre (setVisPos: Y − scY/2),
    // turned by the visual as it rolls. Its touch is Unit.attKorp → udarUnit: comb damage of tipdam, the touch geometry
    // of the touch families. Numbers are AllData (unit roller: phis, move speed/jump, comb hp/armor/damage/tipdam/ear,
    // acttrap); its loot (LootGen roller: scrap 0.2) drops on death (trup 0).
    public static partial class CreatureAuthoring
    {
        private const string RollerClass="UnitRoller";

        //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunRollersBatch
        public static void RunRollersBatch()
        {
            try {RunRollers();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunRollers()=>RunFamily(RollerClass,NpcCreatureBehaviours.Roller,"Роллер",Rollers);

        private static string Rollers(CreatureSources.TemplateObject template,JObject[] rows,HashSet<string> owned,List<CreatureEntry> entries,List<string> types)
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
                if(!labels.SequenceEqual(new[]{"state1","state2"})) return "в листе нет рядов state1 (спокоен) и state2 (заряжен): "+string.Join(", ",labels);
                var dies=((JArray)row["sounds"]).OfType<JObject>().Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal))
                    .Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
                var entry=GenerateRoller(unitId,XElement.Parse((string)row["rawXml"]),sheet,folder,dies,owned);
                entries.Add(entry);types.Add(entry.actorType);
            }
            return null;
        }

        private static CreatureEntry GenerateRoller(string id,XElement unit,JObject sheet,string folder,string[] dies,HashSet<string> owned)
        {
            var phis=unit.Element("phis");var move=unit.Element("move");var comb=unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),damage=Num(comb,"damage"),vision=Num(comb,"vision",1),ear=Num(comb,"ear",1);
            float speed=Num(move,"speed")*FramesPerSecond/PixelsPerMetre,tipdam=Num(comb,"tipdam",2);

            var texture=CopySheet(id,Path.Combine(folder,(string)sheet["sheet"]),(string)sheet["sheetSha256"],true,owned);
            int cellX=(int)sheet["cellWidth"],cellY=(int)sheet["cellHeight"];
            float scale=(float)sheet["pixelsPerSourcePixel"],regX=(float)sheet["registrationX"],regY=(float)sheet["registrationY"];
            var counts=((JArray)sheet["rows"]).OfType<JObject>().Select(r=>(int)r["frames"]).ToArray();
            AtlasActorClip Clip(string pose,int row,bool loop)=>new() {pose=pose,row=row,column=0,count=counts[row],framesPerSecond=FramesPerSecond,loop=loop && counts[row]>1,loopStartFrame=0};
            const int Calm=0,Charged=1;
            var clips=new[]{Clip("idle",Calm,false),Clip("walk",Calm,false),Clip("run",Calm,false),Clip("jump",Calm,false),
                Clip(Remains.Gameplay.NpcCreatureController.Charged,Charged,true),Clip("down",Calm,false)};
            // The sheet's registration is the ball's centre; the atlas pivot is the unit's feet, half its height below.
            float feet=regY+Num(phis,"sY")*.5f*scale;
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+id+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre*scale,pivotX=regX/cellX,pivotY=1-feet/cellY,facesLeft=false,clips=clips};
            var atlas=SaveAtlas(id,texture,art,owned);

            var definition=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=false,bodyInventory=false,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=true,
                // Unit vision (default 1: 1000 px), Unit.detecting (80 px) behind; ear 0 — it does not hear shots.
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=80/PixelsPerMetre,recognitionSeconds=.3f,
                    memorySeconds=4,hearingDistance=ear*vision*1000/PixelsPerMetre,eyeHeight=height*.5f,targetHeight=.25f},
                // Touch (attKorp → udarUnit), as the touch families: no preparation, the victim's 30-frame window as the pause.
                naturalAttack=new NaturalAttackDefinition {damageType=DamageTypes.FromOriginal((int)tipdam),approachSpeed=speed,beginDistance=width*.5f+.4f,verticalDistance=height+1f,
                    preparationSeconds=0,recoverySeconds=1,damage=damage,originX=0,originY=height*.5f,reach=width*.5f+.3f,radius=Mathf.Max(.1f,height*.4f)},
                behavior=NpcCreatureBehaviours.Roller,hitMaterial="metal",hitProtection=armor,deathBlastScale=.5f,attackSounds=TouchSounds(tipdam),
                activateTrap=(int)Num(unit.Element("param"),"acttrap",2),
                motion=new CreatureMotionSettings {walkSpeed=speed,runSpeed=speed,acceleration=Num(move,"accel",1)*FramesPerSecond*FramesPerSecond/PixelsPerMetre,
                    jumpSpeed=Num(move,"jump",0)*FramesPerSecond/PixelsPerMetre}
            };
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
            if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
            type.SetDefinition(definition);type.SetPrefab(SavePrefab(id,texture,atlas,true,owned,rolls:true));
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
            sounds[id]=dies;
            return new CreatureEntry {sourceId=id,actorType=id,family=NpcCreatureBehaviours.Roller,spawn=spawn,
                deathLoot=CreatureDeathLootAuthoring.Install(id,unit),deathLootHeight=height,
                notes=new[]{"гибель в воде (isPlav → die), отдача урона (retdam) и магическая броня (marmor) не перенесены",
                    "разброс скорости каждого роллера (maxSpeed ± 1) не перенесён; в воздухе крутится по скорости, а не сохранённым вращением",
                    "тела нет в оригинале (trup 0); здесь лежит перевёрнутый корпус, как у турели"}};
        }
    }
}
