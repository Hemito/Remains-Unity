using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Explicit room preparation only. Saved body geometry is the version marker: already matching bodies do nothing.
    public static class DroppedItemShapeUpgrade
    {
        private static readonly HashSet<(string world,string id,string item)> warnedMissingShapes=new();
        private sealed class Change
        {
            public string id,item,level;public float x,y,width,height,anchor,angle;public bool mirrored;
            public Vector2 size;
        }
        public sealed class Plan
        {
            private readonly string worldId;private readonly Change[] changes;
            private Plan(string id,Change[] changes) {worldId=id;this.changes=changes;}
            public bool Apply(WorldState world)
            {
                if(world==null || world.worldId!=worldId) throw new InvalidOperationException("Изменился мир обновления форм предметов.");
                var targets=new List<(ContainerState body,Change change)>();
                foreach(var change in changes)
                {
                    var matches=world.containers.Where(c=>c.id==change.id).ToArray();
                    if(matches.Length!=1) throw new InvalidOperationException("Нет единственного брошенного предмета обновления: "+change.id);
                    var body=matches[0];
                    if(body.level!=change.level || body.presentationItem!=change.item || body.x!=change.x || body.y!=change.y ||
                        body.width!=change.width || body.height!=change.height || body.anchorHeight!=change.anchor ||
                        body.presentationAngle!=change.angle || body.presentationMirrored!=change.mirrored)
                        throw new InvalidOperationException("Изменилась поза предмета до обновления формы: "+change.id);
                    targets.Add((body,change));
                }
                if(targets.Count==0)return false;
                if(world.revision==long.MaxValue) throw new InvalidOperationException("Исчерпана ревизия мира при обновлении формы предметов.");
                foreach(var target in targets)
                {
                    float feet=target.body.y-target.body.anchorHeight;
                    target.body.width=target.change.size.x;target.body.height=target.change.size.y;
                    target.body.anchorHeight=target.change.size.y*.5f;target.body.y=feet+target.body.anchorHeight;
                    WorldChanges.Mark(world,target.body);
                }
                world.revision++;return true;
            }
            internal static Plan Create(WorldState world,string level)=>new(world.worldId,Changes(world,level).ToArray());
        }
        public static bool NeedsApply(WorldState world,string level)=>world!=null && !string.IsNullOrEmpty(level) && Changes(world,level).Any();
        public static Plan Prepare(WorldState world,string level)=>NeedsApply(world,level)?Plan.Create(world,level):null;
        private static IEnumerable<Change> Changes(WorldState world,string level)
        {
            foreach(var body in world.containers)
            {
                if(body.level!=level || body.definition!="loose-item" || body.presentationItem==null || MineRules.PlacedWeapon(body.presentationItem)!=null) continue;
                if(!DroppedItemShapeCatalog.TryGet(body.presentationItem,out var shape))
                {
                    if(warnedMissingShapes.Add((world.worldId,body.id,body.presentationItem)))
                        Debug.LogWarning($"Форма сохранённого предмета не обновлена: world={world.worldId}, id={body.id}, level={body.level}, definition={body.definition}, item={body.presentationItem}, size=({body.width}, {body.height}), anchorHeight={body.anchorHeight}; нет подготовленной непрозрачной формы. Исходное физическое тело сохранено без подстановки.");
                    continue;
                }
                var size=shape.WorldBounds(body.presentationAngle,body.presentationMirrored).size;
                if(Mathf.Abs(body.width-size.x)<.000001f && Mathf.Abs(body.height-size.y)<.000001f && Mathf.Abs(body.anchorHeight-size.y*.5f)<.000001f)continue;
                if(!float.IsFinite(body.x) || !float.IsFinite(body.y) || !float.IsFinite(body.anchorHeight))
                    throw new InvalidOperationException("Нечисловая поза сохранённого предмета: "+body.id);
                yield return new Change {id=body.id,item=body.presentationItem,level=body.level,x=body.x,y=body.y,width=body.width,height=body.height,
                    anchor=body.anchorHeight,angle=body.presentationAngle,mirrored=body.presentationMirrored,size=size};
            }
        }
    }
}
