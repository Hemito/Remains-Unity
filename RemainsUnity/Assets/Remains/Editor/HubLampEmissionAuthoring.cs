using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class HubLampEmissionAuthoring
    {
        public static void Run()
        {
            var keys=new HashSet<string> {"Hub/decor-light1-1","Hub/decor-light3-1",
                "Hub/decor-light4-1","Hub/decor-light5-2","Hub/decor-stlight1-1"};
            var materials=new HashSet<Material>();int count=0;
            var meshes=new HashSet<Mesh>();
            var root=PrefabUtility.LoadPrefabContents("Assets/Remains/Resources/Levels/hub.prefab");
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var annex=root.transform.Find("Тир");
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    if(annex!=null && art.transform.IsChildOf(annex)) continue;
                    var data=art.Read(room);
                    // Resized annex fixtures can be authored directly under the room root.
                    if(data.x>=38.4f) continue;
                    if(!keys.Contains(data.texture)) continue;
                    var material=art.GetComponent<MeshRenderer>().sharedMaterial;
                    string path=AssetDatabase.GetAssetPath(material);
                    if(!path.StartsWith("Assets/Remains/Resources/Levels/hub-background-",StringComparison.Ordinal))
                        throw new InvalidOperationException("Неожиданный материал плафона хаба: "+path);
                    var mesh=art.GetComponent<MeshFilter>().sharedMesh;
                    string meshPath=AssetDatabase.GetAssetPath(mesh);
                    if(!meshPath.StartsWith("Assets/Remains/Resources/Levels/hub-artwork/",StringComparison.Ordinal) ||
                        mesh.vertexCount==0 || mesh.colors.Length!=mesh.vertexCount)
                        throw new InvalidOperationException("Неожиданная геометрия плафона: "+meshPath);
                    foreach(var color in mesh.colors)
                        if((Mathf.Abs(color.r-.82f)>.005f && Mathf.Abs(color.r-1)>.005f) ||
                            Mathf.Abs(color.g-color.r)>.005f || Mathf.Abs(color.b-color.r)>.005f)
                            throw new InvalidOperationException("Оттенок плафона изменён: "+meshPath);
                    meshes.Add(mesh);
                    materials.Add(material);count++;
                    Debug.Log($"Плафон {art.name}: {data.texture}, x={data.x}, y={data.y}, material={path}.");
                }
                if(count!=9 || materials.Count!=5)
                    throw new InvalidOperationException($"Ожидались девять плафонов пяти типов; найдено {count}/{materials.Count}.");
                foreach(var material in materials)
                {
                    material.SetFloat("_Emission",1);EditorUtility.SetDirty(material);
                }
                foreach(var mesh in meshes)
                {
                    var colors=mesh.colors;
                    for(int i=0;i<colors.Length;i++) colors[i]=new Color(1,1,1,colors[i].a);
                    mesh.colors=colors;EditorUtility.SetDirty(mesh);
                }
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Debug.Log($"Плафоны хаба сохраняют собственный цвет: {count} объектов, {materials.Count} материалов.");
            HubRangeAuthoring.RenderReferenceHub();PrototypeBuild.Build();
        }
    }
}
