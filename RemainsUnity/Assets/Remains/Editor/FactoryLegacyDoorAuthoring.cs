using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLegacyDoorAuthoring
    {
        public const string ManifestPath="Tools/Research/LegacyFactoryDoors68/manifest.json";
        private const string Folder="Assets/Remains/Resources/Presentation/Doors";
        private const string TexturePath="Assets/Remains/Resources/Original/Doors/LegacyCapabilities/door3-die.png";
        private const string SpritePath=Folder+"/door3-die.asset";
        [Serializable] public sealed class Record {public int module;public string stableId,definition;}
        [Serializable] public sealed class Definition {public string id,destroyedResource,destroySound;public float hp;}
        [Serializable] public sealed class Before {public string path,sha256,guid;}
        [Serializable] public sealed class NativeFrame {public string stagedPng,sha256,rgbaSha256,textureGuid,classification;public int width,height,sourceSprite;public float pixelsPerUnit;public float[] pivotPixels;}
        [Serializable] public sealed class Manifest
        {public int schema;public Record[] records;public Definition[] types;public Dictionary<string,Before> assetsBefore;public Dictionary<string,string> protectedFiles;public NativeFrame door3;}
        public static string Patch(int module)=>"base:factory-legacy-door-"+module+":capabilities-v1";
        public static Manifest ReadManifest()
        {
            string path=Repository(ManifestPath);
            Require(File.Exists(path),"Нет подготовленного source manifest: "+path);
            var data=StateJson.FromJson<Manifest>(File.ReadAllText(path));
            ValidateDeclaration(data);
            foreach(var type in data.types)
            {
                string before=Repository("Tools/Research/LegacyFactoryDoors68/source/"+type.id+"-before.asset");
                Require(File.Exists(before) && Hash(before)==data.assetsBefore[type.id].sha256,"Изменена исходная копия прежнего SO: "+type.id);
            }
            Require(File.Exists(Repository(data.door3.stagedPng)) && Hash(Repository(data.door3.stagedPng))==data.door3.sha256,"Подготовленный native PNG изменён/отсутствует.");
            return data;
        }
        internal static void ValidateDeclaration(Manifest data)
        {
            Require(data?.schema==1 && data.records?.Length==9 && data.types?.Length==3 && data.assetsBefore?.Count==3 && data.protectedFiles?.Count==36,
                "Неполная/неизвестная версия manifest.");
            Require(data.records.All(r=>r!=null) && data.types.All(t=>t!=null) &&
                data.records.Select(r=>r.stableId).Distinct(StringComparer.Ordinal).Count()==9 &&
                data.records.Select(r=>r.module).OrderBy(i=>i).SequenceEqual(new[]{18,18,18,46,46,46,53,53,53}) &&
                data.types.Select(t=>t?.id).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(new[]{"door1","door2","door3"}),"Не точный набор9 ID/3 типов.");
            foreach(var entry in data.records)
                Require(entry!=null && entry.stableId?.StartsWith("original:plant:"+entry.module+":",StringComparison.Ordinal)==true && data.types.Count(t=>t.id==entry.definition)==1,
                    "Неизвестная исходная запись: "+entry?.stableId);
            foreach(var type in data.types)
            {
                float hp=type.id=="door1"?60:type.id=="door2"?1000:5000;
                string graphic=type.id=="door1"?"Original/Room/OfficeOpenings/door1-die":type.id=="door2"?"Original/Doors/WarehouseMetal/door2-die":"Original/Doors/LegacyCapabilities/door3-die";
                Require(type.hp==hp && type.destroyedResource==graphic && type.destroySound==(type.id=="door1"?"break_wood":"break_metal") &&
                    data.assetsBefore.TryGetValue(type.id,out var before) && before!=null && before.path=="RemainsUnity/"+Folder+"/"+type.id+".asset" && HashString(before.sha256) && GuidString(before.guid),"Изменено исходное HP/die/ID: "+type.id);
            }
            var frame=data.door3;
            Require(frame!=null && frame.sourceSprite==284 && frame.classification=="native-bitmap-only" && frame.width==40 && frame.height==120 && frame.pixelsPerUnit==50 &&
                frame.pivotPixels?.Length==2 && frame.pivotPixels[0]==20 && frame.pivotPixels[1]==0 && HashString(frame.sha256) && HashString(frame.rgbaSha256) && GuidString(frame.textureGuid) &&
                frame.stagedPng=="Tools/Research/LegacyFactoryDoors68/resources/door3-die.png","Изменена native bitmap регистрация door3.");
            foreach(var file in data.protectedFiles) Require(!string.IsNullOrWhiteSpace(file.Key) && HashString(file.Value),"Нет точного protected file hash: "+file.Key);
        }
        [MenuItem("Remains/Контент/Обновить прочность прежних дверей завода")]
        public static void Install()
        {
            var data=ReadManifest();VerifyPreservation();
            // Preflight every old asset before any writes; refuse manual/partial unknown profiles.
            foreach(var type in data.types)
            {
                var asset=Load<DoorAsset>(Folder+"/"+type.id+".asset");asset.Validate();
                if(asset.CapabilityVersion==0) Require(Hash(Repository(data.assetsBefore[type.id].path))==data.assetsBefore[type.id].sha256,"Прежний SO изменён до согласованного обновления: "+type.id);
                else CheckDefinition(type.id,asset);
            }
            foreach(int module in new[]{46,18,53}) CheckRegistrationBefore(ModulePath(module),module,data);
            var sounds=FactoryOfficeOpeningsAuthoring.ReadResources();
            FactoryOfficeOpeningsAuthoring.ValidateSoundFiles(sounds,Path.GetDirectoryName(Application.dataPath));
            foreach(var type in data.types) Load<AudioClip>(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,type.destroySound));
            PreflightNativeFrame(data.door3);
            InstallNativeFrame(data.door3);
            foreach(var type in data.types)
            {
                var asset=Load<DoorAsset>(Folder+"/"+type.id+".asset");
                if(asset.CapabilityVersion==1) continue;
                asset.ConfigureBarrier(type.id,asset.Size,asset.Closed,asset.Opened,Destroyed(type),asset.OpenSound,asset.CloseSound,
                    Load<AudioClip>(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,type.destroySound)),true,type.hp,asset.Size);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            foreach(int module in new[]{46,18,53})
            {
                var root=PrefabUtility.LoadPrefabContents(ModulePath(module));
                try
                {
                    var registration=root.GetComponent<RoomDoorCapabilityContent>();
                    if(registration==null)
                    {
                        registration=root.AddComponent<RoomDoorCapabilityContent>();
                        registration.Configure(Patch(module),data.records.Where(r=>r.module==module).Select(r=>r.stableId).ToArray());
                        Require(PrefabUtility.SaveAsPrefabAsset(root,ModulePath(module))!=null,"Не сохранена явная регистрация module="+module);
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            Check();VerifyPreservation();
            Debug.Log("Прежние двери завода:9 исходных ID получили HP60/1000/5000 и native die. Closed/open PNG, Sprite GUID, размеры и anchors сохранены; миграция старых миров отдельно, без замков/threshold и заявления original-runtime equivalence.");
        }
        public static void Check()
        {
            var data=ReadManifest();CheckNativeFrame(data.door3);
            foreach(var type in data.types) CheckDefinition(type.id,Load<DoorAsset>(Folder+"/"+type.id+".asset"));
            foreach(int module in new[]{46,18,53})
            {
                CheckRegistrationBefore(ModulePath(module),module,data,true);
                var room=Load<GameObject>(ModulePath(module)).GetComponent<RoomAuthoring>();
                Require(RoomDoorCapabilityContent.Read(room,"factory:0").Any(l=>l.patch=="factory:0:"+Patch(module) && l.doors.Length==3),"Нет текущей явной регистрации: module="+module);
            }
        }
        // Historical checks accept either declared pre-upgrade or this exact new stage.
        internal static void ValidateLegacyOrUpgraded(DoorAsset asset)
        {
            Require(asset!=null && asset.Id is "door1" or "door2" or "door3","Чужой legacy профиль.");
            if(asset.CapabilityVersion==0) {Require(asset.MaxHealth==0 && asset.Destroyed==null,"Повреждён legacy capabilityVersion0: "+asset.Id);return;}
            CheckDefinition(asset.Id,asset);
        }
        internal static void ValidateDeclaredCapability(DoorState state)
        {
            var asset=Load<DoorAsset>(Folder+"/"+state.definition+".asset");ValidateLegacyOrUpgraded(asset);
            if(asset.CapabilityVersion==0) Require(state.integrity==null && state.interaction==null && state.interactive,"Legacy расстановка получила неизвестные capabilities: "+state.id);
            else Require(state.integrity?.version==1 && state.integrity.health==asset.MaxHealth && state.integrity.maxHealth==asset.MaxHealth && state.integrity.resolvedAttacks.Count==0 &&
                state.interactive && state.InteractionBounds==state.Bounds,"Расстановка не читает обновлённый SO: "+state.id);
        }
        public static void CheckDefinition(string id,DoorAsset asset)
        {
            var data=ReadManifest();var type=data.types.Single(t=>t.id==id);asset.Validate();
            Require(asset.Id==id && asset.CapabilityVersion==1 && asset.Interactive && asset.MaxHealth==type.hp && asset.InteractionSize==asset.Size &&
                asset.Size==new Vector2(.8f,id=="door3"?2.4f:1.6f) && asset.Closed==Load<Sprite>(Folder+"/"+id+"-closed.asset") && asset.Opened==Load<Sprite>(Folder+"/"+id+"-open.asset") &&
                asset.Destroyed==Destroyed(type),"Не точный upgraded профиль: "+id);
            var sounds=FactoryOfficeOpeningsAuthoring.ReadResources();
            Require(asset.DestroySound==Load<AudioClip>(FactoryOfficeOpeningsAuthoring.SoundAssetPath(sounds,type.destroySound)),"Die sound отличается: "+id);
            Require(AssetDatabase.AssetPathToGUID(Folder+"/"+id+".asset")==data.assetsBefore[id].guid,"Изменён GUID existing DoorAsset: "+id);
            string original=File.ReadAllText(Repository("Tools/Research/LegacyFactoryDoors68/source/"+id+"-before.asset"));
            foreach(var field in new[]{("openSound",asset.OpenSound),("closeSound",asset.CloseSound)})
            {
                var match=Regex.Match(original,@"(?m)^  "+field.Item1+@": \{fileID: ([0-9]+), guid: ([a-f0-9]{32}), type: [0-9]+\}");
                Require(match.Success && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(field.Item2,out string guid,out long localId) &&
                    guid==match.Groups[2].Value && localId.ToString(System.Globalization.CultureInfo.InvariantCulture)==match.Groups[1].Value,"Изменён прежний open/close AudioClip: "+id+"/"+field.Item1);
            }
        }
        public static void VerifyPreservation()
        {
            foreach(var pair in ReadManifest().protectedFiles)
                Require(File.Exists(Repository(pair.Key)) && Hash(Repository(pair.Key))==pair.Value,"Изменён защищённый closed/open Sprite/PNG/prefab/material: "+pair.Key);
        }
        private static void CheckRegistrationBefore(string path,int module,Manifest data,bool requireInstalled=false)
        {
            var room=Load<GameObject>(path).GetComponent<RoomAuthoring>();
            var ids=data.records.Where(r=>r.module==module).Select(r=>r.stableId).ToArray();
            var all=RoomDoorContent.Read(room,"factory:0");
            foreach(var entry in data.records.Where(r=>r.module==module)) Require(all.doors.Count(d=>d.id=="factory:0:"+entry.stableId && d.definition==entry.definition)==1,"Нет прежней исходной двери: "+entry.stableId);
            var registrations=room.GetComponents<RoomDoorCapabilityContent>();
            Require(registrations.Length<=1 && (!requireInstalled || registrations.Length==1),"Частичная/повторная capability регистрация module="+module);
            if(registrations.Length==1) Require(registrations[0].enabled && registrations[0].Patch==Patch(module) && registrations[0].StableIds.SequenceEqual(ids),"Чужая capability регистрация module="+module);
        }
        private static void InstallNativeFrame(NativeFrame data)
        {
            if(!File.Exists(Project(TexturePath)))
            {
                string directory=Path.GetDirectoryName(Project(TexturePath));Directory.CreateDirectory(directory);
                File.WriteAllBytes(Project(TexturePath),File.ReadAllBytes(Repository(data.stagedPng)));
                string template=File.ReadAllText(Project("Assets/Remains/Resources/Original/Doors/door3-closed.png.meta"));
                template=Regex.Replace(template,@"(?m)^guid: .*","guid: "+data.textureGuid);
                File.WriteAllText(Project(TexturePath)+".meta",template,new System.Text.UTF8Encoding(false));
                AssetDatabase.Refresh();
                var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.textureType=TextureImporterType.Default;importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
                importer.mipmapEnabled=false;importer.alphaIsTransparency=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.filterMode=FilterMode.Point;importer.wrapMode=TextureWrapMode.Clamp;importer.sRGBTexture=true;importer.SaveAndReimport();
            }
            Require(Hash(Project(TexturePath))==data.sha256 && AssetDatabase.AssetPathToGUID(TexturePath)==data.textureGuid,"Путь native die занят другим PNG/GUID.");
            if(AssetDatabase.LoadMainAssetAtPath(SpritePath)==null)
            {
                var texture=Load<Texture2D>(TexturePath);var sprite=Sprite.Create(texture,new Rect(0,0,40,120),new Vector2(.5f,0),50,0,SpriteMeshType.FullRect);
                sprite.name="door3-die";AssetDatabase.CreateAsset(sprite,SpritePath);
            }
            CheckNativeFrame(data);
        }
        private static void PreflightNativeFrame(NativeFrame data)
        {
            bool textureExists=File.Exists(Project(TexturePath));
            var sprite=AssetDatabase.LoadMainAssetAtPath(SpritePath);
            Require(sprite==null || textureExists,"Native die Sprite существует без своего PNG: "+SpritePath);
            if(textureExists)
            {
                Require(Hash(Project(TexturePath))==data.sha256 && AssetDatabase.AssetPathToGUID(TexturePath)==data.textureGuid,"Native die PNG/GUID занят чужими данными: "+TexturePath);
                if(sprite!=null) CheckNativeFrame(data);
            }
            else Require(!File.Exists(Project(TexturePath)+".meta"),"Native die meta существует без PNG: "+TexturePath);
        }
        private static void CheckNativeFrame(NativeFrame data)
        {
            var texture=Load<Texture2D>(TexturePath);var sprite=Load<Sprite>(SpritePath);var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
            Require(Hash(Project(TexturePath))==data.sha256 && AssetDatabase.AssetPathToGUID(TexturePath)==data.textureGuid && texture.width==40 && texture.height==120 &&
                sprite.texture==texture && sprite.rect==new Rect(0,0,40,120) && sprite.pixelsPerUnit==50 && sprite.pivot==new Vector2(20,0) &&
                // Unity Sprite bounds include renderer depth; the source registration describes the XY plane.
                (Vector2)sprite.bounds.size==new Vector2(.8f,2.4f) && (Vector2)sprite.bounds.center==new Vector2(0,1.2f) &&
                importer.textureType==TextureImporterType.Default && importer.npotScale==TextureImporterNPOTScale.None && !importer.mipmapEnabled && !importer.alphaIsTransparency &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && importer.filterMode==FilterMode.Point && importer.wrapMode==TextureWrapMode.Clamp,"Native door3 die texture/Sprite/регистрация изменены.");
        }
        public static void DiagnoseNativeFrame()
        {
            var data=ReadManifest().door3;
            var texture=Load<Texture2D>(TexturePath);var sprite=Load<Sprite>(SpritePath);
            var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
            Debug.Log($"Диагностика door3 die: путь={TexturePath}; SHA={Hash(Project(TexturePath))}, ожидается={data.sha256}; GUID={AssetDatabase.AssetPathToGUID(TexturePath)}, ожидается={data.textureGuid}; texture={texture.width}x{texture.height}; ссылка texture совпадает={sprite.texture==texture}; rect={sprite.rect}; PPU={sprite.pixelsPerUnit:R}; pivot=({sprite.pivot.x:R},{sprite.pivot.y:R}); bounds.size=({sprite.bounds.size.x:R},{sprite.bounds.size.y:R},{sprite.bounds.size.z:R}); bounds.center=({sprite.bounds.center.x:R},{sprite.bounds.center.y:R},{sprite.bounds.center.z:R}); type={importer.textureType}; NPOT={importer.npotScale}; mipmaps={importer.mipmapEnabled}; alphaIsTransparency={importer.alphaIsTransparency}; compression={importer.textureCompression}; filter={importer.filterMode}; wrap={importer.wrapMode}",sprite);
            CheckNativeFrame(data);
        }
        private static Sprite Destroyed(Definition type)=>type.id=="door3"?Load<Sprite>(SpritePath):Load<Sprite>("Assets/Remains/Resources/"+type.destroyedResource+".png");
        private static string ModulePath(int module)=>module switch {46=>"Assets/Remains/RoomDrafts/FactoryEntry46.prefab",18=>"Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab",53=>"Assets/Remains/RoomDrafts/FactoryOffice53.prefab",_=>throw new InvalidOperationException("Неизвестный legacy module "+module)};
        private static T Load<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Прежние двери: нет "+typeof(T).Name+" "+path);
        internal static string Repository(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"../..",path));
        private static string Project(string path)=>Path.GetFullPath(Path.Combine(Application.dataPath,"..",path));
        private static bool HashString(string text)=>text!=null && Regex.IsMatch(text,"^[a-f0-9]{64}$");
        private static bool GuidString(string text)=>text!=null && Regex.IsMatch(text,"^[a-f0-9]{32}$");
        private static string Hash(string path) {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        internal static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Прежние двери завода: "+reason);}
    }
}
