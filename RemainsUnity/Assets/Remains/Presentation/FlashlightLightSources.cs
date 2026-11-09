using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Presentation
{
    // An ordinary selected item. Its existing held pose/aim is the source of truth on every peer.
    // It supplies only direct light through WorldLightFields, never world fill or reflected light.
    public sealed class FlashlightLightSources
    {
        public const string ItemDefinition="item:test-flashlight";
        public const float Reach=7.5f,EmitterWidth=.18f,InnerHalfAngle=15,OuterHalfAngle=30;
        public static readonly Color Radiance=new(6,5.7f,5.1f,1);
        private sealed class Entry {public string Id;public readonly Vector4[] Points=new Vector4[1];}
        private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> gone=new();
        public void Begin()=>retained.Clear();
        public void Append(ActorState actor,List<WorldLightEmission> output,Rect? visibleArea=null)
        {
            if(output==null)throw new ArgumentNullException(nameof(output));
            if(actor==null || !ActorPresence.IsPresent(actor) || ActorPresence.Hidden(actor) || ActorSessionRules.IsPassive(actor) ||
                actor.health<=0 || !actor.heldItemPoseValid)return;
            var item=HeldItemSelection.Selected(actor);
            if(item?.definition!=ItemDefinition || InventoryAvailability.Count(actor,item)<=0)return;
            if(string.IsNullOrWhiteSpace(actor.id) || !float.IsFinite(actor.weaponAngle) ||
                !float.IsFinite(actor.heldItemPoseX) || !float.IsFinite(actor.heldItemPoseY))
                throw new InvalidOperationException($"Фонарик: некорректная подтверждённая поза, actor={actor.id}, level={actor.level}.");
            Vector2 position=HeldItemPose.Mount(actor);
            if(!float.IsFinite(position.x) || !float.IsFinite(position.y))
                throw new InvalidOperationException($"Фонарик: некорректная точка удерживания, actor={actor.id}, position={position}.");
            var affected=new Rect(position-Vector2.one*Reach,Vector2.one*(Reach*2));
            if(visibleArea.HasValue && !visibleArea.Value.Overlaps(affected))return;
            retained.Add(actor.id);
            if(!entries.TryGetValue(actor.id,out var entry))
            {entry=new Entry{Id="flashlight:"+actor.id};entries.Add(actor.id,entry);}
            entry.Points[0]=new Vector4(position.x,position.y,1,0);
            float angle=actor.weaponAngle*Mathf.Deg2Rad;
            var cone=new LightEmissionCone(new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)),InnerHalfAngle,OuterHalfAngle);
            output.Add(new WorldLightEmission(entry.Id,entry.Points,new Rect(position-Vector2.one*.02f,Vector2.one*.04f),
                Radiance,EmitterWidth,Reach,cone:cone));
        }
        public void End()
        {
            gone.Clear();foreach(string id in entries.Keys)if(!retained.Contains(id))gone.Add(id);
            foreach(string id in gone)entries.Remove(id);
        }
        public void Clear(){entries.Clear();retained.Clear();gone.Clear();}
    }
}
