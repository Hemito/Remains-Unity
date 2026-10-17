using System;
using System.Collections.Generic;
using System.Globalization;
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
    // Turrets (UnitTurret) in the creature pass: a fixed gun on the shared weapon AI. Its picture is an atlas built from
    // the export's parts (CatalogBuilder.Parts): the base drawn without the gun, with the state lamp of each AI state as
    // one row (UnitTurret.animate: light frame aiState + 1 — 1 off, 2 watching, 4 attacking); the gun is the turret's
    // weapon (FirearmAuthoring: frame tr of the part "puha"), held at the part's placement (atlas weapon socket) and
    // turned to the aim. One actor type per gun the original may give a factory turret (tr 1–3: the level is below 10),
    // each with its gun as initial inventory.
    public static partial class CreatureAuthoring
    {
        private static readonly int[] TurretGuns={1,2,3};
        private const int LampOff=1,LampWatching=2,LampNoticed=3,LampAttacking=4,LampSearching=5;

        // UnitTurret.putLoc by turret tip: watch angles (aRot, y down → y up), watch turn rate (watchDrot rad per frame),
        // the attack rate actDrot 0.1 rad per frame for all; period 120 frames; the armored turret keeps its side.
        private static TurretSettings TurretWatch(string unitId)
        {
            const float PerFrame=FramesPerSecond*Mathf.Rad2Deg;
            var settings=new TurretSettings {periodSeconds=120/FramesPerSecond,attackTurnDegreesPerSecond=.1f*PerFrame,
                alertMinSeconds=30/FramesPerSecond,alertMaxSeconds=40/FramesPerSecond,searchSeconds=30*10/FramesPerSecond};
            switch(unitId)
            {
                case "turret0": settings.patrolAngles=new[]{-30f,-150f};settings.watchTurnDegreesPerSecond=.015f*PerFrame;break;
                case "turret1": settings.patrolAngles=new[]{15f,165f};settings.watchTurnDegreesPerSecond=.1f*PerFrame;break;
                case "turret3": settings.patrolAngles=new[]{15f,-15f};settings.watchTurnDegreesPerSecond=.01f*PerFrame;settings.fixedFacing=true;break;
                default: throw new InvalidDataException("Наблюдение турели не описано: "+unitId);
            }
            return settings;
        }
        // The gun starts full (UnitTurret: currentWeapon.hold = holder).
        private static int WeaponCatalogMagazine(string weapon)=>
            AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset).AllAssets().Single(a=>a.Id==weapon).CreateDefinition().magazineSize;

        private static string Turrets(CreatureSources.TemplateObject template,JObject[] rows,Dictionary<string,JObject> resources,string package,
            HashSet<string> owned,List<CreatureEntry> entries,List<string> types)
        {
            foreach(string unitId in template.units)
            {
                var made=entries.Where(e=>e.sourceId==unitId).ToArray();
                if(made.Length>0) {types.AddRange(made.Select(e=>e.actorType));continue;}
                var row=rows.FirstOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==unitId);
                if(row==null) return "нет определения unit "+unitId+" в выгрузке";
                var unit=XElement.Parse((string)row["rawXml"]);
                var graphics=((JArray)row["graphics"]).OfType<JObject>().ToArray();
                JObject Part(string role)=>graphics.FirstOrDefault(g=>(string)g["role"]==role);
                var appearance=Part("appearance");var gun=Part("part:puha");var lamp=Part("part:light");
                if(appearance==null || gun==null || lamp==null) return $"у турели {unitId} нет основания или частей puha/light в выгрузке";
                var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset).AllAssets().Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
                string missing=TurretGuns.Select(tr=>$"weapon:turret{tr}:grade:0").FirstOrDefault(id=>!weapons.Contains(id));
                if(missing!=null) return $"нет оружия турели {missing} (FirearmAuthoring)";
                var dies=((JArray)row["sounds"]).OfType<JObject>().Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal))
                    .Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
                foreach(var entry in GenerateTurret(unitId,unit,appearance,gun,lamp,dies,resources,package,owned)) {entries.Add(entry);types.Add(entry.actorType);}
            }
            return null;
        }

        private static List<CreatureEntry> GenerateTurret(string unitId,XElement unit,JObject appearance,JObject gun,JObject lamp,string[] dies,
            Dictionary<string,JObject> resources,string package,HashSet<string> owned)
        {
            var exported=ExportedContentCatalog.Load();
            var phis=unit.Element("phis");var comb=unit.Element("comb");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1);
            var gunPlace=XElement.Parse((string)gun["metadataXml"]);var lampPlace=XElement.Parse((string)lamp["metadataXml"]);
            if(Num(gunPlace,"scale")!=1 || Num(lampPlace,"scale")!=1) throw new InvalidDataException($"Турель {unitId}: части масштабированы; сборка листа их не поддерживает.");

            // Pictures at the export's largest scale, read from the package (imported sprites give the registration).
            (Color32[] pixels,int w,int h,Vector2 pivot,float zoom) Picture(string resource,int frame)
            {
                var entry=exported.Resource(resource).frames.Single(f=>f.frame==frame);var sprite=exported.FrameSprite(entry);
                var file=((JArray)resources[resource]["files"]).OfType<JObject>().Where(f=>((string)f["path"]).EndsWith("/"+frame+".png",StringComparison.Ordinal) ||
                    ((string)f["path"]).EndsWith("/"+frame+"@8.png",StringComparison.Ordinal)).OrderByDescending(f=>(double)f["zoom"]).First();
                string path=Path.GetFullPath(Path.Combine(package,(string)file["path"]));
                if(Hash(path)!=(string)file["sha256"]) throw new InvalidDataException($"Турель {unitId}: {path} не совпадает с SHA256 выгрузки.");
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if(!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("Не прочитан "+path);
                    if(texture.width!=(int)sprite.rect.width || texture.height!=(int)sprite.rect.height)
                        throw new InvalidDataException($"Турель {unitId}: {path} {texture.width}×{texture.height} не совпадает с импортированным кадром.");
                    return (texture.GetPixels32(),texture.width,texture.height,sprite.pivot,sprite.pixelsPerUnit/PixelsPerMetre);
                }
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
            var basePicture=Picture((string)appearance["resource"],1);float zoom=basePicture.zoom;
            int lampFrames=(int)Num(lampPlace,"frames");
            if(lampFrames<LampSearching) throw new InvalidDataException($"Турель {unitId}: у лампы {lampFrames} кадров, нужно не меньше {LampSearching}.");
            var lamps=Enumerable.Range(1,lampFrames).Select(f=>Picture((string)lamp["resource"],f)).ToArray();
            if(lamps.Any(l=>Mathf.Abs(l.zoom-zoom)>.0001f)) throw new InvalidDataException($"Турель {unitId}: лампа и основание выгружены в разном масштабе.");
            // Placements in pixels at this scale from the registration, y up (the export's are source pixels, y down).
            var lampOffset=new Vector2(Num(lampPlace,"x")*zoom,-Num(lampPlace,"y")*zoom);var gunOffset=new Vector2(Num(gunPlace,"x")*zoom,-Num(gunPlace,"y")*zoom);
            // The cell holds the pictures, the registration (the unit's feet, the atlas pivot) and the gun's mount.
            float minX=Mathf.Min(0,Mathf.Min(-basePicture.pivot.x,gunOffset.x)),minY=Mathf.Min(0,Mathf.Min(-basePicture.pivot.y,gunOffset.y));
            float maxX=Mathf.Max(0,Mathf.Max(basePicture.w-basePicture.pivot.x,gunOffset.x)),maxY=Mathf.Max(0,Mathf.Max(basePicture.h-basePicture.pivot.y,gunOffset.y));
            foreach(var l in lamps)
            {
                minX=Mathf.Min(minX,lampOffset.x-l.pivot.x);minY=Mathf.Min(minY,lampOffset.y-l.pivot.y);
                maxX=Mathf.Max(maxX,lampOffset.x+l.w-l.pivot.x);maxY=Mathf.Max(maxY,lampOffset.y+l.h-l.pivot.y);
            }
            int cellX=Mathf.CeilToInt(maxX-minX),cellY=Mathf.CeilToInt(maxY-minY);var origin=new Vector2(Mathf.Round(-minX),Mathf.Round(-minY));
            var sheet=new Color32[cellX*cellY*lampFrames];
            // One row, a column per lamp frame (a tall turret's column would exceed the texture size limit).
            int sheetWidth=cellX*lampFrames;
            void Draw((Color32[] pixels,int w,int h,Vector2 pivot,float zoom) picture,Vector2 at,int column)
            {
                int ox=Mathf.RoundToInt(origin.x+at.x-picture.pivot.x),oy=Mathf.RoundToInt(origin.y+at.y-picture.pivot.y);
                for(int y=0;y<picture.h;y++) for(int x=0;x<picture.w;x++)
                {
                    int dx=ox+x,dy=oy+y;if(dx<0 || dy<0 || dx>=cellX || dy>=cellY) continue;
                    var s=picture.pixels[y*picture.w+x];if(s.a==0) continue;
                    int i=dy*sheetWidth+column*cellX+dx;var d=sheet[i];float a=s.a/255f,keep=1-a;
                    sheet[i]=new Color32((byte)(s.r*a+d.r*keep),(byte)(s.g*a+d.g*keep),(byte)(s.b*a+d.b*keep),(byte)Mathf.Min(255,s.a+d.a*keep));
                }
            }
            for(int column=0;column<lampFrames;column++) {Draw(basePicture,Vector2.zero,column);Draw(lamps[column],lampOffset,column);}

            string folder=ArtFolder+"/"+unitId;Directory.CreateDirectory(folder);
            string texturePath=folder+"/sheet.png";Guard(texturePath,unitId+"-"+TurretGuns[0],owned);
            var composed=new Texture2D(sheetWidth,cellY,TextureFormat.RGBA32,false);
            try {composed.SetPixels32(sheet);composed.Apply();File.WriteAllBytes(texturePath,composed.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(composed);}
            AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;
            importer.alphaIsTransparency=true;importer.maxTextureSize=8192;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var socket=new AtlasWeaponSocketPoint {x=origin.x+gunOffset.x,y=cellY-(origin.y+gunOffset.y)};
            AtlasActorClip Clip(string pose,int lampFrame)=>new() {pose=pose,row=0,column=lampFrame-1,count=1,framesPerSecond=FramesPerSecond,
                weaponSockets=new[]{new AtlasWeaponSocketPoint {x=socket.x,y=socket.y}}};
            var clips=new[]{Clip("idle",LampWatching),Clip("walk",LampWatching),Clip("run",LampWatching),Clip("jump",LampWatching),
                Clip(Remains.Gameplay.NpcTurret.Alert,LampNoticed),Clip("attack",LampAttacking),Clip(Remains.Gameplay.NpcTurret.Search,LampSearching),Clip("down",LampOff)};
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+unitId+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cellX,cellHeight=cellY,pixelsPerUnit=PixelsPerMetre*zoom,pivotX=origin.x/cellX,pivotY=origin.y/cellY,facesLeft=false,clips=clips};
            Debug.Log($"Турель {unitId}: лист {texture.width}×{texture.height} (собран {sheetWidth}×{cellY}), клетка {cellX}×{cellY}, опора ({origin.x},{origin.y}) → ({art.pivotX:F3},{art.pivotY:F3}), ствол ({socket.x:F1},{socket.y:F1}), масштаб {art.pixelsPerUnit}.");
            string atlasPath=folder+"/artwork.asset";
            var atlas=AssetDatabase.LoadAssetAtPath<AtlasSpriteAsset>(atlasPath);
            if(atlas==null) {atlas=ScriptableObject.CreateInstance<AtlasSpriteAsset>();atlas.Configure(texture,art);AssetDatabase.CreateAsset(atlas,atlasPath);}
            else atlas.Configure(texture,art);
            AtlasSpriteAssetEditor.Rebuild(atlas);

            Directory.CreateDirectory(PrefabFolder);
            string prefabPath=PrefabFolder+"/"+unitId+".prefab",materialPath=PrefabFolder+"/"+unitId+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,materialPath);}
            AtlasActorVisual prefab;
            var root=new GameObject(unitId);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=atlas.Frames("idle")[0];renderer.sortingOrder=20;renderer.sharedMaterial=material;
                var visual=root.AddComponent<AtlasActorVisual>();visual.SetRenderer(renderer);
                visual.ConfigureDeadPresentation(false,0,new Color(.55f,.55f,.55f,1));
                prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath).GetComponent<AtlasActorVisual>();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}

            var result=new List<CreatureEntry>();
            float eye=gunOffset.y/zoom/PixelsPerMetre;
            foreach(int tr in TurretGuns)
            {
                string id=unitId+"-"+tr,weapon=$"weapon:turret{tr}:grade:0";
                var definition=new ActorTypeDefinition
                {
                    id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                    combatAi=true,hasWeapon=true,atlasWeaponSocket=true,bodyInventory=false,canUseDoors=false,bodyMassKg=mass,telekinesisMovable=false,
                    // It sees the side its gun points to (NpcTurret turns it), within vision × 1000 px, and behind only within
                    // Unit.detecting (80 px); the armored turret (noTurn, narrow cone) never behind.
                    perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=unitId=="turret3"?0:80/PixelsPerMetre,recognitionSeconds=.3f,
                        memorySeconds=4,hearingDistance=vision*1000/PixelsPerMetre,eyeHeight=Mathf.Max(0,eye),targetHeight=.25f},
                    behavior=NpcCreatureBehaviours.Turret,hitMaterial="metal",hitProtection=armor,turret=TurretWatch(unitId),deathBlastScale=.5f,
                    activateTrap=(int)Num(unit.Element("param"),"acttrap",2)
                };
                string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
                var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=type==null;
                if(created) type=ScriptableObject.CreateInstance<ActorTypeAsset>();
                type.SetDefinition(definition);type.SetPrefab(prefab);
                if(created) AssetDatabase.CreateAsset(type,actorPath);
                EditorUtility.SetDirty(type);
                var entities=EntityAssetCatalog.Load();entities.AddActor(type);EditorUtility.SetDirty(entities);

                Directory.CreateDirectory(SpawnFolder);
                string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
                var inventory=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(inventoryPath);bool fresh=inventory==null;
                if(fresh) inventory=ScriptableObject.CreateInstance<ContainerLootAsset>();
                inventory.Configure(new ContainerLootTable {id=id+"-gun",version="1",entries=new[]{new ContainerLootEntry {id="gun",definition=weapon,minimum=1,maximum=1,
                    weaponState=new WeaponItemState {rounds=WeaponCatalogMagazine(weapon)}}}});
                if(fresh) AssetDatabase.CreateAsset(inventory,inventoryPath);else EditorUtility.SetDirty(inventory);
                string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
                var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
                if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
                spawn.Configure(id,id,hp,weapon);spawn.SetInventory(inventory);
                if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);

                pendingAnatomy[id]=Anatomy(id,art,width,height);
                result.Add(new CreatureEntry {sourceId=unitId,actorType=id,family=NpcCreatureBehaviours.Turret,spawn=spawn,
                    notes=new[]{$"ствол turret{tr}","добыча (cont) не перенесена","поворот ствола между углами наблюдения, прячущиеся турели и лампа «тревога/поиск» не перенесены"}});
            }
            sounds[unitId]=dies;
            return result;
        }
    }
}
