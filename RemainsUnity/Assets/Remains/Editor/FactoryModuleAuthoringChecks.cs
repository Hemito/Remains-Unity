using System;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Native authoring checks only. Never mutates the saved prefab or gameplay geometry.
    public static class FactoryModuleAuthoringChecks
    {
        public static void Run(GameObject saved)
        {
            var room=saved.GetComponent<RoomAuthoring>();
            if(room==null || !room.HasAuthoredArtwork || saved.GetComponent<RoomPhysicalContent>()==null || saved.GetComponent<RoomDoorContent>()==null)
                throw new InvalidOperationException("Модуль завода: отсутствует сохранённая независимая геометрия/оформление/расстановка.");
            foreach(var layer in saved.GetComponentsInChildren<RoomArtworkAuthoring>())
            {
                var mesh=layer.GetComponent<MeshFilter>()?.sharedMesh;var renderer=layer.GetComponent<MeshRenderer>();
                if(mesh==null || !AssetDatabase.Contains(mesh) || renderer?.sharedMaterial==null || !AssetDatabase.Contains(renderer.sharedMaterial))
                    throw new InvalidOperationException("Модуль завода: рисунок не сохранён отдельными mesh/material asset, layer="+layer.name);
            }
            var clone=UnityEngine.Object.Instantiate(saved);
            try
            {
                var copy=clone.GetComponent<RoomAuthoring>();
                var surfaces=clone.GetComponentsInChildren<RoomSurfaceAuthoring>();
                foreach(var surface in surfaces)
                {
                    var layers=surface.GetComponentsInChildren<RoomArtworkAuthoring>();
                    if(layers.Length==0) throw new InvalidOperationException("Модуль завода: поверхность отделена от её исходной графики, surface="+surface.name);
                    var before=layers.Select(a=>a.Read(copy)).ToArray();var support=surface.Read(copy);
                    surface.transform.localPosition+=new Vector3(.125f,.25f,0);
                    var moved=surface.Read(copy);
                    if(Mathf.Abs(moved.x-support.x-.125f)>.0001f || Mathf.Abs(moved.y-support.y-.25f)>.0001f)
                        throw new InvalidOperationException("Модуль завода: Transform перестал быть источником геометрии, surface="+surface.name);
                    for(int i=0;i<layers.Length;i++)
                    {
                        var after=layers[i].Read(copy);
                        if(Mathf.Abs(after.x-before[i].x-.125f)>.0001f || Mathf.Abs(after.y-before[i].y-.25f)>.0001f || Mathf.Abs(after.width-before[i].width)+Mathf.Abs(after.height-before[i].height)>.0001f)
                            throw new InvalidOperationException("Модуль завода: рисунок не следует своей опоре или теряет отдельный offset, surface="+surface.name+", texture="+after.texture);
                    }
                    surface.transform.localPosition-=new Vector3(.125f,.25f,0);
                }
                foreach(var ladder in clone.GetComponentsInChildren<RoomLadderAuthoring>())
                    if(!ladder.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name=="Пристенная лестница"))
                        throw new InvalidOperationException("Модуль завода: rung не следует единственному ladder anchor, ladder="+ladder.name);
                var placements=clone.GetComponentsInChildren<RoomPhysicalPlacement>().Select(p=>p.Read(copy)).ToArray();
                if(placements.Select(p=>p.id).Distinct().Count()!=placements.Length)
                    throw new InvalidOperationException("Модуль завода: повтор stable ID физической расстановки.");
            }
            finally {UnityEngine.Object.DestroyImmediate(clone);}
            Debug.Log("Модуль завода: native проверены сохранённые mesh/material, единые surface/ladder anchors и отдельные offsets графики; clone удалён, saved prefab не изменён.");
        }
    }
}
