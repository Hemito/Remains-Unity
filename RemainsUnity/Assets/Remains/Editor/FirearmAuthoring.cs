using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The original's firearms and energy weapons (AllData weapon tip 2/3) from the export in one pass, no per-weapon
    // hand authoring: every grade with its own item image becomes a working WeaponCatalog definition whose ID is the
    // formal item ID (weapon:<id>:grade:<n>), with
    //   numbers   Weapon.getCharParam/getPhisParam/getAmmoParam (grade elements over the base): damage, rapid → cooldown,
    //             ammo holder/reload/rashod, phis speed (px/frame → m/s), deviation (±deviation/2°), Bullet.liv 100
    //             frames, speed ×0.8–1.2 (Weapon.shoot), recoil scaled from the pistol's calibration by recoil/5;
    //   artwork   the appearance symbol: frame 1 at rest, the "shoot" label's frames as the shot, the "reload" label's
    //             frames as the reload, the "emit" socket as the muzzle; the projectile symbol as the trace (stretched
    //             as Bullet: native length × speed / 100) and its later frames as the impact, or a beam (vis spring 2+);
    //             the casing symbol when vis shell is set;
    //   sounds    snd shoot and reload through the combat sound table.
    // Kinds the generic projectile does not cover yet are listed as gaps: explosive shells (damexpl), several pellets
    // (kol), arcing shots (grav), flames, spin-up (prep). The hand-made pistol (p10mm) and laser pistol (lasp) keep
    // their grade 0. Generated assets live in "Original" folders and are replaced on every run.
    //   batch -executeMethod Remains.Editor.FirearmAuthoring.RunBatch
    public static class FirearmAuthoring
    {
        private const string WeaponFolder="Assets/Remains/Resources/Content/weapons/Original";
        private const string PrefabFolder="Assets/Remains/Prefabs/Weapons/Original";
        private const string ShotFolder="Assets/Remains/Resources/Presentation/Shots/Original";
        private const string CasingFolder="Assets/Remains/Prefabs/Weapons/ShotEffects/Original";
        private const string CombatAudioPath="Assets/Remains/Resources/Presentation/combat-audio.json";
        private const float PixelsPerMetre=50,FramesPerSecond=30,BulletLifeFrames=100,SpeedVariation=.2f,PistolRecoil=5;
        private static readonly Dictionary<string,int> handMade=new(StringComparer.Ordinal) {{"p10mm",0},{"lasp",0}};
        private static readonly HashSet<string> excluded=new(StringComparer.Ordinal) {"test"};

        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        // Reapply only the material rule to existing assets: leave prefab/clip/domain authoring untouched.
        public static void RegenerateSpriteMaterials()
        {
            int count=0;
            foreach(string path in Directory.GetFiles(PrefabFolder,"*.mat",SearchOption.TopDirectoryOnly).Select(path=>path.Replace('\\','/')))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null || material.shader==null || material.shader.name!="Remains/WorldLit")
                    throw new InvalidOperationException("Не подготовлен материал оружия WorldLit: "+path);
                material.mainTexture=null;EditorUtility.SetDirty(material);count++;
            }
            if(count==0) throw new InvalidOperationException("Не найдены материалы оружия: "+PrefabFolder);
            AssetDatabase.SaveAssets();
            Debug.Log($"Материалы оружия: восстановлено {count}; _MainTex задаёт SpriteRenderer, ссылки на PNG убраны.");
        }

        private sealed class Shot {public string resource;public bool beam;public float speed,length;public readonly List<string> weapons=new();}

        public static void Run()
        {
            var catalog=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var resources=((JArray)catalog["resources"]).OfType<JObject>().ToDictionary(r=>(string)r["id"],StringComparer.Ordinal);
            var exported=ExportedContentCatalog.Load();
            var weapons=AssetDatabase.LoadAssetAtPath<WeaponAssetCatalog>(MineContentAuthoring.CatalogAsset)??throw new InvalidOperationException("Нет каталога оружия.");
            foreach(string folder in new[]{WeaponFolder,PrefabFolder,ShotFolder,CasingFolder}) EnsureFolder(folder);
            var serialized=new SerializedObject(weapons);var list=serialized.FindProperty("weapons");
            var shots=new Dictionary<string,Shot>(StringComparer.Ordinal);var casings=new Dictionary<string,CasingEffectProfile>(StringComparer.Ordinal);
            var audio=new List<JObject>();var generated=new List<string>();var gaps=new List<string>();var assets=new HashSet<string>(StringComparer.Ordinal);

            foreach(var row in ((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="weapon").OrderBy(r=>(string)r["id"],StringComparer.Ordinal))
            {
                string wid=(string)row["id"];var root=XElement.Parse((string)row["rawXml"]);
                if((string)root.Attribute("tip") is not ("2" or "3") || excluded.Contains(wid)) continue;
                var graphics=((JArray)row["graphics"]).OfType<JObject>().ToArray();var sounds=((JArray)row["sounds"]).OfType<JObject>().ToArray();
                int grades=Math.Max(1,root.Elements("char").Count());
                for(int grade=0;grade<grades;grade++)
                {
                    if(handMade.TryGetValue(wid,out int kept) && kept==grade) continue;
                    string id=$"weapon:{wid}:grade:{grade}";
                    string reason=Generate(id,wid,grade,root,graphics,sounds,resources,exported,list,shots,casings,audio,assets);
                    if(reason==null) generated.Add(id);else gaps.Add($"{id}: {reason}");
                }
            }

            // Turret guns: the gun art is the turrets' shared part "puha" (CatalogBuilder.Parts).
            string puha=((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="unit")
                .SelectMany(r=>((JArray)r["graphics"]).OfType<JObject>()).FirstOrDefault(g=>(string)g["role"]=="part:puha")?["resource"]?.ToString();
            foreach(var row in ((JArray)catalog["definitions"]).OfType<JObject>().Where(r=>(string)r["category"]=="weapon").OrderBy(r=>(string)r["id"],StringComparer.Ordinal))
            {
                string wid=(string)row["id"];var root=XElement.Parse((string)row["rawXml"]);
                if((string)root.Attribute("tip")!="0" || !wid.StartsWith("turret",StringComparison.Ordinal) || (string)root.Element("vis")?.Attribute("vweap")!="viswturret") continue;
                string id=$"weapon:{wid}:grade:0";
                string reason=puha==null?"нет части puha у турелей в выгрузке":TurretGun(wid,root,((JArray)row["graphics"]).OfType<JObject>().ToArray(),
                    ((JArray)row["sounds"]).OfType<JObject>().ToArray(),puha,resources,exported,list,shots,audio,assets);
                if(reason==null) generated.Add(id);else gaps.Add($"{id}: {reason}");
            }

            // Shot artwork per projectile symbol, beam and trace speed (the trace length follows the speed, as Bullet).
            foreach(var shot in shots.Values)
            {
                string name=Safe(shot.resource)+(shot.beam?"-beam":"-"+Mathf.RoundToInt(shot.speed));
                string path=ShotFolder+"/"+name+".asset";assets.Add(path);
                var frames=Frames(exported,shot.resource);
                var trace=Sprite.Create(frames[0].texture,frames[0].rect,new Vector2(.5f,.5f),frames[0].pixelsPerUnit,0,SpriteMeshType.FullRect);
                CatalogSpriteReference traceReference;
                try {traceReference=CatalogSpriteAuthoring.Reference(trace);}
                finally {UnityEngine.Object.DestroyImmediate(trace);}
                var artwork=AssetDatabase.LoadAssetAtPath<ShotArtwork>(path);bool created=artwork==null;
                if(created) artwork=ScriptableObject.CreateInstance<ShotArtwork>();
                artwork.weapons=shot.weapons.ToArray();artwork.beam=shot.beam;
                artwork.speed=shot.speed;artwork.length=shot.length;artwork.lifetime=4f/FramesPerSecond;artwork.impactFramesPerSecond=FramesPerSecond;
                artwork.ConfigureReferences(traceReference,
                    (shot.beam?Array.Empty<Sprite>():frames.Skip(1).ToArray()).Select(CatalogSpriteAuthoring.Reference).ToArray());
                artwork.Validate();
                if(created) AssetDatabase.CreateAsset(artwork,path);else EditorUtility.SetDirty(artwork);
            }
            // Generated assets of weapons that are no longer generated are removed (the folders belong to this tool).
            foreach(string folder in new[]{WeaponFolder,PrefabFolder,ShotFolder,CasingFolder})
                foreach(string guid in AssetDatabase.FindAssets("",new[]{folder}))
                {
                    string path=AssetDatabase.GUIDToAssetPath(guid);
                    if(!AssetDatabase.IsValidFolder(path) && !assets.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
            for(int i=list.arraySize-1;i>=0;i--) if(list.GetArrayElementAtIndex(i).objectReferenceValue==null) list.DeleteArrayElementAtIndex(i);
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(weapons);
            AssetDatabase.SaveAssets();
            WriteAudio(audio);
            foreach(string gap in gaps) Debug.Log("Оружие не подключено: "+gap);
            Debug.Log($"Огнестрел оригинала: подключено {generated.Count} определений ({generated.Select(g=>g.Split(':')[1]).Distinct().Count()} моделей), "+
                $"не подключено {gaps.Count}; графика выстрелов {shots.Count}, гильз {casings.Count}.");
        }

        private static string Generate(string id,string wid,int grade,XElement root,JObject[] graphics,JObject[] sounds,Dictionary<string,JObject> resources,
            ExportedContentCatalog exported,SerializedProperty list,Dictionary<string,Shot> shots,Dictionary<string,CasingEffectProfile> casings,List<JObject> audio,HashSet<string> assets)
        {
            // Weapon.getXmlParam: each element of the grade over the base one.
            XElement Merged(string name)
            {
                var all=root.Elements(name).ToArray();if(all.Length==0) return new XElement(name);
                var merged=new XElement(all[0]);
                if(grade>0 && grade<all.Length) foreach(var attribute in all[grade].Attributes()) merged.SetAttributeValue(attribute.Name,attribute.Value);
                return merged;
            }
            var character=Merged("char");var phis=Merged("phis");var vis=Merged("vis");var ammoNode=Merged("ammo");var snd=Merged("snd");
            if(Num(character,"damexpl",0)>0) return "взрывающийся снаряд (damexpl)";
            if(phis.Attribute("grav")!=null) return "навесной снаряд (grav)";
            if(phis.Attribute("flame")!=null) return "огнемёт (flame)";
            if(character.Attribute("prep")!=null) return "раскрутка перед стрельбой (prep)";
            string ammo=((string)root.Element("a"))?.Trim();
            if(string.IsNullOrEmpty(ammo) || ammoNode.Attribute("holder")==null || ammoNode.Attribute("reload")==null) return "нет патронов или магазина (a, ammo holder/reload)";
            if(!exported.TryItem("item:"+ammo,out _)) return "патрон "+ammo+" не импортирован";
            string Binding(JObject[] bindings,string role)=>bindings.FirstOrDefault(b=>(string)b["role"]==role)?["resource"]?.ToString();
            string appearance=Binding(graphics,"appearance-grade-"+grade),projectile=Binding(graphics,"projectile-grade-"+grade)??Binding(graphics,"projectile-grade-0");
            if(appearance==null || Binding(graphics,"item-image-grade-"+grade)==null) return "нет вида или изображения предмета этого грейда";
            if(projectile==null) return "нет вида снаряда";
            if(!exported.TryItem(id,out _)) return "предмет не импортирован";
            string casingResource=vis.Attribute("shell")!=null?Binding(graphics,"casing-grade-"+grade)??Binding(graphics,"casing-grade-0"):null;
            Sprite[] frames,projectileFrames,casingFrames=null;
            try {frames=Frames(exported,appearance);projectileFrames=Frames(exported,projectile);if(casingResource!=null) casingFrames=Frames(exported,casingResource);}
            catch(InvalidDataException error) {return error.Message;}
            var source=resources[appearance];
            var emit=((JArray)source["weaponSockets"]??new JArray()).OfType<JObject>().FirstOrDefault(s=>(int)s["frame"]==1 && (string)s["name"]=="emit");
            if(emit==null) return "нет точки вылета (emit) у вида "+appearance;
            string shootSound=Binding(sounds,"shoot"),reloadSound=Binding(sounds,"reload");
            if(shootSound==null) return "нет звука выстрела";

            float speed=Num(phis,"speed")*FramesPerSecond/PixelsPerMetre,lifetime=BulletLifeFrames/FramesPerSecond;
            float recoil=Num(phis,"recoil",0)/PistolRecoil;
            var definition=new WeaponDefinition
            {
                id=id,damage=Num(character,"damage"),cooldown=Num(character,"rapid")/FramesPerSecond,ammo=Num(ammoNode,"recharg",0)>0?null:"item:"+ammo,
                rechargeSeconds=Num(ammoNode,"recharg",0)/FramesPerSecond,
                magazineSize=(int)Num(ammoNode,"holder"),ammoPerShot=(int)Num(ammoNode,"rashod",1),reloadSeconds=Mathf.Max(1,Num(ammoNode,"reload"))/FramesPerSecond,   // reload 0 (buckshot): one frame, the shortest the rules allow
                projectileSpeed=speed,projectileSpeedVariation=SpeedVariation,projectileLifetime=lifetime,range=speed*lifetime,
                shotSpreadHalfAngleDegrees=Mathf.Min(90,Num(phis,"deviation",0)*.5f),
                recoil=recoil>0?Scaled(WeaponRecoilProfile.PistolCalibration(),recoil):WeaponRecoilProfile.Disabled(),
                holdingArea=new HoldingArea(),stopAtFirstActor=true,stopAtTerrain=true,pellets=(int)Num(character,"kol",1)
            };
            if(definition.damage<=0 || definition.cooldown<=0 || definition.magazineSize<=0 || speed<=0) return "нет урона, темпа, магазина или скорости снаряда";

            // Presentation: prefab with the rest frame and the muzzle at the emit socket (source px, y down).
            string file=Safe(wid)+"-"+grade;
            var rest=frames[0];
            var view=Prefab(id,file,rest,(float)emit["x"],(float)emit["y"],assets);
            if(view.ReadRig().muzzleX<=0) return "точка вылета позади рукояти";

            // The appearance's labels: a clip runs from its label to the next one (or the last frame).
            var labels=((JObject)source["labels"]??new JObject()).Properties().ToDictionary(p=>p.Name,p=>(int)p.Value,StringComparer.Ordinal);
            int count=(int)source["frameCount"];
            Sprite[] Range(string label)
            {
                if(!labels.TryGetValue(label,out int first)) return null;
                int next=labels.Values.Where(v=>v>first).DefaultIfEmpty(count+1).Min();
                return frames.Skip(first-1).Take(next-first).ToArray();
            }
            WeaponShotClip shotClip=null;WeaponReloadClip reloadClip=null;
            if(Range("shoot") is {Length:>0} shotFrames)
            {
                string path=PrefabFolder+"/"+file+"-shot.asset";assets.Add(path);
                shotClip=AssetDatabase.LoadAssetAtPath<WeaponShotClip>(path);bool created=shotClip==null;
                if(created) shotClip=ScriptableObject.CreateInstance<WeaponShotClip>();
                shotClip.ConfigureDeferred(FramesPerSecond,FramePaths(exported,appearance,shotFrames,frames),rest.pixelsPerUnit);
                if(created) AssetDatabase.CreateAsset(shotClip,path);else EditorUtility.SetDirty(shotClip);
            }
            if(definition.rechargeSeconds<=0 && Range("reload") is {Length:>0} reloadFrames)
            {
                string path=PrefabFolder+"/"+file+"-reload.asset";assets.Add(path);
                reloadClip=AssetDatabase.LoadAssetAtPath<WeaponReloadClip>(path);bool created=reloadClip==null;
                if(created) reloadClip=ScriptableObject.CreateInstance<WeaponReloadClip>();
                reloadClip.ConfigureDeferred(FramePaths(exported,appearance,reloadFrames,frames),rest.pixelsPerUnit);
                if(created) AssetDatabase.CreateAsset(reloadClip,path);else EditorUtility.SetDirty(reloadClip);
            }
            CasingEffectProfile casing=null;
            if(casingResource!=null)
            {
                if(!casings.TryGetValue(casingResource,out casing))
                {
                    string path=CasingFolder+"/"+Safe(casingResource)+".asset";
                    casing=AssetDatabase.LoadAssetAtPath<CasingEffectProfile>(path);bool created=casing==null;
                    if(created) casing=ScriptableObject.CreateInstance<CasingEffectProfile>();
                    casing.Configure(casingFrames[0],Vector2.zero);
                    casing.ConfigureReference(CatalogSpriteAuthoring.Reference(casingFrames[0]));
                    if(created) AssetDatabase.CreateAsset(casing,path);else EditorUtility.SetDirty(casing);
                    casings.Add(casingResource,casing);
                }
                assets.Add(AssetDatabase.GetAssetPath(casing));
            }

            // The trace: Bullet stretches the symbol to speed/100 of its length (spring 1); a beam (spring 2+) spans the hit.
            bool beam=Num(vis,"spring",1)>=2;
            float native=projectileFrames[0].rect.width/(projectileFrames[0].pixelsPerUnit/PixelsPerMetre);
            string shotKey=projectile+"|"+beam+"|"+(beam?0:Mathf.RoundToInt(speed));
            if(!shots.TryGetValue(shotKey,out var shot))
                shots.Add(shotKey,shot=new Shot {resource=projectile,beam=beam,speed=speed,length=native*Num(phis,"speed")/100/PixelsPerMetre});
            shot.weapons.Add(id);

            string assetPath=WeaponFolder+"/"+file+".asset";assets.Add(assetPath);
            var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(assetPath);bool fresh=asset==null;
            if(fresh) asset=ScriptableObject.CreateInstance<WeaponAsset>();
            asset.SetDefinition(definition);asset.SetPresentation(view);asset.SetShotAnimation(shotClip);asset.SetReloadAnimation(reloadClip);asset.SetCasingProfile(casing);
            if(fresh) AssetDatabase.CreateAsset(asset,assetPath);else EditorUtility.SetDirty(asset);
            shotClip?.ValidateAgainst(rest);reloadClip?.ValidateAgainst(rest);
            bool listed=false;
            for(int i=0;i<list.arraySize;i++) listed|=list.GetArrayElementAtIndex(i).objectReferenceValue==asset;
            if(!listed) {list.arraySize++;list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=asset;}

            audio.Add(new JObject {["weapon"]=id,["clip"]=AudioClipSource.ExportedPrefix+shootSound,["kind"]="shot",["minGain"]=1,["maxGain"]=1});
            if(reloadSound!=null) audio.Add(new JObject {["weapon"]=id,["clip"]=AudioClipSource.ExportedPrefix+reloadSound,["kind"]="reload",["minGain"]=1,["maxGain"]=1});
            return null;
        }

        // A weapon prefab: the rest frame and the muzzle at the emit socket (source px from the registration, y down).
        private static WeaponView Prefab(string id,string file,Sprite rest,float emitX,float emitY,HashSet<string> assets)
        {
            string materialPath=PrefabFolder+"/"+file+".mat",prefabPath=PrefabFolder+"/"+file+".prefab";assets.Add(materialPath);assets.Add(prefabPath);
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null) {material=WorldArtworkMaterial.CreateForSpriteRenderer();AssetDatabase.CreateAsset(material,materialPath);}
            else {material.mainTexture=null;EditorUtility.SetDirty(material);}
            var node=new GameObject(id);
            try
            {
                var renderer=node.AddComponent<SpriteRenderer>();renderer.sprite=rest;renderer.sharedMaterial=material;renderer.sortingOrder=100;
                var muzzle=new GameObject("Дуло").transform;muzzle.SetParent(node.transform,false);
                muzzle.localPosition=new Vector3(emitX/PixelsPerMetre,-emitY/PixelsPerMetre,0);
                var component=node.AddComponent<WeaponView>();component.Configure(id,renderer,muzzle);
                component.ConfigureRest(CatalogSpriteAuthoring.Reference(rest));
                return PrefabUtility.SaveAsPrefabAsset(node,prefabPath).GetComponent<WeaponView>();
            }
            finally {UnityEngine.Object.DestroyImmediate(node);}
        }

        // Turret guns (UnitTurret: new Weapon(this, "turret" + tr), tip 0, vweap viswturret — an invisible clip that
        // marks only the emit point): no ammunition; the gun drawn is frame tr of the turrets' part "puha", turned by the
        // turret to its aim. Numbers as the firearms'.
        private static string TurretGun(string wid,XElement root,JObject[] graphics,JObject[] sounds,string puha,Dictionary<string,JObject> resources,
            ExportedContentCatalog exported,SerializedProperty list,Dictionary<string,Shot> shots,List<JObject> audio,HashSet<string> assets)
        {
            if(!int.TryParse(wid.Substring("turret".Length),out int tr) || tr<1) return "не номер оружия турели";
            var character=root.Element("char");var phis=root.Element("phis");var vis=root.Element("vis");
            if(character==null || phis==null || vis==null) return "нет char, phis или vis";
            var ammoNode=root.Element("ammo");
            if(ammoNode?.Attribute("holder")==null || ammoNode.Attribute("reload")==null) return "нет магазина (ammo holder/reload)";
            if(Num(character,"damexpl",0)>0 || Num(character,"kol",1)>1 || phis.Attribute("grav")!=null) return "взрыв, дробь или навесной снаряд";
            string Binding(JObject[] bindings,string role)=>bindings.FirstOrDefault(b=>(string)b["role"]==role)?["resource"]?.ToString();
            string marker=Binding(graphics,"appearance-grade-0"),projectile=Binding(graphics,"projectile-grade-0"),shoot=Binding(sounds,"shoot");
            if(marker==null || projectile==null || shoot==null) return "нет маркера дула (vweap), снаряда или звука";
            var emit=((JArray)resources[marker]["weaponSockets"]??new JArray()).OfType<JObject>().FirstOrDefault(s=>(int)s["frame"]==1 && (string)s["name"]=="emit");
            if(emit==null) return "нет точки вылета у "+marker;
            Sprite[] guns,projectileFrames;
            try {guns=Frames(exported,puha);projectileFrames=Frames(exported,projectile);}
            catch(InvalidDataException error) {return error.Message;}
            if(tr>guns.Length) return $"у части puha нет кадра {tr}";
            string id=$"weapon:{wid}:grade:0";
            float speed=Num(phis,"speed")*FramesPerSecond/PixelsPerMetre,lifetime=BulletLifeFrames/FramesPerSecond;
            var definition=new WeaponDefinition
            {
                id=id,damage=Num(character,"damage"),cooldown=Num(character,"rapid")/FramesPerSecond,
                projectileSpeed=speed,projectileSpeedVariation=SpeedVariation,projectileLifetime=lifetime,range=speed*lifetime,
                shotSpreadHalfAngleDegrees=Mathf.Min(90,Num(phis,"deviation",0)*.5f),recoil=WeaponRecoilProfile.Disabled(),
                holdingArea=new HoldingArea(),stopAtFirstActor=true,stopAtTerrain=true,
                // UnitTurret: currentWeapon.hold = holder; it reloads (ammo reload) from an endless supply.
                magazineSize=(int)Num(ammoNode,"holder"),ammoPerShot=1,reloadSeconds=Mathf.Max(1,Num(ammoNode,"reload"))/FramesPerSecond,unlimitedReserve=true
            };
            var view=Prefab(id,Safe(wid),guns[tr-1],(float)emit["x"],(float)emit["y"],assets);
            bool beam=Num(vis,"spring",1)>=2;
            float native=projectileFrames[0].rect.width/(projectileFrames[0].pixelsPerUnit/PixelsPerMetre);
            string shotKey=projectile+"|"+beam+"|"+(beam?0:Mathf.RoundToInt(speed));
            if(!shots.TryGetValue(shotKey,out var shot))
                shots.Add(shotKey,shot=new Shot {resource=projectile,beam=beam,speed=speed,length=native*Num(phis,"speed")/100/PixelsPerMetre});
            shot.weapons.Add(id);
            string assetPath=WeaponFolder+"/"+Safe(wid)+".asset";assets.Add(assetPath);
            var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(assetPath);bool fresh=asset==null;
            if(fresh) asset=ScriptableObject.CreateInstance<WeaponAsset>();
            asset.SetDefinition(definition);asset.SetPresentation(view);asset.SetShotAnimation(null);asset.SetReloadAnimation(null);asset.SetCasingProfile(null);
            if(fresh) AssetDatabase.CreateAsset(asset,assetPath);else EditorUtility.SetDirty(asset);
            bool listed=false;
            for(int i=0;i<list.arraySize;i++) listed|=list.GetArrayElementAtIndex(i).objectReferenceValue==asset;
            if(!listed) {list.arraySize++;list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=asset;}
            audio.Add(new JObject {["weapon"]=id,["clip"]=AudioClipSource.ExportedPrefix+shoot,["kind"]="shot",["minGain"]=1,["maxGain"]=1});
            return null;
        }

        private static WeaponRecoilProfile Scaled(WeaponRecoilProfile profile,float factor)
        {
            factor=Mathf.Clamp(factor,.1f,3);
            profile.horizontalPeak*=factor;profile.anglePeakDegrees*=factor;profile.maxHorizontal*=factor;profile.maxAngleDegrees*=factor;
            return profile;
        }
        // The exported frames of a symbol in order, as the imported sprites (pivot = the symbol's registration point).
        private static Sprite[] Frames(ExportedContentCatalog exported,string resource)
        {
            var frames=exported.Resource(resource).frames?.OrderBy(f=>f.frame).ToArray()??Array.Empty<ExportedFrame>();
            var sprites=frames.Select(exported.FrameSprite).ToArray();
            if(sprites.Length==0 || sprites.Any(s=>s==null)) throw new InvalidDataException($"Нет импортированных кадров символа {resource}.");
            return sprites;
        }
        // The Resources paths of a symbol's frames (the deferred clips name them, not reference them).
        private static string[] FramePaths(ExportedContentCatalog exported,string resource,Sprite[] wanted,Sprite[] all)
        {
            var frames=exported.Resource(resource).frames.OrderBy(f=>f.frame).ToArray();
            return wanted.Select(sprite=>frames[Array.IndexOf(all,sprite)].spriteResource).ToArray();
        }
        private static void WriteAudio(List<JObject> generated)
        {
            var json=JObject.Parse(File.ReadAllText(CombatAudioPath));var entries=(JArray)json["entries"];
            // Earlier generated entries are replaced as a whole (weapon IDs of the export).
            var kept=entries.OfType<JObject>().Where(e=>!((string)e["weapon"]).StartsWith("weapon:",StringComparison.Ordinal)).ToList();
            var lines=kept.Concat(generated).Select(e=>"    "+e.ToString(Newtonsoft.Json.Formatting.None));
            File.WriteAllText(CombatAudioPath,"{\n  \"entries\": [\n"+string.Join(",\n",lines)+"\n  ]\n}\n");
            AssetDatabase.ImportAsset(CombatAudioPath,ImportAssetOptions.ForceUpdate);
        }
        private static string Safe(string value)=>new string(value.Select(c=>char.IsLetterOrDigit(c) || c=='-' || c=='_'?c:'-').ToArray());
        private static float Num(XElement element,string name,float? fallback=null)
        {
            string value=(string)element?.Attribute(name);
            if(value!=null) return float.Parse(value,CultureInfo.InvariantCulture);
            return fallback??throw new InvalidDataException($"Нет атрибута {name} в {element?.Name}.");
        }
        private static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];
            for(int i=1;i<parts.Length;i++) {string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,parts[i]);current=next;}
        }
    }
}
