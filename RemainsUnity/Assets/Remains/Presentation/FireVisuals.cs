using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    internal readonly struct FireAudioPoint
    {
        public readonly string Id,Level;
        public readonly Vector2 Position;
        public readonly float Gain;
        public FireAudioPoint(string id,string level,Vector2 position,float gain)
        {Id=id;Level=level;Position=position;Gain=gain;}
    }

    // Fire on the shown level (FireRules): flames standing along every burning patch — a few per patch, evenly with a
    // stable jitter, so that neighbouring patches merge into a strip — fading in and out; a torch is the unbroken bottle
    // lying with a small flame; a burning actor carries a flame. Pooled renderers, drawn each frame (animation only).
    public sealed class FireVisuals : MonoBehaviour
    {
        private const float FlameSpacing=.25f,FadeIn=.15f,FadeOut=.6f,TorchScale=.6f,ActorScale=.8f,DropletScale=.28f;
        private static readonly Quaternion LeftFlame=Quaternion.Euler(0,0,90),RightFlame=Quaternion.Euler(0,0,-90),
            CeilingFlame=Quaternion.Euler(0,0,180);
        private CoopSession session;private FireController fires;private GrenadeController grenades;private FireArtwork artwork;
        private readonly List<SpriteRenderer> flames=new();private int used;
        private readonly List<FirePatch> activePatches=new();
        private readonly List<FireAudioPoint> audioPoints=new();
        private readonly Dictionary<string,SpriteRenderer> bottles=new(System.StringComparer.Ordinal);
        private readonly Dictionary<string,Vector2> wickOffsets=new(System.StringComparer.Ordinal);
        private readonly HashSet<string> shownBottles=new(System.StringComparer.Ordinal);
        private readonly List<string> gone=new();
        private MaterialPropertyBlock properties;
        public int DrawnFlames=>used;
        internal event Action<IReadOnlyList<FireAudioPoint>> AudioFrame;
        public FireLightSources Lights {get;}=new();

        public void Initialize(CoopSession coop,FireController controller,GrenadeController flights)
        {session=coop;fires=controller;grenades=flights;artwork=FireArtwork.Load();properties=new MaterialPropertyBlock();}

        private void LateUpdate()
        {
            used=0;shownBottles.Clear();Lights.Begin();audioPoints.Clear();
            var world=session?.World;var viewer=session?.LocalActor;
            if(world!=null && viewer!=null && session.LevelTime.HasLocal(viewer.level))
            {
                double now=session.LevelTime.ReadLocal(viewer.level);
                foreach(var fire in world.fires)
                {
                    if(fire.level!=viewer.level || !FireRules.Burning(fire,now)) continue;
                    var patches=fires.Patches(fire);if(patches==null) continue;
                    double age=now-fire.start;
                    if(fire.torch && patches.Count>0)
                    {
                        var wick=Bottle(fire,patches[0].position);
                        float alpha=FlameGain((float)age,fire.fuel);
                        Lights.Fire(fire,patches,alpha,wick);
                        Flame(wick,TorchScale,(float)age,0,alpha);
                        audioPoints.Add(new FireAudioPoint(fire.id,fire.level,wick,.55f*alpha));
                        continue;
                    }
                    var paths=fire.dropletPaths;
                    bool airborne=false;
                    if(paths!=null)
                        for(int i=0;i<paths.Length;i++)
                            if(paths[i].TryPosition(age,out var position))
                            {
                                Flame(position,DropletScale,(float)age,i*7,1);
                                airborne=true;
                            }
                    var profile=FireRules.Profile(fire);
                    float patchSeconds=profile.PatchSeconds,lightGain=0;
                    activePatches.Clear();
                    for(int p=0;p<patches.Count;p++)
                    {
                        var patch=patches[p];
                        if(!patch.IsBurning(age,patchSeconds)) continue;
                        float patchAge=(float)(age-patch.landedAt),alpha=FlameGain(patchAge,patchSeconds);
                        activePatches.Add(patch);lightGain=Mathf.Max(lightGain,alpha);
                        int count=Mathf.Max(1,Mathf.RoundToInt(patch.radius*2/FlameSpacing));
                        for(int i=0;i<count;i++)
                        {
                            float offset=count==1?0:Mathf.Lerp(-patch.radius,patch.radius,i/(float)(count-1));
                            float jitter=Hash(fire.seed,p,i);
                            Flame(patch.OffsetAlongSurface(offset+(jitter-.5f)*FlameSpacing*.5f),
                                .7f+.4f*jitter,patchAge,p*7+i,alpha,patch.attachment);
                        }
                    }
                    Lights.Fire(fire,activePatches,lightGain);
                    if(profile.shatter && (airborne || activePatches.Count>0))
                        AddSplashAudio(fire,viewer.BodyPosition,lightGain);
                }
                // A lit bottle in flight burns at its neck.
                foreach(var flight in grenades.Flights)
                    if(flight.state.level==viewer.level && flight.state.fuel>0 && WeaponCatalog.Get(flight.state.weapon) is {IsFire:true,fire:{shatter:true}})
                    {
                        var wick=GrenadeArtwork.WickPosition(flight.position,flight.state.angle,flight.state.mirrored,WickOffset(flight.state.weapon));
                        Flame(wick,TorchScale*.8f,(float)now,flight.state.id.GetHashCode(),1);
                        Lights.Bottle(flight.state.id,wick);
                        audioPoints.Add(new FireAudioPoint(flight.state.id,flight.state.level,wick,.4f));
                    }
                foreach(var actor in world.actors)
                    if(actor.level==viewer.level && actor.burnLevel==actor.level && actor.burnUntil>now && ActorPresence.IsPresent(actor))
                    {Flame(actor.BodyPosition+Vector2.down*.3f,ActorScale,(float)now,actor.id.GetHashCode(),1);Lights.Actor(actor.id,actor.BodyPosition);}
            }
            Lights.End();
            AudioFrame?.Invoke(audioPoints);
            for(int i=used;i<flames.Count;i++) if(flames[i].enabled) flames[i].enabled=false;
            foreach(var pair in bottles) if(!shownBottles.Contains(pair.Key) && pair.Value.enabled) pair.Value.enabled=false;
            // A burnt-out torch's bottle goes with its fire.
            if(bottles.Count>shownBottles.Count && world!=null)
            {
                gone.Clear();
                foreach(var id in bottles.Keys) if(!world.fires.Exists(f=>f.id==id)) gone.Add(id);
                foreach(var id in gone) {Destroy(bottles[id].gameObject);bottles.Remove(id);}
            }
        }

        private static float FlameGain(float age,float lifetime)
            =>Mathf.Clamp01(age/FadeIn)*Mathf.Clamp01((lifetime-age)/FadeOut);

        private void AddSplashAudio(FireState fire,Vector2 listener,float gain)
        {
            var nearest=fire.Position;float best=float.PositiveInfinity;
            foreach(var patch in activePatches)
            {
                float distance=(patch.position-listener).sqrMagnitude;
                if(distance>=best) continue;
                best=distance;nearest=patch.position;
            }
            audioPoints.Add(new FireAudioPoint(fire.id,fire.level,nearest,activePatches.Count==0?.4f:.75f*gain));
        }

        private void Flame(Vector2 at,float scale,float age,int phase,float alpha,FireAttachment attachment=FireAttachment.Floor)
        {
            if(used==flames.Count)
            {
                var node=new GameObject("Пламя");node.transform.SetParent(transform,false);
                var created=node.AddComponent<SpriteRenderer>();created.sharedMaterial=artwork.Material;
                created.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Effect);flames.Add(created);
            }
            var renderer=flames[used++];
            var frames=artwork.Frames;int frame=(int)(age*artwork.FramesPerSecond+(phase&0xFFFF))%frames.Length;
            renderer.sprite=frames[frame<0?frame+frames.Length:frame];renderer.enabled=true;
            renderer.transform.position=new Vector3(at.x,at.y,0);renderer.transform.localScale=new Vector3(scale,scale,1);
            var orientation=attachment switch
            {
                FireAttachment.WallLeft=>LeftFlame,
                FireAttachment.WallRight=>RightFlame,
                FireAttachment.Ceiling=>CeilingFlame,
                _=>Quaternion.identity
            };
            if(renderer.transform.rotation!=orientation) renderer.transform.rotation=orientation;
            properties.SetFloat("_Emission",1);properties.SetColor("_Color",new Color(1,1,1,alpha));renderer.SetPropertyBlock(properties);
        }
        private Vector2 WickOffset(string weapon)
        {
            if(!wickOffsets.TryGetValue(weapon,out var offset))
            {
                offset=GrenadeArtwork.WickOffset(GrenadeArtwork.LoadPrefab(weapon));
                wickOffsets.Add(weapon,offset);
            }
            return offset;
        }

        private Vector2 Bottle(FireState fire,Vector2 at)
        {
            if(!bottles.TryGetValue(fire.id,out var bottle))
            {
                bottle=Instantiate(GrenadeArtwork.LoadPrefab(fire.weapon),transform);bottle.name="Горелка "+fire.id;
                bottle.transform.rotation=Quaternion.Euler(0,0,90);bottles.Add(fire.id,bottle);
            }
            shownBottles.Add(fire.id);bottle.enabled=true;bottle.transform.position=new Vector3(at.x,at.y+.06f,0);
            return GrenadeArtwork.WickPosition(bottle.transform.position,bottle.transform.eulerAngles.z,bottle.flipY,WickOffset(fire.weapon));
        }
        private static float Hash(int seed,int patch,int flame)
        {
            unchecked {uint h=(uint)(seed*73856093^patch*19349663^flame*83492791);h^=h>>13;h*=1274126177;h^=h>>16;return (h&0xFFFF)/65535f;}
        }
        private void OnDisable()
        {
            audioPoints.Clear();
            AudioFrame?.Invoke(audioPoints);
        }
        private void OnDestroy()
        {
            foreach(var pair in bottles) if(pair.Value!=null) Destroy(pair.Value.gameObject);
        }
    }
}
