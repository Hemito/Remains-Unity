using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit calibration of imported vertex shade; the saved mesh remains the source of truth.
    public static class HubConcreteToneAuthoring
    {
        public static void Run()
        {
            var root=PrefabUtility.LoadPrefabContents("Assets/Remains/Resources/Levels/hub.prefab");
            var meshes=new HashSet<Mesh>();int layers=0;
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                var annex=root.transform.Find("Тир");
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    // The annex is our new room, outside the original screenshot's calibration area.
                    if(annex!=null && art.transform.IsChildOf(annex)) continue;
                    if(art.Read(room).texture!="Hub/concrete") continue;
                    var mesh=art.GetComponent<MeshFilter>().sharedMesh;
                    string path=AssetDatabase.GetAssetPath(mesh);
                    if(!path.StartsWith("Assets/Remains/Resources/Levels/hub-artwork/",StringComparison.Ordinal))
                        throw new InvalidOperationException("Бетон хаба использует неожиданный общий ресурс: "+path);
                    if(mesh.colors.Length!=mesh.vertexCount || mesh.vertexCount==0)
                        throw new InvalidOperationException("Нет полного набора оттенков бетона: "+path);
                    foreach(var color in mesh.colors)
                        if((Mathf.Abs(color.r-.72f)>.005f && Mathf.Abs(color.r-.86f)>.005f) ||
                            Mathf.Abs(color.g-color.r)>.005f || Mathf.Abs(color.b-color.r)>.005f)
                            throw new InvalidOperationException("Исходный оттенок бетона изменён; повторная миграция запрещена: "+path);
                    meshes.Add(mesh);layers++;
                }
                if(meshes.Count==0) throw new InvalidOperationException("Бетонное обрамление хаба не найдено.");
                foreach(var mesh in meshes)
                {
                    var colors=mesh.colors;
                    for(int i=0;i<colors.Length;i++) colors[i]=new Color(1,1,1,colors[i].a);
                    mesh.colors=colors;EditorUtility.SetDirty(mesh);
                }
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Debug.Log($"Оттенок бетона хаба: снято дополнительное затемнение вершин; слоёв {layers}, геометрий {meshes.Count}.");
            HubRangeAuthoring.RenderReferenceHub();
            PrototypeBuild.Build();
        }
    }
}
