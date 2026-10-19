using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Creatures from the original data in one pass (no per-creature hand authoring): for every unit type placed in
    // the factory templates (CreatureSources) whose behaviour family is implemented and whose artwork can be imported
    // at full quality, it writes the actor type, animation atlas (the original raster sheet, BlitAnim semantics),
    // prefab, anatomy, initial state and death voice; the rest are listed in CreatureCatalog as gaps with a reason.
    // Values come from AllData (sizes, speeds, health, damage, vision; px → m at 50 px/m, px/frame → m/s at 30 fps);
    // what the original does not define (contact geometry, timings) is the family's documented default.
    // Generated assets are owned by this tool: re-running replaces them; a hand-made asset at a generated path stops it.
    //   batch -executeMethod Remains.Editor.CreatureAuthoring.RunBatch
    public static partial class CreatureAuthoring
    {

        private const string ArtFolder="Assets/Remains/Resources/Original/Creatures";
        private const string PrefabFolder="Assets/Remains/Prefabs/Actors/Creatures";
        private const string SpawnFolder="Assets/Remains/Content/Creatures";
        private const string ActorFolder="Assets/Remains/Resources/Content/actors";
        private const string CatalogPath="Assets/Remains/Resources/"+CreatureCatalog.Resource+".asset";
        private const string AnatomyPath="Assets/Remains/Resources/Content/anatomy.json";
        private const string DeathAudioPath="Assets/Remains/Resources/"+ActorDeathAudioCatalog.Resource+".json";
        private const float PixelsPerMetre=50,FramesPerSecond=30;

        // Behaviour families implemented in Unity (original class → family). A class not listed here is a gap.
        private static readonly Dictionary<string,string> families=new(StringComparer.Ordinal)
            {{"UnitMonstrik",NpcCreatureBehaviours.Crawler},{"UnitBat",NpcCreatureBehaviours.Bat},{"UnitBloat",NpcCreatureBehaviours.Bloat},{"UnitSlime",NpcCreatureBehaviours.Slime}};
        // Vector creatures drawn into animation sheets by Tools/build_creature_sheets.py (rows per state, cells at the
        // unit's registration); the raster ones use the original blit sheets.
        private const string VectorSheets="../../.reference/creature-sheets-v1";

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        private sealed class Source
        {
            public string id,family,className;public XElement unit;public JObject definition;
            public XElement vis;public XElement[] blits;public JObject sheet;public string[] dieSounds;
            public JObject vector;public string vectorFolder;   // a built vector sheet (manifest entry) instead of a blit sheet
        }

        public static void Run()
        {
            string package=ContentPackage.Directory;
            var catalog=JObject.Parse(File.ReadAllText(Path.Combine(package,"catalog.json")));
            var rows=((JArray)catalog["definitions"]).OfType<JObject>().ToArray();
            var resources=((JArray)catalog["resources"]).OfType<JObject>().ToDictionary(r=>(string)r["id"],StringComparer.Ordinal);
            soundIds.Clear();
            foreach(var resource in resources.Values.Where(r=>(string)r["kind"]=="sound"))
                foreach(var name in ((JArray)resource["names"]).Select(n=>(string)n)) soundIds.TryAdd(name,(string)resource["id"]);
            var previous=AssetDatabase.LoadAssetAtPath<CreatureCatalog>(CatalogPath);
            var owned=new HashSet<string>(previous?.Creatures.Select(c=>c.actorType)??Enumerable.Empty<string>(),StringComparer.Ordinal);
            var entries=new List<CreatureEntry>();var rules=new List<CreatureObjectRule>();var gaps=new List<CreatureGap>();
            foreach(var template in CreatureSources.FactoryObjects(catalog).OrderBy(o=>o.objectId,StringComparer.Ordinal))
            {
                // Traps and charges are hazards (HazardAuthoring), neither creatures nor gaps.
                if(template.problem==null && template.units.All(u=>CreatureSources.HazardKind(template.className,CreatureSources.UnitXml(catalog,u))!=null)) continue;
                string reason=template.problem;
                var types=new List<string>();
                if(reason==null && template.className==CreatureSources.TurretClass) reason=Turrets(template,rows,resources,package,owned,entries,types);
                else foreach(string unitId in template.units)
                {
                    if(reason!=null) break;
                    var existing=entries.FirstOrDefault(e=>e.actorType==unitId);
                    if(existing!=null) {types.Add(existing.actorType);continue;}
                    reason=Select(unitId,template.className,rows,resources,out var source);
                    if(reason==null) {var entry=Generate(source,package,owned);entries.Add(entry);types.Add(entry.actorType);}
                }
                if(reason!=null) gaps.Add(new CreatureGap {sourceId=template.objectId,reason=reason});
                else rules.Add(new CreatureObjectRule {objectId=template.objectId,actorTypes=types.ToArray()});
            }
            WriteAnatomy(entries.Select(e=>e.actorType).ToHashSet(StringComparer.Ordinal),owned);
            WriteDeathAudio(entries,owned);
            var asset=previous??ScriptableObject.CreateInstance<CreatureCatalog>();
            asset.Configure(entries.ToArray(),rules.ToArray(),gaps.ToArray());
            if(previous==null) {Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));AssetDatabase.CreateAsset(asset,CatalogPath);}
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssets();
            foreach(var entry in entries)
                Debug.Log($"Существо {entry.sourceId} → {entry.actorType} ({entry.family}){(entry.notes.Length==0?"":": "+string.Join("; ",entry.notes))}.");
            foreach(var rule in rules) Debug.Log($"Объект {rule.objectId} → {string.Join(" | ",rule.actorTypes)}.");
            foreach(var gap in gaps) Debug.Log($"Объект {gap.sourceId} не подключён: {gap.reason}");
            Debug.Log($"Существа завода: типов {entries.Count}, объектов подключено {rules.Count}, ещё нет {gaps.Count}.");
        }

        private static string Select(string id,string cls,JObject[] rows,Dictionary<string,JObject> resources,out Source source)
        {
            source=null;
            var unit=rows.FirstOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==id);
            if(unit==null) return "нет определения unit "+id+" в выгрузке";
            if(!families.TryGetValue(cls,out string family)) return $"поведение класса оригинала {cls} ещё не реализовано";
            // Unit.getXmlParam reads the parent unit first (UnitBloat: "bloat"), then the unit's own values over it.
            var parentRow=(string)unit["parent"] is {} parentId?rows.FirstOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==parentId):null;
            var xml=Inherit(XElement.Parse((string)unit["rawXml"]),parentRow==null?null:XElement.Parse((string)parentRow["rawXml"]));
            var dies=new[]{unit,parentRow}.Where(r=>r!=null).SelectMany(r=>((JArray)r["sounds"]).OfType<JObject>())
                .Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal)).Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
            source=new Source {id=id,family=family,className=cls,unit=xml,definition=unit,dieSounds=dies};
            var sheet=((JArray)unit["graphics"]).OfType<JObject>().FirstOrDefault(g=>(string)g["role"]=="animation-sheet");
            if(sheet!=null)
            {
                var meta=XElement.Parse("<m>"+(string)sheet["metadataXml"]+"</m>");
                if(!resources.TryGetValue((string)sheet["resource"],out var resource)) return "нет ресурса листа "+sheet["resource"];
                source.vis=meta.Element("vis");source.blits=meta.Elements("blit").ToArray();source.sheet=resource;
                return null;
            }
            string sheets=Path.GetFullPath(Path.Combine(Application.dataPath,VectorSheets));
            string manifest=Path.Combine(sheets,"manifest.json");
            var built=File.Exists(manifest)?(JObject)JObject.Parse(File.ReadAllText(manifest))["units"]?[id]:null;
            if(built==null) {source=null;return "векторный вид не собран в лист (Tools/build_creature_sheets.py)";}
            source.vector=built;source.vectorFolder=sheets;
            return null;
        }

        private static CreatureEntry Generate(Source source,string package,HashSet<string> owned)
        {
            string id=source.id;var notes=new List<string>();
            var phis=source.unit.Element("phis");var move=source.unit.Element("move");var comb=source.unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),damage=Num(comb,"damage"),vision=Num(comb,"vision",1);
            float walk=Num(move,"speed")*FramesPerSecond/PixelsPerMetre,run=Num(move,"run",Num(move,"speed"))*FramesPerSecond/PixelsPerMetre;
            if(width<=0 || height<=0 || hp<=0 || damage<0 || run<=0) throw new InvalidDataException($"Существо {id}: неполные размеры, здоровье или скорость в AllData.");

            // Sheet: the original raster or the built vector sheet, hash-checked, as its own texture.
            string folder=ArtFolder+"/"+id;Directory.CreateDirectory(folder);
            string input,expected;
            if(source.vector!=null) {input=Path.Combine(source.vectorFolder,(string)source.vector["sheet"]);expected=(string)source.vector["sheetSha256"];}
            else
            {
                var file=((JArray)source.sheet["files"]).OfType<JObject>().Single(f=>Path.GetExtension((string)f["path"]).ToLowerInvariant()==".png");
                input=Path.GetFullPath(Path.Combine(package,(string)file["path"]));expected=(string)file["sha256"];
            }
            if(Hash(input)!=expected) throw new InvalidDataException($"Существо {id}: лист {input} не совпадает с SHA256 источника.");
            string texturePath=folder+"/sheet.png";
            Guard(texturePath,id,owned);
            File.Copy(input,texturePath,true);AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.filterMode=source.vector!=null?FilterMode.Bilinear:FilterMode.Point;
            importer.alphaIsTransparency=true;importer.maxTextureSize=8192;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            int cellX,cellY;float dx,dy,pixelsPerUnit;bool lyingFrame;AtlasActorClip[] clips;
            if(source.vector!=null)
            {
                cellX=(int)source.vector["cellWidth"];cellY=(int)source.vector["cellHeight"];
                dx=(float)source.vector["registrationX"];dy=(float)source.vector["registrationY"];
                pixelsPerUnit=PixelsPerMetre*(float)source.vector["pixelsPerSourcePixel"];
                clips=VectorClips(source,notes,out lyingFrame);
            }
            else
            {
                cellX=(int)Num(source.vis,"sprX");cellY=(int)Num(source.vis,"sprY",cellX);
                dx=Num(source.vis,"sprDX",-1);dy=Num(source.vis,"sprDY",-1);
                // Unit.initBlit: the bitmap is offset by sprDX/sprDY (defaults: half width, height − 10).
                if(dx<0) dx=cellX*.5f;if(dy<0) dy=cellY-10;
                pixelsPerUnit=PixelsPerMetre;clips=Clips(source,notes,out lyingFrame);
            }
            if(texture.width%cellX!=0 || texture.height%cellY!=0) throw new InvalidDataException($"Существо {id}: лист {texture.width}×{texture.height} не делится на клетки {cellX}×{cellY}.");
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+id+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=pixelsPerUnit,pivotX=dx/cellX,pivotY=1-dy/cellY,facesLeft=false,clips=clips};
            string atlasPath=folder+"/artwork.asset";Guard(atlasPath,id,owned);
            var atlas=AssetDatabase.LoadAssetAtPath<AtlasSpriteAsset>(atlasPath);
            if(atlas==null) {atlas=ScriptableObject.CreateInstance<AtlasSpriteAsset>();atlas.Configure(texture,art);AssetDatabase.CreateAsset(atlas,atlasPath);}
            else atlas.Configure(texture,art);
            AtlasSpriteAssetEditor.Rebuild(atlas);

            var definition=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=false,bodyInventory=true,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=true,
                // Unit vision: a target is noticed within vision × 1000 px (Unit.as); close range and timings: crawler family.
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=width*2,recognitionSeconds=.3f,
                    memorySeconds=4,hearingDistance=vision*1000/PixelsPerMetre,eyeHeight=height*.67f,targetHeight=.25f},
                naturalAttack=source.family==NpcCreatureBehaviours.Crawler
                    // Bite geometry scaled from the crawler family (CRAWLER_SPEC, radroach); damage and speed are AllData.
                    ?new NaturalAttackDefinition {approachSpeed=run,beginDistance=width*1.5f,verticalDistance=Mathf.Max(.65f,height*2.2f),
                        preparationSeconds=.3f,recoverySeconds=1,damage=damage,originX=width*.33f,originY=height*.53f,reach=width*1.25f,radius=Mathf.Max(.08f,height*.4f)}
                    // Touch (attKorp → udarUnit): no preparation; the pause is the victim's 30-frame window between touches.
                    :new NaturalAttackDefinition {approachSpeed=walk,beginDistance=width*.5f+.4f,verticalDistance=height+1f,
                        preparationSeconds=0,recoverySeconds=1,damage=damage,originX=0,originY=height*.5f,reach=width*.5f+.3f,radius=Mathf.Max(.1f,height*.4f)},
                behavior=source.family==NpcCreatureBehaviours.Crawler?null:source.family,
                activateTrap=(int)Num(source.unit.Element("param"),"acttrap",2),
                hitMaterial=MetalClasses.Contains(source.className)?"metal":source.className=="UnitSlime"?"slime":"flesh",
                attackSounds=TouchSounds(Num(comb,"tipdam",0)),
                motion=source.family==NpcCreatureBehaviours.Crawler?null:Motion(source,walk)
            };
            if(walk<=0) notes.Add("нет скорости шага");
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);
            bool created=type==null;if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
            type.SetDefinition(definition);

            Directory.CreateDirectory(PrefabFolder);
            string prefabPath=PrefabFolder+"/"+id+".prefab",materialPath=PrefabFolder+"/"+id+".mat";Guard(prefabPath,id,owned);Guard(materialPath,id,owned);
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,materialPath);}
            var root=new GameObject(id);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=atlas.Frames("idle")[0];renderer.sortingOrder=20;renderer.sharedMaterial=material;
                var visual=root.AddComponent<AtlasActorVisual>();visual.SetRenderer(renderer);
                // A lying pose that is the standing frame (no die/death row) is drawn turned over and greyed, as the radroach.
                visual.ConfigureDeadPresentation(lyingFrame,0,new Color(.55f,.55f,.55f,1));
                type.SetPrefab(PrefabUtility.SaveAsPrefabAsset(root,prefabPath).GetComponent<AtlasActorVisual>());
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            if(created) AssetDatabase.CreateAsset(type,actorPath);
            EditorUtility.SetDirty(type);
            var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

            Directory.CreateDirectory(SpawnFolder);
            string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);
            if(spawn==null) {spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();spawn.Configure(id,id,hp,null);AssetDatabase.CreateAsset(spawn,spawnPath);}
            else spawn.Configure(id,id,hp,null);
            EditorUtility.SetDirty(spawn);
            string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
            CreatureLootAuthoring.Install(inventoryPath,id,source.unit,spawn);
            if(source.dieSounds.Length==0) notes.Add("нет голоса смерти в AllData");
            pendingAnatomy[id]=Anatomy(id,art,width,height);
            sounds[id]=source.dieSounds;
            return new CreatureEntry {sourceId=id,actorType=id,family=source.family,spawn=spawn,notes=notes.ToArray()};
        }

        // Unit classes that set mat = 1 (metal) in their constructors; UnitSlime sets 12.
        private static readonly HashSet<string> MetalClasses=new(StringComparer.Ordinal) {"UnitMsp","UnitDamager","UnitAIRobot","UnitDestr","UnitDron"};
        // Unit.udarUnit: the touch sound by the attacker's damage type.
        private static string[] TouchSounds(float damageType)
        {
            string[] names=damageType switch {14=>new[]{"fang_hit1","fang_hit2","fang_hit3"},10=>new[]{"acid"},9=>new[]{"electro"},16=>new[]{"hit_necr"},_=>new[]{"hit_flesh"}};
            return names.Select(name=>"exported:"+(soundIds.TryGetValue(name,out string id)?id:throw new InvalidDataException("Нет исходного звука "+name))).ToArray();
        }
        private static readonly Dictionary<string,string> soundIds=new(StringComparer.Ordinal);
        private static CreatureMotionSettings Motion(Source source,float walk)
        {
            var move=source.unit.Element("move");var un=source.unit.Element("un");
            float run=move?.Attribute("run")!=null?Num(move,"run")*FramesPerSecond/PixelsPerMetre:walk*2;   // UnitBat/UnitBloat: runSpeed = 2 × speed
            return new CreatureMotionSettings {walkSpeed=walk,runSpeed=Mathf.Max(run,walk),acceleration=Num(move,"accel",1)*FramesPerSecond*FramesPerSecond/PixelsPerMetre,
                dashRange=Num(un,"attr",400)/PixelsPerMetre,dashChance=Num(un,"attch",.4f)};
        }
        private static XElement Inherit(XElement unit,XElement parent)
        {
            if(parent==null) return unit;
            var merged=new XElement(parent);
            foreach(var own in unit.Elements())
            {
                var target=merged.Element(own.Name);
                if(target==null) {merged.Add(new XElement(own));continue;}
                foreach(var attribute in own.Attributes()) target.SetAttributeValue(attribute.Name,attribute.Value);
            }
            foreach(var attribute in unit.Attributes()) merged.SetAttributeValue(attribute.Name,attribute.Value);
            return merged;
        }
        // Vector sheets: a row per state clip (stay/fly/attack/die) or one looping row (the clip's own frames), 30 fps.
        private static AtlasActorClip[] VectorClips(Source source,List<string> notes,out bool lyingFrame)
        {
            var rows=((JArray)source.vector["rows"]).OfType<JObject>().Select((r,i)=>(label:(string)r["label"],frames:(int)r["frames"],row:i)).ToList();
            AtlasActorClip Clip(string pose,(string label,int frames,int row) r,bool loop)=>new() {pose=pose,row=r.row,column=0,count=r.frames,
                framesPerSecond=FramesPerSecond,loop=loop && r.frames>1,loopStartFrame=0};
            var byLabel=rows.ToDictionary(r=>r.label,StringComparer.Ordinal);
            lyingFrame=false;
            if(byLabel.TryGetValue("fly",out var fly))
            {
                var stay=byLabel.TryGetValue("stay",out var s)?s:fly;
                var attack=byLabel.TryGetValue("attack",out var a)?a:fly;
                var result=new List<AtlasActorClip> {Clip("idle",stay,true),Clip("walk",fly,true),Clip("run",fly,true),Clip("jump",fly,true),Clip("attack",attack,true)};
                if(byLabel.TryGetValue("die",out var die)) result.Add(Clip("down",die,false));
                else {lyingFrame=true;result.Add(Clip("down",(stay.label,1,stay.row),false));notes.Add("нет анимации смерти — лежит стоящий кадр");}
                return result.ToArray();
            }
            if(rows.Count!=1) throw new InvalidDataException($"Существо {source.id}: ряды листа без метки fly не разобраны: {string.Join(", ",rows.Select(r=>r.label))}.");
            var only=rows[0];lyingFrame=true;
            notes.Add("одна анимация на все позы; смерть — стоящий кадр перевёрнут");
            return new[]{Clip("idle",only,true),Clip("walk",only,true),Clip("run",only,true),Clip("jump",only,true),Clip("attack",only,true),Clip("down",(only.label,1,only.row),false)};
        }

        // BlitAnim (serv/BlitAnim.as): row y, frames ff…ff+len−1 stepped by df per 30 fps tick, replay from rf.
        private static AtlasActorClip[] Clips(Source source,List<string> notes,out bool lyingFrame)
        {
            var blits=source.blits.ToDictionary(b=>(string)b.Attribute("id"),StringComparer.Ordinal);
            AtlasActorClip Clip(string pose,XElement blit,bool? loop=null)
            {
                int first=(int)Num(blit,"ff",0),count=(int)Num(blit,"len",1);
                bool replay=loop??blit.Attribute("rep")!=null;
                int restart=(int)Num(blit,"rf",first);
                return new AtlasActorClip {pose=pose,row=(int)Num(blit,"y",0),column=first,count=count,framesPerSecond=FramesPerSecond*Num(blit,"df",1),
                    loop=replay && count>1,loopStartFrame=replay && count>1?Mathf.Clamp(restart-first,0,count-1):0};
            }
            if(!blits.TryGetValue("stay",out var stay)) throw new InvalidDataException($"Существо {source.id}: нет анимации stay.");
            var clips=new List<AtlasActorClip> {Clip("idle",stay,false)};
            clips.Add(blits.TryGetValue("walk",out var walk)?Clip("walk",walk):Rename(Clip("idle",stay,false),"walk",notes,"шаг"));
            clips.Add(blits.TryGetValue("run",out var run)?Clip("run",run):Rename(clips[1],"run",notes,"бег"));
            clips.Add(blits.TryGetValue("jump",out var jump)?Clip("jump",jump,false):Rename(clips[0],"jump",notes,"прыжок"));
            // Lying: the dying animation holding its last frame, else the static dead frame, else the standing frame.
            lyingFrame=false;
            if(blits.TryGetValue("die",out var die)) clips.Add(Clip("down",die,false));
            else if(blits.TryGetValue("death",out var death)) clips.Add(Clip("down",death,false));
            else {lyingFrame=true;clips.Add(Rename(clips[0],"down",notes,"лежачая поза (стоящий кадр перевёрнут)"));}
            return clips.ToArray();
        }
        private static AtlasActorClip Rename(AtlasActorClip clip,string pose,List<string> notes,string what)
        {
            notes.Add($"в листе нет анимации «{what}» — взят кадр {clip.pose}");
            return new AtlasActorClip {pose=pose,row=clip.row,column=clip.column,count=clip.count,framesPerSecond=clip.framesPerSecond,loop=clip.loop,loopStartFrame=clip.loopStartFrame};
        }

        // The movement hull is the original sX × sY; hit circles: a head at the front and the body (one circle when short).
        private static readonly Dictionary<string,AnatomyDefinition> pendingAnatomy=new(StringComparer.Ordinal);
        private static AnatomyDefinition Anatomy(string id,AtlasActorArtwork art,float width,float height)
        {
            var regions=new List<HitRegionDefinition>();
            float headRadius=height*.53f;
            if(width>=height*1.5f) regions.Add(new HitRegionDefinition {id="head",x=width*.5f-headRadius*.5f,y=height*.6f,radius=headRadius});
            regions.Add(new HitRegionDefinition {id="torso",x=width>=height*1.5f?-width*.12f:0,y=height*.5f,radius=Mathf.Max(height*.5f,width*.3f)});
            foreach(var region in regions)
                region.poses=art.clips.Select(clip=>new HitRegionPose {pose=clip.pose,
                    centers=Enumerable.Repeat(new Vector2(region.x,clip.pose=="down"?Mathf.Min(region.y,height*.4f):region.y),clip.count).ToArray()}).ToArray();
            return new AnatomyDefinition {id=id,atlasArtwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                movement=new MovementHull {width=width,height=height},regions=regions.ToArray()};
        }
        private static void WriteAnatomy(HashSet<string> generated,HashSet<string> owned)
        {
            var json=JObject.Parse(File.ReadAllText(AnatomyPath));var list=(JArray)json["anatomies"];
            foreach(var item in list.OfType<JObject>().ToArray())
            {
                string id=(string)item["id"];
                if(!generated.Contains(id) && !owned.Contains(id)) continue;
                if(!owned.Contains(id)) throw new InvalidOperationException($"Анатомия {id} уже есть и создана не генератором существ; она не заменена.");
                item.Remove();
            }
            foreach(string id in generated.OrderBy(v=>v,StringComparer.Ordinal))
                list.Add(JObject.Parse(Remains.Serialization.StateJson.ToJson(pendingAnatomy[id])));
            File.WriteAllText(AnatomyPath,json.ToString(Newtonsoft.Json.Formatting.None)+"\n");
            AssetDatabase.ImportAsset(AnatomyPath,ImportAssetOptions.ForceUpdate);
            pendingAnatomy.Clear();
        }
        private static void WriteDeathAudio(List<CreatureEntry> entries,HashSet<string> owned)
        {
            var json=JObject.Parse(File.ReadAllText(DeathAudioPath));var list=(JArray)json["entries"];
            var generated=entries.Select(e=>e.actorType).ToHashSet(StringComparer.Ordinal);
            foreach(var item in list.OfType<JObject>().ToArray())
            {
                string type=(string)item["actorType"];
                if(!generated.Contains(type) && !owned.Contains(type)) continue;
                if(!owned.Contains(type)) throw new InvalidOperationException($"Голос смерти {type} задан вручную; он не заменён.");
                item.Remove();
            }
            foreach(var entry in entries)
            {
                var source=sounds.TryGetValue(entry.sourceId,out var clips)?clips:null;
                if(source==null || source.Length==0) continue;
                list.Add(new JObject {["actorType"]=entry.actorType,["sourceBank"]="unit:"+entry.sourceId,["clips"]=new JArray(source),["gain"]=1});
            }
            File.WriteAllText(DeathAudioPath,json.ToString(Newtonsoft.Json.Formatting.Indented)+"\n");
            AssetDatabase.ImportAsset(DeathAudioPath,ImportAssetOptions.ForceUpdate);
        }
        private static readonly Dictionary<string,string[]> sounds=new(StringComparer.Ordinal);

        private static void Guard(string path,string id,HashSet<string> owned)
        {
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null && !owned.Contains(id))
                throw new InvalidOperationException($"Существо {id}: {path} уже существует и создано не генератором; оно не заменено.");
        }
        private static float Num(XElement element,string name,float? fallback=null)
        {
            string value=(string)element?.Attribute(name);
            if(value!=null) return float.Parse(value,CultureInfo.InvariantCulture);
            return fallback??throw new InvalidDataException($"Нет атрибута {name} в {element?.Name}.");
        }
        private static string Hash(string path) {using var stream=File.OpenRead(path);using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    }
}
