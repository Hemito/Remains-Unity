using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [Serializable] public sealed class AtlasActorClip
    {public string pose;public int row,count;public float framesPerSecond;public bool loop;}
    [Serializable] public sealed class AtlasActorArtwork
    {
        public string resource;public int width,height,cellWidth,cellHeight;
        public float pixelsPerUnit,pivotX,pivotY;public bool facesLeft;
        public AtlasActorClip[] clips;
    }
    public sealed class AtlasActorVisual : ActorVisual
    {
        private sealed class Clip
        {
            public Sprite[] frames;public float rate;public bool loop;
            public Sprite Sample(float time)
            {
                int index=Mathf.FloorToInt(Mathf.Max(0,time)*rate);
                return frames[loop?index%frames.Length:Mathf.Min(index,frames.Length-1)];
            }
        }
        private sealed class Artwork
        {public string resource;public bool facesLeft;public readonly Dictionary<string,Clip> clips=new(StringComparer.Ordinal);}
        private static readonly Dictionary<string,Artwork> cache=new(StringComparer.Ordinal);
        private ActorState state;
        private SpriteRenderer view;
        private Artwork artwork;
        private string artworkKey,missingPose;
        public string Resource=>artwork?.resource;
        public override string ActorId=>state?.id;

        public override void Bind(ActorState actor)
        {
            state=actor;string key=ActorTypes.Get(actor.actorType).artwork;
            if(view==null) {view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=20;}
            if(artworkKey!=key)
            {
                if(string.IsNullOrEmpty(key)) throw new InvalidOperationException($"Нет атласа актёра: {actor.id}, type={actor.actorType}.");
                if(!cache.TryGetValue(key,out artwork)) {artwork=Load(key);cache.Add(key,artwork);}
                artworkKey=key;
            }
            Draw();
        }
        private static Artwork Load(string key)
        {
            var asset=Resources.Load<TextAsset>(key);
            if(asset==null) throw new InvalidOperationException("Нет описания атласа: "+key);
            var data=JsonUtility.FromJson<AtlasActorArtwork>(asset.text);
            if(data==null || string.IsNullOrEmpty(data.resource) || data.clips==null || data.clips.Length==0 ||
                data.cellWidth<=0 || data.cellHeight<=0 || !float.IsFinite(data.pixelsPerUnit) || data.pixelsPerUnit<=0 ||
                !float.IsFinite(data.pivotX) || !float.IsFinite(data.pivotY) || data.pivotX<0 || data.pivotX>1 || data.pivotY<0 || data.pivotY>1)
                throw new InvalidOperationException("Некорректное описание атласа: "+key);
            var texture=Resources.Load<Texture2D>(data.resource);
            if(texture==null || texture.width!=data.width || texture.height!=data.height ||
                data.width%data.cellWidth!=0 || data.height%data.cellHeight!=0)
                throw new InvalidOperationException($"Неверный размер атласа: {key}, resource={data.resource}, expected={data.width}x{data.height}.");
            var result=new Artwork {resource=data.resource,facesLeft=data.facesLeft};
            foreach(var entry in data.clips)
            {
                if(entry==null || string.IsNullOrEmpty(entry.pose) || result.clips.ContainsKey(entry.pose) || entry.row<0 ||
                    entry.row>=data.height/data.cellHeight || entry.count<1 || entry.count>data.width/data.cellWidth ||
                    !float.IsFinite(entry.framesPerSecond) || entry.framesPerSecond<=0)
                    throw new InvalidOperationException($"Некорректный клип атласа: {key}, pose={entry?.pose}.");
                var clip=new Clip {frames=new Sprite[entry.count],rate=entry.framesPerSecond,loop=entry.loop};
                for(int frame=0;frame<entry.count;frame++)
                {
                    var sprite=Sprite.Create(texture,new Rect(frame*data.cellWidth,data.height-(entry.row+1)*data.cellHeight,data.cellWidth,data.cellHeight),
                        new Vector2(data.pivotX,data.pivotY),data.pixelsPerUnit);
                    sprite.name=$"{entry.pose}:{frame}";SpriteArtworkMaterial.Prepare(sprite);clip.frames[frame]=sprite;
                }
                result.clips.Add(entry.pose,clip);
            }
            return result;
        }
        private void Update() {if(state!=null) Draw();}
        public void Draw()
        {
            transform.position=state.Position;view.flipX=(state.facing<0)!=artwork.facesLeft;
            string pose=ActorPose.Clip(state);
            if(!artwork.clips.TryGetValue(pose,out var clip))
            {
                if(missingPose!=pose) Debug.LogWarning($"Нет позы атласа: actor={state.id}, artwork={artworkKey}, pose={pose}. Отображение пропущено.",this);
                missingPose=pose;view.enabled=false;return;
            }
            missingPose=null;view.enabled=true;var sprite=clip.Sample(state.poseTime);
            if(view.sprite!=sprite) SpriteArtworkMaterial.Apply(view,sprite);
        }
    }
}
