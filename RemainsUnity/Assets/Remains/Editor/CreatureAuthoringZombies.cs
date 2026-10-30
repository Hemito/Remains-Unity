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
    // Zombies (UnitZombie, stage 1) in the creature pass: the object zombie becomes zombieN by the danger of its room
    // (Location.randomCid, biome 0 — the factory: 0–4), each on ZombieBrain over the raiders' mind. Values are AllData
    // (unit zombie and its variant: phis, move speed/run/jump/accel, comb hp/damage/armor/raddamage, un ss/glow) on the
    // original raster sheet sprZombieN (BlitAnim rows: stay, trot, run, jump by vertical speed, die, dig, walk). The
    // glowing ghoul (ss 5) radiates (radioactiv 1 within 120 px, its flare 20 within 1200 px). Variants 5 and 6 belong to
    // other biomes; the other super powers are stage 2.
    public static partial class CreatureAuthoring
    {
        private const string ZombieClass="UnitZombie";
        private static readonly int[] FactoryZombies={0,1,2,3,4};
        // UnitZombie.jump stab: frame = (dy × 0.6 + 8) / 16 of 16, dy in px a frame down — vy 8 m/s up is the first frame.
        private static readonly MotionFramePoint[] ZombieJumpFrames={new() {velocity=-8,frame=15},new() {velocity=8,frame=0}};

        //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunZombiesBatch
        public static void RunZombiesBatch()
        {
            try {RunZombies();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunZombies()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var rows=((JArray)catalog["definitions"]).OfType<JObject>().ToArray();
            var resources=((JArray)catalog["resources"]).OfType<JObject>().ToDictionary(r=>(string)r["id"],StringComparer.Ordinal);
            IndexSounds(resources.Values);
            var previous=AssetDatabase.LoadAssetAtPath<CreatureCatalog>(CatalogPath)??throw new InvalidOperationException("Нет каталога существ: сначала полная генерация.");
            var owned=new HashSet<string>(previous.Creatures.Select(c=>c.actorType),StringComparer.Ordinal);
            var template=CreatureSources.FactoryObjects(catalog).Single(o=>o.className==ZombieClass && o.problem==null);
            var made=new List<CreatureEntry>();
            var rule=Zombies(template,rows,resources,owned,made);
            var entries=previous.Creatures.Where(c=>c.family!=NpcCreatureBehaviours.Zombie).Concat(made).ToArray();
            var rules=previous.Objects.Where(r=>r.objectId!=template.objectId).Append(rule).ToArray();
            var gaps=previous.Gaps.Where(g=>g.sourceId!=template.objectId).ToArray();
            var generated=made.Select(e=>e.actorType).ToHashSet(StringComparer.Ordinal);
            WriteAnatomy(generated,owned,generated);
            WriteDeathAudio(made,owned,partial:true);
            previous.Configure(entries,rules,gaps);EditorUtility.SetDirty(previous);AssetDatabase.SaveAssets();
            foreach(var entry in made) Debug.Log($"Зомби {entry.actorType}: {string.Join("; ",entry.notes)}.");
            foreach(var variant in rule.variants.Where(v=>!string.IsNullOrEmpty(v.gap))) Debug.Log($"Вариант {variant.unit} не подключён: {variant.gap}");
        }

        private static CreatureObjectRule Zombies(CreatureSources.TemplateObject template,JObject[] rows,Dictionary<string,JObject> resources,HashSet<string> owned,List<CreatureEntry> entries)
        {
            var variants=new List<CreatureVariant>();
            foreach(string unitId in template.units)
            {
                int cid=int.Parse(unitId.Substring("zombie".Length));
                var variant=new CreatureVariant {unit=unitId,cid=cid};variants.Add(variant);
                if(!FactoryZombies.Contains(cid)) {variant.gap="на заводе не встречается (биом 0: zombie0–4)";continue;}
                var made=entries.FirstOrDefault(e=>e.sourceId==unitId);
                if(made!=null) {variant.actorType=made.actorType;continue;}
                string reason=Select(unitId,ZombieClass,rows,resources,out var source,NpcCreatureBehaviours.Zombie);
                if(reason!=null) {variant.gap=reason;continue;}
                var entry=GenerateZombie(source,owned);
                entries.Add(entry);variant.actorType=entry.actorType;
            }
            return new CreatureObjectRule {objectId=template.objectId,actorTypes=variants.Where(v=>v.actorType!=null).Select(v=>v.actorType).ToArray(),
                variantRule=CreatureObjectRule.ZombieVariants,variants=variants.ToArray()};
        }

        private static CreatureEntry GenerateZombie(Source source,HashSet<string> owned)
        {
            string id=source.id;string package=ContentPackage.Directory;
            var phis=source.unit.Element("phis");var move=source.unit.Element("move");var comb=source.unit.Element("comb");var un=source.unit.Element("un");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),damage=Num(comb,"damage"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1),ear=Num(comb,"ear",1);
            float Speed(string name)=>Num(move,name)*FramesPerSecond/PixelsPerMetre;
            int super=(int)Num(un,"ss",0);

            var file=((JArray)source.sheet["files"]).OfType<JObject>().Single(f=>Path.GetExtension((string)f["path"]).ToLowerInvariant()==".png");
            var texture=CopySheet(id,Path.GetFullPath(Path.Combine(package,(string)file["path"])),(string)file["sha256"],false,owned);
            int cellX=(int)Num(source.vis,"sprX"),cellY=(int)Num(source.vis,"sprY",cellX);
            float dx=Num(source.vis,"sprDX",-1),dy=Num(source.vis,"sprDY",-1);
            if(dx<0) dx=cellX*.5f;if(dy<0) dy=cellY-10;
            var blits=source.blits.ToDictionary(b=>(string)b.Attribute("id"),StringComparer.Ordinal);
            XElement Blit(string name)=>blits.TryGetValue(name,out var blit)?blit:throw new InvalidDataException($"Зомби {id}: нет анимации {name}.");
            var jump=BlitClip("jump",Blit("jump"),false);jump.verticalFrames=ZombieJumpFrames;
            var clips=new[]{BlitClip("idle",Blit("stay"),false),BlitClip("walk",Blit("walk")),BlitClip("trot",Blit("trot")),BlitClip("run",Blit("run")),jump,
                BlitClip("down",Blit("die"),false),BlitClip(ZombieBrain.EmergingMode,Blit("dig"),false)};
            if(texture.width%cellX!=0 || texture.height%cellY!=0) throw new InvalidDataException($"Зомби {id}: лист {texture.width}×{texture.height} не делится на клетки {cellX}×{cellY}.");
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+id+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre,pivotX=dx/cellX,pivotY=1-dy/cellY,facesLeft=false,clips=clips};
            var atlas=SaveAtlas(id,texture,art,owned);

            float tipdam=Num(comb,"tipdam",2);
            var raider=new RaiderSettings
            {
                attacker=RaiderAttacker.Teeth,zombie=true,walkSpeed=Speed("speed"),zombieRun=Speed("run"),jumpSpeed=Speed("jump"),
                acceleration=Num(move,"accel",5)*FramesPerSecond/PixelsPerMetre,holdDistance=0,weaponInTeeth=false,stroll=true,climbsLadders=false,slowness=0,
                attackReach=200/PixelsPerMetre,zombieSuper=super==5?5:0,
                // setSuper 5: tPrepSuper 30, tSuper 45, tZlo 140.
                angerFrames=super==5?140:120,prepareFrames=30,superFrames=super==5?45:75,
                bodyHit=new NaturalAttackDefinition {damageType=DamageTypes.FromOriginal((int)tipdam),approachSpeed=Speed("speed"),beginDistance=width*.5f+.4f,verticalDistance=height+1f,
                    preparationSeconds=0,recoverySeconds=1,damage=damage,originX=0,originY=height*.5f,reach=width*.5f+.3f,radius=Mathf.Max(.1f,height*.4f)}
            };
            var definition=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=false,bodyInventory=true,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=true,
                // Unit vision and ear × 1000 px (defaults 1); an unalarmed zombie looks with 0.7 of it (not carried over).
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=80/PixelsPerMetre,recognitionSeconds=.3f,memorySeconds=4,
                    hearingDistance=ear*1000/PixelsPerMetre,eyeHeight=height*.64f,targetHeight=.7f},
                behavior=NpcCreatureBehaviours.Zombie,raider=raider,hitMaterial="flesh",hitProtection=armor,attackSounds=TouchSounds(tipdam),
                activateTrap=(int)Num(source.unit.Element("param"),"acttrap",2),radiationHit=Num(comb,"raddamage",0),
                // setSuper 5: radioactiv 1 within 120 px; the flare 20 within 1200 px.
                radiationAura=super==5?new RadiationAura {strength=1,radius=120/PixelsPerMetre,flareStrength=20,flareRadius=1200/PixelsPerMetre}:null
            };
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
            if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
            type.SetDefinition(definition);type.SetPrefab(SavePrefab(id,texture,atlas,false,owned));
            if(created) AssetDatabase.CreateAsset(type,actorPath);
            EditorUtility.SetDirty(type);
            var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

            Directory.CreateDirectory(SpawnFolder);
            string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
            if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
            spawn.Configure(id,id,hp,null);
            if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);
            string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
            CreatureLootAuthoring.Install(inventoryPath,id,source.unit,spawn);

            pendingAnatomy[id]=Anatomy(id,art,width,height);
            sounds[id]=source.dieSounds;
            var notes=new List<string>{"этап 1: общее поведение, закопанные, удар корпусом"};
            if(super==5) notes.Add("светящийся: радиация вокруг и вспышка; лечение других зомби вспышкой не перенесено");
            else if(super>0) notes.Add($"суперсила ss {super} — этап 2");
            if(source.unit.Element("un")?.Attribute("res")!=null) notes.Add("воскрешение (res) — этап 2");
            return new CreatureEntry {sourceId=id,actorType=id,family=NpcCreatureBehaviours.Zombie,spawn=spawn,notes=notes.ToArray()};
        }
    }
}
