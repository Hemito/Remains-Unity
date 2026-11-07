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
        private sealed class ProneProfile
        {
            public readonly float lift;
            public readonly MovementHull hull;
            public readonly Rect[] regions;
            public ProneProfile(float lift,params Rect[] regions)
            {
                this.lift=lift;this.regions=regions;
                hull=new MovementHull();
                foreach(var region in regions)
                {
                    hull.width=Mathf.Max(hull.width,2*Mathf.Max(-region.xMin,region.xMax));
                    hull.height=Mathf.Max(hull.height,region.yMax);
                }
            }
        }
        // Conservative leg, torso and head bounds of the last source down frame, measured
        // relative to the sprite pivot and lifted to the support plane (50 px/m).
        private static readonly ProneProfile lightRaiderProne=new(.2f,
            new Rect(-1.2f,0,.6f,.72f),new Rect(-.6f,0,1,.78f),new Rect(.4f,0,.8f,.96f));
        private static readonly ProneProfile heavyRaiderProne=new(.12f,
            new Rect(-1.18f,0,.48f,.46f),new Rect(-.7f,0,1.2f,.68f),new Rect(.5f,0,.68f,.9f));
        private static readonly Dictionary<string,AnatomyDefinition> definitions=new();
        private static readonly Dictionary<string,Vector2?[]> poseCenters=new();
        private static readonly Dictionary<string,PoseSupportContactData> supportContacts=new();
        public static void Load()
        {
            definitions.Clear();
            poseCenters.Clear();supportContacts.Clear();
            PonyAnimationData.Load();
            var asset=GameAssets.Load<TextAsset>("Content/anatomy");
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

        // Atlas bodies (raiders) have no drawn foot parts: nothing to check, as opposed to a pose missing its contacts.
        public static bool DeclaresSupportContacts(string anatomy)=>supportContacts.ContainsKey(anatomy);
        public static bool TrySupportContacts(ActorState actor,out IReadOnlyList<Vector2> contacts)
        {
            contacts=null;
            if(!supportContacts.TryGetValue(actor.anatomy,out var data) || !BedRest.HasFootSupport(ActorPose.Clip(actor))) return false;
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
        public static MovementHull Movement(ActorState actor)=>ActorPose.IsTacticallyProne(actor)?Prone(actor).hull:
            actor.crouched?Crouching(actor.anatomy):Movement(actor.anatomy);
        private static ProneProfile Prone(ActorState actor)=>definitions[actor.anatomy].atlasArtwork switch
        {
            "Original/Raider/artwork" or "Original/Creatures/raider5/artwork"=>lightRaiderProne,
            "Original/Creatures/raider7/artwork"=>heavyRaiderProne,
            _=>null
        };
        public static float ProneLift(ActorState actor)=>ActorPose.IsTacticallyProne(actor)?Prone(actor).lift:0;
        public static bool CanLieProne(ActorState actor,RoomLayout room)=>Prone(actor) is { } prone &&
            ActorTerrainMotion.IsClear(room,prone.hull,actor.Position);

        // The same prone contact is used for the tactical prediction and for live projectiles.
        public static float? ProneContact(ActorState actor,Vector2 start,Vector2 end,float radius=0)
        {
            var prone=Prone(actor);
            if(prone==null) return null;
            var body=actor.BodyPosition;
            start-=body;end-=body;
            start.x*=actor.facing;end.x*=actor.facing;
            float nearest=float.PositiveInfinity;
            foreach(var region in prone.regions)
                if(SurfaceContact.Entry(region,start,end,radius) is float entry) nearest=Mathf.Min(nearest,entry);
            return float.IsPositiveInfinity(nearest)?null:nearest;
        }
        // A body without a crouching hull (the creatures and raiders) cannot crouch.
        public static bool CanCrouch(string id)=>definitions.TryGetValue(id,out var definition) && definition.crouchingMovement!=null;
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
        private static bool TryRegion(ActorState actor,AnatomyDefinition definition,HitRegionDefinition region,out Vector2 center,out float radius,bool standing=false)
        {
            center=default;radius=region.radius;
            var offset=new Vector2(region.x,region.y);
            string pose=standing?"idle":ActorPose.Clip(actor);
            if(!standing && ActorPose.IsTacticallyProne(actor))
            {
                var bounds=Prone(actor).regions[region.id=="legs"?0:region.id=="head"?2:1];
                offset=bounds.center;radius=bounds.height*.5f;
            }
            else if(!string.IsNullOrEmpty(definition.atlasArtwork) || region.shapes!=null && region.shapes.Length>0)
            {
                int frame=standing?0:!string.IsNullOrEmpty(definition.atlasArtwork)?AtlasAnimationData.Get(definition.atlasArtwork).Clip(pose).Sample(actor):
                    PonyMotionFrames.Sample(actor,actor.poseTime);
                if(!poseCenters.TryGetValue(actor.anatomy+":"+region.id+":"+pose,out var centers) || !centers[frame].HasValue)
                {Debug.LogWarning($"Нет графической привязки области {region.id}: actor={actor.id}, pose={actor.pose}, frame={frame}.");return false;}
                offset=centers[frame].Value;
            }
            center=actor.BodyPosition+new Vector2(offset.x*actor.facing,offset.y);
            return true;
        }

        public static string Contact(ActorState actor,AttackEvent attack)=>Contact(actor,attack,out _);

        // How far a blast at this point is from the body: its nearest region centre (the same centres a blast's contact
        // measures; the original measured from the unit's middle). Infinity without an anatomy.
        public static float BlastDistance(ActorState actor,Vector2 point)
            =>BlastDistance(actor,point,null);

        internal static float BlastDistance(ActorState actor,Vector2 point,Predicate<Vector2> reachable)
        {
            if(!definitions.TryGetValue(actor.anatomy,out var definition))
            {
                Debug.LogWarning($"Отсутствует анатомия {actor.anatomy} у {actor.id}; расстояние до взрыва не определено.");
                return float.PositiveInfinity;
            }
            float nearest=float.PositiveInfinity;
            foreach(var region in definition.regions)
                if(TryRegion(actor,definition,region,out var center,out _) && (reachable==null || reachable(center)))
                    nearest=Mathf.Min(nearest,Vector2.Distance(center,point));
            return nearest;
        }

        public static string Contact(ActorState actor,AttackEvent attack,out float fraction)
        {
            bool blast=Remains.Content.WeaponCatalog.IsExplosive(attack.weapon);
            return Contact(actor,attack.Origin,attack.End,attack.radius,out fraction,
                blast?Remains.Content.WeaponCatalog.Get(attack.weapon).range:null);
        }

        public static string Contact(ActorState actor,Vector2 start,Vector2 end,float radius,out float fraction,float? blastRange=null,bool standing=false)
        {
            fraction=float.PositiveInfinity;
            string nearest=null;
            if(!definitions.TryGetValue(actor.anatomy,out var definition))
            {
                Debug.LogWarning($"Отсутствует анатомия {actor.anatomy} у {actor.id}; попадание пропущено.");
                return null;
            }
            if(!blastRange.HasValue && !standing && ActorPose.IsTacticallyProne(actor))
            {
                if(ProneContact(actor,start,end,radius) is not float entry) return null;
                fraction=entry;return "torso";
            }
            foreach(var region in definition.regions)
            {
                if(!TryRegion(actor,definition,region,out var center,out float regionRadius,standing)) continue;
                float? entry=blastRange.HasValue?(Vector2.Distance(center,end)<blastRange.Value?0f:(float?)null):
                    HitGeometry.CircleEntry(center,regionRadius+radius,start,end);
                if(entry.HasValue && entry.Value<fraction) {fraction=entry.Value;nearest=region.id;}
            }
            return nearest;
        }
    }
}
