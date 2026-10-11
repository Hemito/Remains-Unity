using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // Explicit content loading, independent of the local player's visible light window.
    public static class LampContent
    {
        public static List<LampState> Read(ITemplateLevel level)
        {
            var table=RoomLightingTable.Read(level.Lighting.text,level.Lighting.name);
            var result=new List<LampState>();
            for(int i=0;i<level.Rooms.Count;i++)
            {
                var room=level.Rooms[i];
                foreach(var picture in level.RoomInstances.GetChild(i).GetComponentsInChildren<RoomSpriteAuthoring>(true))
                {
                    if(picture.Kind!=RoomSpriteKind.Decor || !table.Lamps.ContainsKey(picture.Definition)) continue;
                    var decor=room.Template.Decor[picture.Index];
                    if(string.IsNullOrWhiteSpace(decor.Id)) throw new InvalidOperationException($"Лампа без ID: {level.Level}, {room.Template.Template}, декор {picture.Index}.");
                    var renderer=picture.GetComponent<SpriteRenderer>();
                    if(renderer.sprite==null) throw new InvalidOperationException($"Нет изображения лампы: {picture.Definition}, {decor.Id}.");
                    var local=renderer.sprite.bounds;
                    var matrix=renderer.transform.localToWorldMatrix;
                    Vector3 a=matrix.MultiplyPoint3x4(new Vector3(local.min.x,local.min.y,0));
                    Vector3 b=matrix.MultiplyPoint3x4(new Vector3(local.max.x,local.min.y,0));
                    Vector3 c=matrix.MultiplyPoint3x4(new Vector3(local.min.x,local.max.y,0));
                    Vector3 d=matrix.MultiplyPoint3x4(new Vector3(local.max.x,local.max.y,0));
                    var min=Vector3.Min(Vector3.Min(a,b),Vector3.Min(c,d));
                    var max=Vector3.Max(Vector3.Max(a,b),Vector3.Max(c,d));
                    var lamp=new LampState{id=$"{level.Level}:{room.X},{room.Y}:{decor.Id}",level=level is HubTemplateLevel?"hub":level.Level,
                        x=min.x,y=min.y,width=max.x-min.x,height=max.y-min.y};
                    lamp.Validate();result.Add(lamp);
                }
            }
            return result;
        }

        public static List<LampState> Read(WorldState world,string level)
        {
            if(FactoryContentSources.IsGenerated(world,level))
            {
                FactoryContentSources.Prepare(world,level);
                var root=FactoryContentSources.Require(world,level).GetComponent<FactoryLevelRoot>();
                return Read(new FactoryTemplateLevel(root));
            }
            var source=RoomAuthoring.Load(level,world);
            if(!source.TryGetComponent<HubTemplateLevel>(out _)) return new List<LampState>();
            var instance=UnityEngine.Object.Instantiate(source);
            instance.gameObject.SetActive(false);
            try
            {
                var hub=instance.GetComponent<HubTemplateLevel>();
                hub.Build(source.CreateLayout(false));
                return Read(hub);
            }
            finally
            {
                if(Application.isPlaying) UnityEngine.Object.Destroy(instance.gameObject);
                else UnityEngine.Object.DestroyImmediate(instance.gameObject);
            }
        }
    }
}
