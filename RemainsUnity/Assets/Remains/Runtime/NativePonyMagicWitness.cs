using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // Explicit native diagnostic capture, after the ordinary renderer LateUpdate.
    // Expected activity comes from the action phase, never from the presentation source.
    internal sealed class NativePonyMagicWitness
    {
        private sealed class Identity
        {public int renderer,material,mesh,frame;public string clip;}
        private readonly Dictionary<string,Identity> identities=new(StringComparer.Ordinal);
        private MaterialPropertyBlock block;

        public bool FixtureVisible(PrototypeRuntime runtime,ActorState actor,Vector2 feet,int magic)
        {
            if(actor==null || actor.appearance==null || actor.appearance.magic!=magic || actor.level!="hub" ||
                Vector2.Distance(actor.Position,feet)>.0001f) return false;
            var view=runtime.FindActorVisual(actor.id) as PonyVisual;
            if(view==null || !view.gameObject.activeInHierarchy || view.ActorId!=actor.id || view.UsesMagicPreview ||
                !view.TryGetDrawnBounds(out var bounds) || !view.TryGetMagicRenderer(out var renderer)) return false;
            var body=actor.BodyPosition;
            if(view.transform.position!=new Vector3(body.x,body.y,0)) return false;
            var min=runtime.WorldCamera.WorldToViewportPoint(bounds.min);var max=runtime.WorldCamera.WorldToViewportPoint(bounds.max);
            if(min.z<=0 || min.x<0 || min.y<0 || max.x>1 || max.y>1) return false;
            block??=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);var color=block.GetColor("_Color");var expected=PonyAppearance.FromRgb(magic);
            return block.HasColor("_Color") && Mathf.Abs(color.r-expected.r)<.00001f && Mathf.Abs(color.g-expected.g)<.00001f &&
                Mathf.Abs(color.b-expected.b)<.00001f;
        }

        public Record Capture(PrototypeRuntime runtime,ActorState actor,float opacity,PonyMagicAction action,string source=null,float mass=0)
        {
            Require(actor!=null && actor.player && actor.connected && !actor.down && actor.health>0 &&
                actor.level==runtime.Session.LocalActor.level,"Нет активного участника для actual horn: actor="+actor?.id);
            var view=runtime.FindActorVisual(actor.id) as PonyVisual;
            Require(runtime.LevelPresentationReady && view!=null && view.gameObject.activeInHierarchy &&
                view.ActorId==actor.id && !view.UsesMagicPreview,"Не подключён actual world horn: actor="+actor.id);
            Require(view.DrawnClip==ActorPose.Clip(actor) && view.DrawnFrame==PonyMotionFrames.Sample(actor,actor.poseTime),
                "Рог не относится к последнему фактическому кадру: actor="+actor.id+", clip="+view.DrawnClip+", frame="+view.DrawnFrame);
            var appearance=PonyAppearance.Snapshot(actor);
            var plan=view.CurrentDrawPlan;
            var parts=(plan.HasEquipment?plan.Frames(view.DrawnClip):PonyRenderSelection.Frames(appearance,view.DrawnClip))[view.DrawnFrame];
            PonyRenderPart role=null;int roles=0;
            foreach(var part in parts) if(PonyMagicRenderRoles.Contains(part)) {role=part;roles++;}
            MeshRenderer renderer=null;
            Require(roles==1 && role.visible && view.TryGetMagicRenderer(out renderer),
                "Нет единственной подтверждённой роли рога: actor="+actor.id+", roles="+roles);
            var filter=renderer.GetComponent<MeshFilter>();
            Require(filter!=null && filter.sharedMesh==role.mesh && renderer.sharedMaterial==role.material,
                "Actual horn не использует подготовленные mesh/material: actor="+actor.id);
            var actual=view.DrawnMagicActivity;
            Require(actual.Opacity==opacity && actual.Action==action && actual.SourceId==source && actual.MassKg==mass,
                "Actual horn activity не соответствует обычному действию: actor="+actor.id+", expected="+action+"/"+source+"/"+opacity+
                ", actual="+actual.Action+"/"+actual.SourceId+"/"+actual.Opacity+", mass="+actual.MassKg);
            block??=new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);var color=block.GetColor("_Color");var expected=PonyAppearance.FromRgb(appearance.Magic);
            Require(block.HasColor("_Color") && block.HasFloat("_Emission") && renderer.enabled==(opacity>0) &&
                !renderer.forceRenderingOff && Mathf.Abs(color.r-expected.r)<.00001f && Mathf.Abs(color.g-expected.g)<.00001f &&
                Mathf.Abs(color.b-expected.b)<.00001f && color.a==opacity && block.GetFloat("_Emission")==1,
                "Видимость/RGB/alpha/emission actual horn не совпали: actor="+actor.id+", color="+color+", opacity="+opacity);
            var body=actor.BodyPosition;
            Require(view.transform.position==new Vector3(body.x,body.y,0) && view.transform.localScale==new Vector3(actor.facing,1,1),
                "Роль рога изменила общий body root/facing: actor="+actor.id);
            int rendererId=renderer.GetInstanceID(),materialId=renderer.sharedMaterial.GetInstanceID(),meshId=filter.sharedMesh.GetInstanceID();
            if(identities.TryGetValue(actor.id,out var previous))
            {
                Require(previous.renderer==rendererId && previous.material==materialId &&
                    (previous.clip!=view.DrawnClip || previous.frame!=view.DrawnFrame || previous.mesh==meshId),
                    "Обычный select/Q/release пересоздал renderer/material/неизменный mesh рога: actor="+actor.id);
            }
            identities[actor.id]=new Identity {renderer=rendererId,material=materialId,mesh=meshId,clip=view.DrawnClip,frame=view.DrawnFrame};
            var bounds=renderer.bounds;var min=runtime.WorldCamera.WorldToViewportPoint(bounds.min);var max=runtime.WorldCamera.WorldToViewportPoint(bounds.max);
            Require(min.z>0 && min.x>=0 && min.y>=0 && max.x<=1 && max.y<=1,
                "Роль рога участника вне actual кадра: actor="+actor.id+", min="+min+", max="+max);
            return new Record {world=runtime.Session.World.worldId,actor=actor.id,owner=actor.owner,level=actor.level,generation=actor.generation,
                lifecycle=actor.lifecycleSequence,selectionRevision=actor.weaponSelectionRevision,selectedWeapon=actor.equippedWeaponItem,
                selectedItem=actor.heldItemId,magic=appearance.Magic,clip=view.DrawnClip,frame=view.DrawnFrame,time=view.DrawnTime,
                roleShape=role.sourceShape,rolePath=role.sourcePath,preview=view.UsesMagicPreview,enabled=renderer.enabled,
                action=actual.Action.ToString(),source=actual.SourceId,massKg=actual.MassKg,opacity=actual.Opacity,actualColor=color,
                emission=block.GetFloat("_Emission"),renderer=rendererId,material=materialId,mesh=meshId,drawOrder=renderer.sortingOrder,
                root=view.transform.position,scale=view.transform.localScale,minimum=bounds.min,maximum=bounds.max,viewportMinimum=min,viewportMaximum=max};
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Свидетельство рога: "+reason);}

        [Serializable] public sealed class Record
        {
            public string world,actor,owner,level,selectedWeapon,selectedItem,clip,roleShape,rolePath,action,source;
            public int generation,lifecycle,magic,frame,renderer,material,mesh,drawOrder;
            public long selectionRevision;
            public float time,massKg,opacity,emission;
            public bool preview,enabled;
            public Color actualColor;
            public Vector3 root,scale,minimum,maximum,viewportMinimum,viewportMaximum;
        }
    }
}
