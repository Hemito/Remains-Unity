using System;
using System.IO;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponArtworkReimport
    {
        [Serializable] private sealed class Export {public float pixelsPerUnit;public WeaponRig[] weapons;}
        public static void Run()
        {
            // This entry point describes the historical four-weapon4x migration.
            // Refuse before touching assets when the current reserve is installed.
            if(VectorArtworkReserveManifest.Hash("Assets/Remains/Resources/Original/Weapons/spear.png")!=
                VectorArtworkReserveManifest.Hash("../.reference/original/weapons-4x/DefineSprite_1366_visspear/1.png"))
                throw new InvalidOperationException("Этот вход предназначен для исторической миграции4×. Текущие векторные рисунки устанавливаются через VectorArtworkReserveAuthoring.InstallAndBuild; уменьшение запрещено.");
            var data=JsonUtility.FromJson<Export>(File.ReadAllText("../.reference/migration/weapon-artwork-4x.json"));
            if(data.pixelsPerUnit!=200 || data.weapons.Length!=4) throw new InvalidOperationException("Неверный экспорт графики оружия 4×.");
            foreach(var rig in data.weapons)
            {
                string root="Assets/Remains/Resources/Original/Weapons/";
                var texture=Import(root+rig.id+".png");
                var rect=new Rect(rig.x,rig.y,rig.width,rig.height);
                var pivot=new Vector2(rig.pivotX,rig.pivotY);
                Replace(rig.id+"-sprite",texture,rect,pivot,data.pixelsPerUnit);
                var origin=rect.position+Vector2.Scale(pivot,rect.size);
                int frames=rig.id=="pistol"?1:rig.id=="energy"?4:0;
                for(int i=1;i<=frames;i++)
                {
                    var shot=Import(root+"Shot/"+rig.id+"-"+i+".png");
                    if(shot.width!=texture.width || shot.height!=texture.height) throw new InvalidOperationException("Кадры оружия имеют разные холсты: "+rig.id);
                    Replace(rig.id+"-shot-"+i,shot,new Rect(0,0,shot.width,shot.height),new Vector2(origin.x/shot.width,origin.y/shot.height),data.pixelsPerUnit);
                }
            }
            AssetDatabase.SaveAssets();
            PrototypeBuild.Build();
            // Run visual checks in a fresh editor process: BuildPlayer invalidates cached shader resources.
        }
        private static Texture2D Import(string path)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=false;
            importer.maxTextureSize=4096;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static void Replace(string name,Texture2D texture,Rect rect,Vector2 pivot,float ppu)
        {
            string path="Assets/Remains/Prefabs/Weapons/"+name+".asset";
            var target=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if(target==null) throw new InvalidOperationException("Отсутствует сохранённый спрайт: "+path);
            var fresh=Sprite.Create(texture,rect,pivot,ppu,0,SpriteMeshType.FullRect);fresh.name=target.name;
            EditorUtility.CopySerialized(fresh,target);EditorUtility.SetDirty(target);UnityEngine.Object.DestroyImmediate(fresh);
        }
    }
}
