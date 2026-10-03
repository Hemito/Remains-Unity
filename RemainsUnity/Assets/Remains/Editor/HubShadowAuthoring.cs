using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HubShadowAuthoring
    {
        public static void Run()
        {
            const string resource="Original/Room/Hub/light-mask-shadow-1";
            string texturePath="Assets/Remains/Resources/"+resource+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
            if(importer==null) throw new InvalidOperationException("Не импортирована исходная маска back_shadow_l.");
            importer.isReadable=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
            var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();float top=room.CreateLayout().top;
                // RoomsCamp room_0_0: shadow at (35,1) and (1,17), each 12 x 7 original 40-pixel cells.
                foreach(var entry in new[]{("upper-right",35f,1f),("library",1f,17f)})
                {
                    string name="Тень: "+entry.Item1;
                    var found=root.transform.Find(name);
                    var component=found!=null?found.GetComponent<RoomLightAuthoring>():new GameObject(name).AddComponent<RoomLightAuthoring>();
                    if(component==null) throw new InvalidOperationException("У области тени отсутствует RoomLightAuthoring: "+name);
                    component.transform.SetParent(root.transform,false);
                    float x=entry.Item2*.8f,y=top-(entry.Item3+7)*.8f;
                    component.Configure(new RoomLight {id="shadow-"+entry.Item1,shadow=true,intensity=.9f,radius=1,
                        x=x,y=y,mask=resource,maskX=x,maskY=y,maskWidth=12*.8f,maskHeight=7*.8f},mask);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();LightingChecks.Run();HubVisualChecks.Render();
        }
    }
}
