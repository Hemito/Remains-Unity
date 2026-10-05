using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RaiderWeaponSocketAuthoring
    {
        [Serializable] private sealed class Point {public float x,y,r;}
        [Serializable] private sealed class Row {public Point[] points;}
        [Serializable] private sealed class Research {public string sourceSha256;public Row[] rows;}
        public static void InstallAndBuild()
        {
            const string atlasPath="Assets/Remains/Resources/Original/Raider/artwork.asset";
            string researchPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/original/raider-art/weapon-sockets.json"));
            var research=JsonUtility.FromJson<Research>(File.ReadAllText(researchPath));
            if(research?.rows?.Length!=10 || research.sourceSha256!="0c1551d76799a20fcd3235a1038ba9aa962318a7b803b557e3410ff75ec1bd45")
                throw new InvalidOperationException("Не подтверждён выбранный числовой ресурс крепления оружия рейдера 1.04.");
            var atlas=AssetDatabase.LoadAssetAtPath<AtlasSpriteAsset>(atlasPath);
            var type=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>("Assets/Remains/Resources/Content/actors/raider.asset");
            if(atlas==null || type==null) throw new InvalidOperationException("Нет сохранённых определений рейдера.");
            var data=atlas.CreateDefinition();
            var oldSprites=AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().ToDictionary(s=>s.name,LocalId);
            foreach(var clip in data.clips)
            {
                var row=research.rows[clip.row].points;
                if(row==null || row.Length<clip.count) throw new InvalidOperationException($"Недостаточно исходных точек: pose={clip.pose}, row={clip.row}.");
                clip.weaponSockets=row.Take(clip.count).Select(p=>new AtlasWeaponSocketPoint {x=p.x,y=p.y}).ToArray();
                AtlasWeaponSockets.ValidateClip(clip,atlasPath,required:true);
            }
            atlas.Configure(atlas.Texture,data);AtlasSpriteAssetEditor.Rebuild(atlas);
            var sprites=AssetDatabase.LoadAllAssetsAtPath(atlasPath).OfType<Sprite>().ToArray();
            if(sprites.Length!=oldSprites.Count || sprites.Any(s=>!oldSprites.TryGetValue(s.name,out long id) || LocalId(s)!=id))
                throw new InvalidOperationException("Обновление точек оружия изменило ID кадров атласа.");
            var definition=type.CreateDefinition();definition.atlasWeaponSocket=true;type.SetDefinition(definition);EditorUtility.SetDirty(type);
            AssetDatabase.SaveAssets();ActorTypes.Load();Anatomy.Load();WeaponCatalog.Load();
            RaiderWeaponSocketChecks.Run();RaiderWeaponSocketChecks.Render();PrototypeBuild.Build();
        }
        private static long LocalId(Sprite sprite)
        {
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string _,out long id))
                throw new InvalidOperationException("Не прочитан ID сохранённого кадра "+sprite.name);
            return id;
        }
    }
}
