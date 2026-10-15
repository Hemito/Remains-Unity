using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Content;

namespace Remains.Domain
{
    [Serializable] public sealed class HitRegionDefinition { public string id; public float x,y,radius; public string[] shapes; public HitRegionPose[] poses; }
    [Serializable] public sealed class HitRegionPose {public string pose;public Vector2[] centers;}
    [Serializable] public sealed class MovementHull { public float width,height; }
    [Serializable] public sealed class AnatomyDefinition { public string id,atlasArtwork,landingPose,fallPose; public MovementHull movement,crouchingMovement; public PoseSupportDefinition supportContacts; public HitRegionDefinition[] regions; }
    [Serializable] public sealed class AnatomyCatalog { public AnatomyDefinition[] anatomies; }
    public static class Anatomy
    {
        private static readonly Dictionary<string,AnatomyDefinition> definitions=new();
        private static readonly Dictionary<string,Vector2?[]> poseCenters=new();
        private static readonly Dictionary<string,PoseSupportContactData> supportContacts=new();
        public static void Load()
        {
            definitions.Clear();
            poseCenters.Clear();supportContacts.Clear();
            PonyAnimationData.Load();
            var asset=Resources.Load<TextAsset>("Content/anatomy");
            if(asset==null) throw new InvalidOperationException("Не найдены определения анатомии Content/anatomy.");
            // Missing optional definitions must remain null, rather than empty Unity-serialized classes.
            var catalog=Remains.Serialization.StateJson.FromJson<AnatomyCatalog>(asset.text);
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
                    if(region==null || string.IsNullOrEmpty(region.id) || !ids.Add(region.id) || !float.IsFinite(region.radius) || region.radius<=0 || !float.IsFinite(region.x) || !float.IsFinite(region.y))
                        throw new InvalidOperationException($"Некорректная область {region?.id} анатомии {definition.id}.");
                definitions.Add(definition.id,definition);
                if(definition.supportContacts!=null)
                {
                    if(!string.IsNullOrEmpty(definition.atlasArtwork))
                        throw new InvalidOperationException($"Контакты из составной графики не подходят атласной анатомии: anatomy={definition.id}, artwork={definition.atlasArtwork}.");
                    // Visible foot contacts and anatomical damage regions have different purposes.
                    // Each declares its own graphic parts; neither may restrict the other's coverage.
                    supportContacts.Add(definition.id,new PoseSupportContactData(definition.id,definition.supportContacts));
                }
                foreach(var optionalPose in new[]{definition.landingPose,definition.fallPose})
                if(!string.IsNullOrEmpty(optionalPose))
                {
                    if(!string.IsNullOrEmpty(definition.atlasArtwork))
                        AtlasAnimationData.Get(definition.atlasArtwork).Clip(optionalPose);
                    else PonyAnimationData.Duration(optionalPose);
                }
                foreach(var region in definition.regions)
                {
                    if(!string.IsNullOrEmpty(definition.atlasArtwork))
                    {
                        var atlas=AtlasAnimationData.Get(definition.atlasArtwork);
                        if(region.shapes?.Length>0 || region.poses==null || region.poses.Length!=atlas.clips.Length)
                            throw new InvalidOperationException($"Нет полного набора поз области: anatomy={definition.id}, region={region.id}.");
                        var trackPoses=new HashSet<string>();
                        foreach(var track in region.poses)
                        {
                            if(track==null || string.IsNullOrEmpty(track.pose) || !trackPoses.Add(track.pose))
                                throw new InvalidOperationException($"Повторная или отсутствующая поза области: anatomy={definition.id}, region={region.id}.");
                            var clip=atlas.Clip(track.pose);
                            if(track.centers==null || track.centers.Length!=clip.count)
                                throw new InvalidOperationException($"Число кадров области не совпадает с атласом: anatomy={definition.id}, region={region.id}, pose={track.pose}.");
                            var centers=new Vector2?[clip.count];
                            for(int frame=0;frame<centers.Length;frame++)
                            {
                                var center=track.centers[frame];
                                if(!float.IsFinite(center.x) || !float.IsFinite(center.y)) throw new InvalidOperationException("Некорректный центр области: "+region.id);
                                centers[frame]=center;
                            }
                            poseCenters.Add(definition.id+":"+region.id+":"+track.pose,centers);
                        }
                        continue;
                    }
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

        public static bool TrySupportContacts(ActorState actor,out IReadOnlyList<Vector2> contacts)
        {
            contacts=null;
            if(!supportContacts.TryGetValue(actor.anatomy,out var data)) return false;
            contacts=data.Sample(ActorPose.Clip(actor),PonyMotionFrames.Sample(actor,actor.poseTime));
            return true;
        }

        public static string FallPose(ActorState actor)=>definitions[actor.anatomy].fallPose;
        public static string LandingPose(ActorState actor)=>definitions[actor.anatomy].landingPose;
        public static float PoseDuration(ActorState actor,string pose)
        {
            var definition=definitions[actor.anatomy];
            if(string.IsNullOrEmpty(definition.atlasArtwork)) return PonyAnimationData.Duration(pose);
            var clip=AtlasAnimationData.Get(definition.atlasArtwork).Clip(pose);
            return clip.count/clip.framesPerSecond;
        }
        public static MovementHull Movement(string id)=>definitions.TryGetValue(id,out var definition)?definition.movement:
            throw new InvalidOperationException($"Нет определения движения анатомии {id}.");
        public static MovementHull Movement(ActorState actor)=>actor.crouched?Crouching(actor.anatomy):Movement(actor.anatomy);
        public static MovementHull Crouching(string id)=>definitions.TryGetValue(id,out var definition) && definition.crouchingMovement!=null?definition.crouchingMovement:
            throw new InvalidOperationException($"Не заданы габариты приседания: anatomy={id}.");
        public static float StanceHeightRatio(ActorState actor)=>Movement(actor).height/Movement(actor.anatomy).height;

        public static bool TryRegion(ActorState actor,string id,out Vector2 center,out float radius)
        {
            center=default;radius=0;
            if(!definitions.TryGetValue(actor.anatomy,out var definition)) return false;
            foreach(var region in definition.regions)
                if(region.id==id) return TryRegion(actor,definition,region,out center,out radius);
            return false;
        }
        private static bool TryRegion(ActorState actor,AnatomyDefinition definition,HitRegionDefinition region,out Vector2 center,out float radius)
        {
            center=default;radius=region.radius;
            var offset=new Vector2(region.x,region.y);
            string pose=ActorPose.Clip(actor);
            if(!string.IsNullOrEmpty(definition.atlasArtwork) || region.shapes!=null && region.shapes.Length>0)
            {
                int frame=!string.IsNullOrEmpty(definition.atlasArtwork)?AtlasAnimationData.Get(definition.atlasArtwork).Clip(pose).Sample(actor):
                    PonyMotionFrames.Sample(actor,actor.poseTime);
                if(!poseCenters.TryGetValue(actor.anatomy+":"+region.id+":"+pose,out var centers) || !centers[frame].HasValue)
                {Debug.LogWarning($"Нет графической привязки области {region.id}: actor={actor.id}, pose={actor.pose}, frame={frame}.");return false;}
                offset=centers[frame].Value;
            }
            center=actor.BodyPosition+new Vector2(offset.x*actor.facing,offset.y);
            return true;
        }

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
                if(!TryRegion(actor,definition,region,out var center,out float radius)) continue;
                float? entry=Remains.Content.WeaponCatalog.IsExplosive(attack.weapon)?
                    (Vector2.Distance(center,attack.End)<Remains.Content.WeaponCatalog.Get(attack.weapon).range?0f:(float?)null):
                    HitGeometry.CircleEntry(center,radius+attack.radius,attack.Origin,attack.End);
                if(entry.HasValue && entry.Value<fraction) {fraction=entry.Value;nearest=region.id;}
            }
            return nearest;
        }
    }
}
