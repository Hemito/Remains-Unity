using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEntryLighting
    {
        [Serializable] private sealed class Source {public Placement[] lights;}
        [Serializable] private sealed class Placement
        {public string id,kind;public float x,y,maskX,maskY,maskWidth,maskHeight;}
        public static void Import()
        {
            AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles("Assets/Remains/Resources/Original/Room/Factory46","light-mask-*.png"))
            {
                var texture=(TextureImporter)AssetImporter.GetAtPath(path);
                texture.isReadable=true;texture.npotScale=TextureImporterNPOTScale.None;texture.maxTextureSize=8192;
                texture.textureCompression=TextureImporterCompression.Uncompressed;texture.mipmapEnabled=false;
                texture.alphaIsTransparency=true;texture.wrapMode=TextureWrapMode.Clamp;texture.SaveAndReimport();
            }
            const string prefab="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);
            try
            {
                if(root.GetComponent<RoomLightingAuthoring>()!=null) throw new InvalidOperationException("Освещение входа уже задано; авторские правки не заменены.");
                var source=JsonUtility.FromJson<Source>(File.ReadAllText(Path.Combine(OriginalTrainingRoomSource.Research,"factory-46-lights.json")));
                if(source.lights.Length!=13 || source.lights.Select(l=>l.id).Distinct().Count()!=13)
                    throw new InvalidOperationException("Ожидались 13 уникальных исходных светильников.");
                var room=root.GetComponent<RoomAuthoring>();
                var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(new Color(.38f,.4f,.43f));lighting.SetBackgroundAmbientScale(.6f);
                var group=new GameObject("Исходные светильники");group.transform.SetParent(root.transform,false);
                foreach(var lamp in source.lights)
                {
                    string key="Factory46/decor-"+lamp.kind+"-1";
                    var fixture=root.GetComponentsInChildren<RoomArtworkAuthoring>().Single(art=>
                    {var a=art.Read(room);return a.texture==key && Mathf.Abs(a.x-lamp.x)<.001f && Mathf.Abs(a.y+a.height-lamp.y)<.001f;});
                    var bounds=fixture.Read(room);
                    string mask="Original/Room/Factory46/light-mask-"+lamp.kind+"-1";
                    var texture=Resources.Load<Texture2D>(mask);
                    if(texture==null || !texture.isReadable || Mathf.Abs(texture.width/200f-lamp.maskWidth)>.001f || Mathf.Abs(texture.height/200f-lamp.maskHeight)>.001f)
                        throw new InvalidOperationException("Маска светильника потеряла исходный размер: "+lamp.id);
                    var node=new GameObject(lamp.kind+" · "+lamp.id);node.transform.SetParent(group.transform,false);
                    node.AddComponent<RoomLightAuthoring>().Configure(new RoomLight {
                        id=lamp.id,x=bounds.x+bounds.width/2,y=bounds.y+bounds.height/2,radius=7,intensity=1.1f,
                        color=Color.white,mask=mask,maskX=lamp.maskX,maskY=lamp.maskY,maskWidth=lamp.maskWidth,maskHeight=lamp.maskHeight},texture);
                    fixture.transform.SetParent(node.transform,true);
                }
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    if(art.Read(room).order>=-10) continue;
                    var material=art.GetComponent<MeshRenderer>().sharedMaterial;material.SetFloat("_BackgroundLighting",1);EditorUtility.SetDirty(material);
                }
                if(lighting.CreateProfile(room).lights.Length!=13) throw new InvalidOperationException("Потеряны источники света входа.");
                PrefabUtility.SaveAsPrefabAsset(root,prefab);AssetDatabase.SaveAssets();
                Debug.LogWarning("FactoryEntry46: маски и позиции светильников взяты из ресурсов; ambient и интенсивность — настройки черновика, точное совпадение с оригиналом не проверено.",root);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            FactoryEntryDecoration.Verify();
        }
    }
}
