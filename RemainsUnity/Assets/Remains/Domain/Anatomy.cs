using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class HitRegionDefinition { public string id; public float x,y,radius; public string[] shapes; }
    [Serializable] public sealed class MovementHull { public float width,height; }
    [Serializable] public sealed class AnatomyDefinition { public string id; public MovementHull movement,crouchingMovement; public HitRegionDefinition[] regions; }
    [Serializable] public sealed class AnatomyCatalog { public AnatomyDefinition[] anatomies; }
    public static class Anatomy
    {
        private static readonly Dictionary<string,AnatomyDefinition> definitions=new();
        private static readonly Dictionary<string,Vector2?[]> poseCenters=new();
        public static void Load()
        {
            definitions.Clear();
            poseCenters.Clear();
            PonyAnimationData.Load();
            var asset=Resources.Load<TextAsset>("Content/anatomy");
            if(asset==null) throw new InvalidOperationException("Не найдены определения анатомии Content/anatomy.");
            var catalog=JsonUtility.FromJson<AnatomyCatalog>(asset.text);
            foreach(var definition in catalog.anatomies)
            {
                if(definition.movement==null || !float.IsFinite(definition.movement.width) || !float.IsFinite(definition.movement.height) ||
                    definition.movement.width<=0 || definition.movement.height<=0)
                    throw new InvalidOperationException($"Некорректный габарит движения анатомии {definition.id}.");
                var ids=new HashSet<string>();
                if(definition.crouchingMovement!=null && (!float.IsFinite(definition.crouchingMovement.width) || !float.IsFinite(definition.crouchingMovement.height) ||
                    definition.crouchingMovement.width<=0 || definition.crouchingMovement.height<=0 || definition.crouchingMovement.height>definition.movement.height))
                    throw new InvalidOperationException($"Некорректные габариты приседания: anatomy={definition.id}.");
                foreach(var region in definition.regions)
                    if(string.IsNullOrEmpty(region.id) || !ids.Add(region.id) || region.radius<=0)
                        throw new InvalidOperationException($"Некорректная область {region.id} анатомии {definition.id}.");
                definitions.Add(definition.id,definition);
                foreach(var region in definition.regions)
                {
                    if(region.shapes==null || region.shapes.Length==0) continue;
                    foreach(var clip in PonyAnimationData.Library.clips)
                    {
                        var centers=new Vector2?[clip.frames.Length];
                        for(int frame=0;frame<centers.Length;frame++)
                            if(PonyAnimationData.TryCenter(clip.id,frame,region.shapes,out var center)) centers[frame]=center;
                            else Debug.LogWarning($"Нет графической привязки области {region.id}: anatomy={definition.id}, pose={clip.id}, frame={frame}.");
                        poseCenters.Add(definition.id+":"+region.id+":"+clip.id,centers);
                    }
                }
            }
        }

        public static MovementHull Movement(string id)=>definitions.TryGetValue(id,out var definition)?definition.movement:
            throw new InvalidOperationException($"Нет определения движения анатомии {id}.");
        public static MovementHull Movement(ActorState actor)=>actor.crouched?Crouching(actor.anatomy):Movement(actor.anatomy);
        public static MovementHull Crouching(string id)=>definitions.TryGetValue(id,out var definition) && definition.crouchingMovement!=null?definition.crouchingMovement:
            throw new InvalidOperationException($"Не заданы габариты приседания: anatomy={id}.");
        public static float StanceHeightRatio(ActorState actor)=>Movement(actor).height/Movement(actor.anatomy).height;

        public static string Contact(ActorState actor,AttackEvent attack)=>Contact(actor,attack,out _);

        public static string Contact(ActorState actor,AttackEvent attack,out float fraction)
        {
            fraction=float.PositiveInfinity;
            string nearest=null;
            if(!definitions.TryGetValue(actor.anatomy,out var definition))
            {
                Debug.LogWarning($"Отсутствует анатомия {actor.anatomy} у {actor.id}; попадание пропущено.");
                return null;
            }
            foreach(var region in definition.regions)
            {
                var offset=new Vector2(region.x,region.y);
                string pose=ActorPose.Clip(actor);
                if(region.shapes!=null && region.shapes.Length>0)
                {
                    int frame=PonyAnimationData.FrameIndex(pose,actor.poseTime,actor.down);
                    if(!poseCenters.TryGetValue(actor.anatomy+":"+region.id+":"+pose,out var centers) || !centers[frame].HasValue)
                    {Debug.LogWarning($"Нет графической привязки области {region.id}: actor={actor.id}, pose={actor.pose}, frame={frame}.");continue;}
                    offset=centers[frame].Value;
                }
                var center=actor.Position+new Vector2(offset.x*actor.facing,offset.y);
                float? entry=attack.weapon=="grenade"?
                    (Vector2.Distance(center,attack.End)<3?0f:(float?)null):
                    HitGeometry.CircleEntry(center,region.radius+attack.radius,attack.Origin,attack.End);
                if(entry.HasValue && entry.Value<fraction) {fraction=entry.Value;nearest=region.id;}
            }
            return nearest;
        }
    }
}
