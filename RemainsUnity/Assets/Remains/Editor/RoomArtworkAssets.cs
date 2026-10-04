using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomArtworkAssets
    {
        public static void Save(GameObject generated,string parentFolder,string folderName)
        {
            string folder=parentFolder+"/"+folderName;
            if(AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Каталог оформления уже существует: "+folder);
            AssetDatabase.CreateFolder(parentFolder,folderName);
            int index=0;
            foreach(var filter in generated.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();string prefix=folder+"/layer-"+index++;
                AssetDatabase.CreateAsset(filter.sharedMesh,prefix+".asset");
                var material=new Material(renderer.sharedMaterial);var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                material.SetFloat("_Emission",WorldArtworkMaterial.ReadFloat(renderer,"_Emission"));
                material.SetFloat("_BackgroundLighting",WorldArtworkMaterial.ReadFloat(renderer,"_BackgroundLighting"));renderer.SetPropertyBlock(null);
                AssetDatabase.CreateAsset(material,prefix+".mat");renderer.sharedMaterial=material;
            }
            var lifetime=generated.GetComponent<RoomMeshLifetime>();lifetime.Meshes.Clear();UnityEngine.Object.DestroyImmediate(lifetime);
            UnityEngine.Object.DestroyImmediate(generated.GetComponent<RoomLighting>());
        }
    }
}
