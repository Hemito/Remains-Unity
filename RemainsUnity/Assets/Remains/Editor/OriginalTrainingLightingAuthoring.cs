using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class OriginalTrainingLightingAuthoring
    {
        public static void Import()
        {
            const string path="Assets/Remains/RoomDrafts/OriginalTrainingRoom.prefab";
            const string maskPath="Assets/Remains/Resources/Original/Room/TrainingRoom/light-mask-light5-1.png";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(root.GetComponent<RoomLightingAuthoring>()!=null || File.Exists(maskPath))
                    throw new InvalidOperationException("Освещение стрельбища уже подготовлено; импорт не должен стирать правки.");
                File.Copy(Path.Combine(OriginalTrainingRoomSource.Research,"hub-light-sprites/DefineSprite_592_back_light5_l/1.png"),maskPath);
                AssetDatabase.ImportAsset(maskPath);
                var importer=(TextureImporter)AssetImporter.GetAtPath(maskPath);
                importer.isReadable=true;importer.npotScale=TextureImporterNPOTScale.None;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;
                importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
                var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(new Color(.38f,.4f,.43f));
                lighting.SetBackgroundAmbientScale(.6f);
                var room=root.GetComponent<RoomAuthoring>();var source=OriginalTrainingRoomSource.Read();
                var fixtures=source.Elements("back").Where(e=>(string)e.Attribute("id")=="light4" || (string)e.Attribute("id")=="light5").ToArray();
                if(fixtures.Length!=7) throw new InvalidOperationException("В стрельбище ожидаются семь исходных светильников.");
                var group=new GameObject("Источники света");group.transform.SetParent(root.transform,false);int index=0;
                foreach(var fixture in fixtures)
                {
                    string kind=(string)fixture.Attribute("id");bool ceiling=kind=="light4";
                    float x=(float)fixture.Attribute("x")*.8f,y=(source.Elements("a").Count()-(float)fixture.Attribute("y"))*.8f;
                    string key="TrainingRoom/decor-"+kind+"-1";
                    var art=root.GetComponentsInChildren<RoomArtworkAuthoring>().Single(a=>
                    {var d=a.Read(room);return d.texture==key && Mathf.Abs(d.x-x)<.001f && Mathf.Abs(d.y+d.height-y)<.001f;});
                    var data=art.Read(room);
                    string mask=ceiling?"Original/Room/Hub/light-mask-light4-1":"Original/Room/TrainingRoom/light-mask-light5-1";
                    var texture=Resources.Load<Texture2D>(mask);
                    if(texture==null || !texture.isReadable) throw new InvalidOperationException("Нет читаемой маски: "+mask);
                    var node=new GameObject("Светильник "+kind+" · "+index);node.transform.SetParent(group.transform,false);
                    node.AddComponent<RoomLightAuthoring>().Configure(new RoomLight {
                        id="training-lamp-"+index++,x=data.x+data.width/2,y=data.y+data.height/2,
                        radius=7,intensity=1.1f,color=Color.white,mask=mask,
                        maskX=x+(ceiling?-1.782f:-1.061f),maskY=y-(ceiling?3.91f:1.841f),
                        maskWidth=texture.width/100f,maskHeight=texture.height/100f},texture);
                    art.transform.SetParent(node.transform,true);
                }
                // Walls respond to the background map; platforms and foreground rims keep actor lighting.
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var data=art.Read(room);if(data.order> -10 || data.texture=="TrainingRoom/steel" || data.texture=="TrainingRoom/concrete") continue;
                    var renderer=art.GetComponent<MeshRenderer>();renderer.sharedMaterial.SetFloat("_BackgroundLighting",1);
                    EditorUtility.SetDirty(renderer.sharedMaterial);
                }
                if(lighting.CreateProfile(room).lights.Length!=7) throw new InvalidOperationException("Потеряны источники света.");
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            OriginalTrainingCratesAuthoring.Verify();
        }
    }
}
