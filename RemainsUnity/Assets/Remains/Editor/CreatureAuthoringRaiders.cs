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
    // Raiders (UnitRaider) in the creature pass: the template object "raider" becomes raider1–9 by the room's danger
    // (Location.randomCid) and each variant's weapon by its list (Unit.getXmlWeapon). One actor type per variant and
    // weapon (as the turrets per gun): the raster sheet sprRaiderN with the walk (trot), run, jump, dying and ladder rows,
    // the weapon held at the original's attachment table of the sheet's sex (BlitAnim wPosRaider1 male 130 px cells,
    // wPosRaider2 female 120 px), RaiderBrain settings from AllData (raider + raiderN), the weapon with a full magazine.
    // A variant is a gap when its attack is not implemented (melee, levitated, thrown) or one of its weapons is missing.
    public static partial class CreatureAuthoring
    {
        public const string RaiderClass="UnitRaider";
        private const int RaiderVariantCount=9;   // UnitRaider.kolTrs
        private const string BlitAnim="../../.reference/original/dlc-ui/scripts/fe/serv/BlitAnim.as";
        // The authored airborne frame curve of the raider sheets (RAIDER_SPEC 2026-10-04: +7.2 m/s → 0, 0 → 7, −7.2 → 15).
        private static readonly MotionFramePoint[] RaiderJumpFrames={new() {velocity=-7.2f,frame=15},new() {velocity=0,frame=7},new() {velocity=7.2f,frame=0}};

        private static CreatureObjectRule Raiders(CreatureSources.TemplateObject template,JObject[] rows,Dictionary<string,JObject> resources,string package,
            HashSet<string> owned,List<CreatureEntry> entries)
        {
            var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset).AllAssets()
                .Select(a=>a.CreateDefinition()).ToDictionary(d=>d.id,StringComparer.Ordinal);
            var parent=rows.Single(r=>(string)r["category"]=="unit" && (string)r["id"]=="raider");
            var variants=new List<CreatureVariant>();var types=new List<string>();
            for(int cid=1;cid<=RaiderVariantCount;cid++)
            {
                string unitId="raider"+cid;
                var row=rows.SingleOrDefault(r=>(string)r["category"]=="unit" && (string)r["id"]==unitId)??throw new InvalidDataException("Нет определения "+unitId);
                var own=XElement.Parse((string)row["rawXml"]);
                var unit=Inherit(own,XElement.Parse((string)parent["rawXml"]));
                // The weapon list from the unit itself: merging with the parent would fold repeated elements.
                var list=own.Elements("w").Select(w=>(id:(string)w.Attribute("id"),chance:Num(w,"ch",0),dif:(int)Num(w,"dif",0))).ToArray();
                var variant=new CreatureVariant {unit=unitId,cid=cid,weapons=list.Select(w=>new CreatureWeaponChoice {weapon=w.id,chance=w.chance,difficulty=w.dif}).ToArray()};
                variants.Add(variant);
                variant.gap=RaiderGap(unit,list.Select(w=>w.id),weapons);
                if(variant.gap!=null) continue;
                var look=RaiderArt(unitId,unit,row,resources,package,owned,variant.weapons.Select(c=>RaiderTypeId(unitId,c.weapon)).ToArray());
                foreach(var choice in variant.weapons)
                {
                    var entry=GenerateRaider(unitId,unit,row,parent,choice.weapon,weapons,look,owned);
                    entries.Add(entry);types.Add(entry.actorType);choice.actorType=entry.actorType;
                }
            }
            return new CreatureObjectRule {objectId=template.objectId,actorTypes=types.ToArray(),variantRule=CreatureObjectRule.RaiderVariants,variants=variants.ToArray()};
        }

        // Why a variant is not playable yet: UnitRaider attackerType (weapon tip and comb krep) and the weapons it may get.
        private static string RaiderGap(XElement unit,IEnumerable<string> list,Dictionary<string,WeaponDefinition> weapons)
        {
            var ids=list.ToArray();
            if(ids.Length==0) return "без оружия — рукопашный рейдер ещё не реализован";
            var missing=ids.Where(id=>!weapons.ContainsKey(OriginalWeaponIds.Runtime(id,0))).ToArray();
            if(missing.Length>0) return "нет оружия "+string.Join(", ",missing)+" (рукопашное, метательное или ещё не импортированное)";
            if(ids.Any(id=>weapons[OriginalWeaponIds.Runtime(id,0)].meleeMotion!=null)) return "рукопашный рейдер ещё не реализован";
            if(Num(unit.Element("comb"),"krep",0)!=1) return "оружие удерживается телекинезом (comb krep 0) — его вид у рейдера ещё не реализован";
            return null;
        }

        private static string RaiderTypeId(string unitId,string originalWeapon)=>unitId+"-"+originalWeapon;
        private sealed class RaiderLook {public AtlasActorArtwork art;public AtlasActorVisual prefab;public bool female;}

        // The variant's picture, once for all its weapons: the sheet (hash-checked), the atlas with the attachment
        // table, the prefab. Guarded by the variant's actor types.
        private static RaiderLook RaiderArt(string unitId,XElement unit,JObject row,Dictionary<string,JObject> resources,string package,HashSet<string> owned,string[] typeIds)
        {
            bool female=(string)unit.Element("vis").Attribute("sex")=="w";
            string guardId=typeIds[0];
            var sheet=((JArray)row["graphics"]).OfType<JObject>().Single(g=>(string)g["role"]=="animation-sheet");
            var meta=(JObject)sheet["sheet"];int cell=(int)meta["cellWidth"];
            if((int)meta["cellHeight"]!=cell) throw new InvalidDataException($"Рейдер {unitId}: клетки не квадратные.");
            var file=((JArray)resources[(string)sheet["resource"]]["files"]).OfType<JObject>().Single();
            string input=Path.GetFullPath(Path.Combine(package,(string)file["path"]));
            if(Hash(input)!=(string)file["sha256"]) throw new InvalidDataException($"Рейдер {unitId}: лист {input} не совпадает с SHA256 выгрузки.");
            string folder=ArtFolder+"/"+unitId;Directory.CreateDirectory(folder);
            string texturePath=folder+"/sheet.png";Guard(texturePath,guardId,owned);
            File.Copy(input,texturePath,true);AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType=TextureImporterType.Default;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;
            importer.alphaIsTransparency=true;importer.maxTextureSize=8192;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture.width%cell!=0 || texture.height%cell!=0) throw new InvalidDataException($"Рейдер {unitId}: лист {texture.width}×{texture.height} не делится на клетки {cell}.");

            var sockets=RaiderSockets(female?"wPosRaider2":"wPosRaider1",cell);
            var blits=XElement.Parse("<m>"+(string)sheet["metadataXml"]+"</m>").Elements("blit").ToDictionary(b=>(string)b.Attribute("id"),StringComparer.Ordinal);
            AtlasActorClip Clip(string pose,string blit,bool loop)
            {
                var b=blits.TryGetValue(blit,out var value)?value:throw new InvalidDataException($"Рейдер {unitId}: нет анимации {blit}.");
                int line=(int)Num(b,"y",0),count=(int)Num(b,"len",1),restart=(int)Num(b,"rf",0);
                if(sockets[line].Length<count) throw new InvalidDataException($"Рейдер {unitId}: в таблице оружия ряда {line} {sockets[line].Length} точек на {count} кадров.");
                return new AtlasActorClip {pose=pose,row=line,column=0,count=count,framesPerSecond=FramesPerSecond,loop=loop && count>1,loopStartFrame=loop?restart:0,
                    verticalFrames=blit=="jump"?RaiderJumpFrames.Select(p=>new MotionFramePoint {velocity=p.velocity,frame=p.frame}).ToArray():Array.Empty<MotionFramePoint>(),
                    weaponSockets=sockets[line].Take(count).ToArray()};
            }
            // As the hand-made raider5 atlas (RAIDER_SPEC): walk is the trot row, climbing the ladder row, lying the dying one.
            var clips=new[]{Clip("idle","stay",true),Clip("walk","trot",true),Clip("run","run",true),Clip("jump","jump",false),Clip("down","die",false),Clip("climb","laz",true)};
            var art=new AtlasActorArtwork {resource=ArtFolder.Substring("Assets/Remains/Resources/".Length)+"/"+unitId+"/sheet",width=texture.width,height=texture.height,
                cellWidth=cell,cellHeight=cell,pixelsPerUnit=PixelsPerMetre,pivotX=(float)meta["originX"]/cell,pivotY=1-(float)meta["originY"]/cell,facesLeft=false,clips=clips};
            foreach(var clip in clips) AtlasWeaponSockets.ValidateClip(clip,unitId,required:true);
            string atlasPath=folder+"/artwork.asset";
            var atlas=AssetDatabase.LoadAssetAtPath<AtlasSpriteAsset>(atlasPath);
            if(atlas==null) {atlas=ScriptableObject.CreateInstance<AtlasSpriteAsset>();atlas.Configure(texture,art);AssetDatabase.CreateAsset(atlas,atlasPath);}
            else atlas.Configure(texture,art);
            AtlasSpriteAssetEditor.Rebuild(atlas);

            Directory.CreateDirectory(PrefabFolder);
            string prefabPath=PrefabFolder+"/"+unitId+".prefab",materialPath=PrefabFolder+"/"+unitId+".mat";Guard(prefabPath,guardId,owned);
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,materialPath);}
            var root=new GameObject(unitId);
            try
            {
                var renderer=root.AddComponent<SpriteRenderer>();renderer.sprite=atlas.Frames("idle")[0];renderer.sortingOrder=20;renderer.sharedMaterial=material;
                var visual=root.AddComponent<AtlasActorVisual>();visual.SetRenderer(renderer);
                visual.ConfigureDeadPresentation(false,0,new Color(.55f,.55f,.55f,1));
                return new RaiderLook {art=art,female=female,prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath).GetComponent<AtlasActorVisual>()};
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }

        private static CreatureEntry GenerateRaider(string unitId,XElement unit,JObject row,JObject parent,string originalWeapon,Dictionary<string,WeaponDefinition> weapons,
            RaiderLook look,HashSet<string> owned)
        {
            var phis=unit.Element("phis");var move=unit.Element("move");var comb=unit.Element("comb");var un=unit.Element("un");
            float width=Num(phis,"sX")/PixelsPerMetre,height=Num(phis,"sY")/PixelsPerMetre,mass=Num(phis,"massa",1);
            float hp=Num(comb,"hp"),armor=Num(comb,"armor",0),vision=Num(comb,"vision",1);
            var art=look.art;bool female=look.female;
            string id=RaiderTypeId(unitId,originalWeapon),weapon=OriginalWeaponIds.Runtime(originalWeapon,0);
            var definition=weapons[weapon];
            int burst=(int)Num(un,"och",0);
            if(burst==0 && definition.magazineSize>10) burst=Mathf.RoundToInt(definition.magazineSize/2f);   // UnitRaider: holder > 10 → holder / 2
            var raider=new RaiderSettings
            {
                attacker=RaiderAttacker.Ranged,
                walkSpeed=un?.Attribute("walker")!=null?2.5f*FramesPerSecond/PixelsPerMetre:Num(move,"speed")*FramesPerSecond/PixelsPerMetre,
                jumpSpeed=Num(move,"jump",15)*FramesPerSecond/PixelsPerMetre,acceleration=Num(move,"accel",5)*FramesPerSecond/PixelsPerMetre,
                holdDistance=Num(un,"dist",1000)/PixelsPerMetre,burstFrames=burst,weaponInTeeth=true,stroll=un?.Attribute("stay")==null,
                climbsLadders=un?.Attribute("flyer")==null,slowness=40
            };
            var type=new ActorTypeDefinition
            {
                id=id,presentation="atlas",artwork=art.resource.Substring(0,art.resource.Length-"sheet".Length)+"artwork",
                combatAi=true,hasWeapon=true,atlasWeaponSocket=true,bodyInventory=true,canUseDoors=true,bodyMassKg=mass,telekinesisMovable=true,
                // Unit vision × 1000 px; the hand-made raider's eye and observed heights, scaled by the sheet's height.
                perception=new NpcPerceptionSettings {sightDistance=vision*1000/PixelsPerMetre,closeDistance=1.6f,recognitionSeconds=.3f,memorySeconds=4,
                    hearingDistance=vision*1000/PixelsPerMetre,eyeHeight=height*.64f,targetHeight=.7f},
                behavior=NpcCreatureBehaviours.Raider,raider=raider,hitMaterial="flesh",hitProtection=armor,
                activateTrap=(int)Num(unit.Element("param"),"acttrap",2)
            };
            string actorPath=ActorFolder+"/"+id+".asset";Guard(actorPath,id,owned);
            var asset=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(actorPath);bool created=asset==null;
            if(created) asset=ScriptableObject.CreateInstance<ActorTypeAsset>();
            asset.SetDefinition(type);

            asset.SetPrefab(look.prefab);
            if(created) AssetDatabase.CreateAsset(asset,actorPath);
            EditorUtility.SetDirty(asset);
            var entities=EntityAssetCatalog.Load();entities.AddActor(asset);EditorUtility.SetDirty(entities);

            // The weapon starts full (Weapon.hold = holder). The original's units reload without ammunition; the port's
            // NPCs reload from their inventory, so they carry one to two magazines more, as the hand-made raider.
            Directory.CreateDirectory(SpawnFolder);
            string inventoryPath=SpawnFolder+"/"+id+"-inventory.asset";Guard(inventoryPath,id,owned);
            var inventory=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>(inventoryPath);bool fresh=inventory==null;
            if(fresh) inventory=ScriptableObject.CreateInstance<ContainerLootAsset>();
            var lines=new List<ContainerLootEntry> {new() {id="weapon",definition=weapon,minimum=1,maximum=1,weaponState=new WeaponItemState {rounds=definition.magazineSize}}};
            if(!string.IsNullOrEmpty(definition.ammo)) lines.Add(new ContainerLootEntry {id="ammo",definition=definition.ammo,minimum=definition.magazineSize,maximum=definition.magazineSize*2});
            inventory.Configure(new ContainerLootTable {id=id+"-weapon",version="1",entries=lines.ToArray()});
            if(fresh) AssetDatabase.CreateAsset(inventory,inventoryPath);else EditorUtility.SetDirty(inventory);
            string spawnPath=SpawnFolder+"/"+id+"-spawn.asset";Guard(spawnPath,id,owned);
            var spawn=AssetDatabase.LoadAssetAtPath<NpcSpawnAsset>(spawnPath);bool newSpawn=spawn==null;
            if(newSpawn) spawn=ScriptableObject.CreateInstance<NpcSpawnAsset>();
            spawn.Configure(id,id,hp,weapon);spawn.SetInventory(inventory);
            if(newSpawn) AssetDatabase.CreateAsset(spawn,spawnPath);else EditorUtility.SetDirty(spawn);

            pendingAnatomy[id]=Anatomy(id,art,width,height);
            sounds[unitId]=new[]{row,parent}.SelectMany(r=>((JArray)r["sounds"]).OfType<JObject>())
                .Where(s=>((string)s["role"]??"").StartsWith("die",StringComparison.Ordinal)).Select(s=>"exported:"+(string)s["resource"]).Distinct().ToArray();
            // LootGen.lootDrop "raider" rolls items by category (eda 0.25, ammo 0.25, explosives 0.12); the creature loot tables
            // carry fixed items only, so the body keeps just its inventory for now.
            const string LootGap="добыча raider (случайные еда/патроны/взрывчатка по категориям LootGen) не перенесена — у тела только его инвентарь";
            Debug.LogWarning($"Рейдер {id}: {LootGap}.");
            return new CreatureEntry {sourceId=unitId,actorType=id,family=NpcCreatureBehaviours.Raider,spawn=spawn,
                notes=new[]{"оружие "+weapon,$"таблица оружия {(female?"wPosRaider2":"wPosRaider1")}",LootGap,"броня comb armor как поглощение; aqual, armorhp, vulner, dexter, skill не перенесены"}};
        }

        // BlitAnim.wPosRaiderN: per sheet row, the weapon point of each frame (cell pixels, x right, y down). Numbers only:
        // the literal is read as JSON, anything else is rejected.
        private static readonly Dictionary<string,AtlasWeaponSocketPoint[][]> socketTables=new(StringComparer.Ordinal);
        private static AtlasWeaponSocketPoint[][] RaiderSockets(string table,int cell)
        {
            if(!socketTables.TryGetValue(table,out var rows))
            {
                string text=File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,BlitAnim)));
                string marker=$"public static var {table}:Array = ";
                int start=text.IndexOf(marker,StringComparison.Ordinal);
                if(start<0) throw new InvalidDataException("Нет таблицы "+table+" в BlitAnim.as.");
                start+=marker.Length;int depth=0,end=-1;
                for(int i=start;i<text.Length && end<0;i++) {if(text[i]=='[') depth++;else if(text[i]==']' && --depth==0) end=i+1;}
                var array=JArray.Parse(text.Substring(start,end-start));
                rows=array.Select(r=>((JArray)r).Select(p=>new AtlasWeaponSocketPoint {x=(float)p["x"],y=(float)p["y"]}).ToArray()).ToArray();
                socketTables[table]=rows;
            }
            if(rows.SelectMany(r=>r).Any(p=>p.x<0 || p.y<0 || p.x>cell || p.y>cell)) throw new InvalidDataException($"Таблица {table} выходит за клетку {cell} px.");
            return rows;
        }
    }
}
