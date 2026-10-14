using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class AtlasActorVisual : ActorVisual
    {
        private sealed class Clip
        {
            public Sprite[] frames;public AtlasActorClip definition;
            public Sprite Sample(ActorState actor)
            {
                return frames[definition.Sample(actor)];
            }
        }
        private sealed class Artwork
        {public string resource;public bool facesLeft;public readonly Dictionary<string,Clip> clips=new(StringComparer.Ordinal);}
        private static readonly Dictionary<string,Artwork> cache=new(StringComparer.Ordinal);
        private ActorState state;
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer view;
        public void SetRenderer(SpriteRenderer renderer)=>view=renderer;
        private bool generatedView;
        [SerializeField,HideInInspector] private bool customDeadPresentation;
        [SerializeField,HideInInspector] private bool deadFlipY;
        [SerializeField,HideInInspector] private float deadLift;
        [SerializeField,HideInInspector] private Color deadTint=Color.white;
        public void ConfigureDeadPresentation(bool flipY,float lift,Color tint)
        {
            if(!float.IsFinite(lift)) throw new InvalidOperationException("Некорректное смещение изображения тела: "+name);
            customDeadPresentation=true;deadFlipY=flipY;deadLift=lift;deadTint=tint;
        }
        private Artwork artwork;
        private string artworkKey,missingPose;
        public string Resource=>artwork?.resource;
        public override string ActorId=>state?.id;
        public override void AppendLightOccluders(List<RoomRadianceField.LightOccluder> output)=>AppendSpriteOccluder(view,output);

        public override void Bind(ActorState actor)
        {
            state=actor;string key=ActorTypes.Get(actor.actorType).artwork;
            if(view==null) {view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=WorldVisualOrderPolicy.ActorPartOrder;generatedView=true;}
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
            var saved=Resources.Load<AtlasSpriteAsset>(key);
            var data=AtlasAnimationData.Get(key);
            if(data==null || string.IsNullOrEmpty(data.resource) || data.clips==null || data.clips.Length==0 ||
                data.cellWidth<=0 || data.cellHeight<=0 || !float.IsFinite(data.pixelsPerUnit) || data.pixelsPerUnit<=0 ||
                !float.IsFinite(data.pivotX) || !float.IsFinite(data.pivotY) || data.pivotX<0 || data.pivotX>1 || data.pivotY<0 || data.pivotY>1)
                throw new InvalidOperationException("Некорректное описание атласа: "+key);
            var texture=saved!=null?saved.Texture:Resources.Load<Texture2D>(data.resource);
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
                var clip=new Clip {frames=saved!=null?saved.Frames(entry.pose):new Sprite[entry.count],definition=entry};
                for(int frame=0;saved==null && frame<entry.count;frame++)
                {
                    var sprite=Sprite.Create(texture,new Rect(frame*data.cellWidth,data.height-(entry.row+1)*data.cellHeight,data.cellWidth,data.cellHeight),
                        new Vector2(data.pivotX,data.pivotY),data.pixelsPerUnit);
                    sprite.name=$"{entry.pose}:{frame}";clip.frames[frame]=sprite;
                }
                foreach(var sprite in clip.frames) SpriteArtworkMaterial.Prepare(sprite);
                result.clips.Add(entry.pose,clip);
            }
            return result;
        }
        private void Update() {if(state==null) return;using(Remains.Diagnostics.FrameHitchLog.Section("AtlasActorVisual.Update")) Draw();}
        public void Draw()
        {
            transform.position=state.Position+Vector2.up*(state.down?deadLift:0);view.flipX=(state.facing<0)!=artwork.facesLeft;
            if(customDeadPresentation) {view.flipY=state.down && deadFlipY;view.color=state.down?deadTint:Color.white;}
            string pose=ActorPose.Clip(state);
            if(!artwork.clips.TryGetValue(pose,out var clip))
            {
                if(missingPose!=pose) Debug.LogWarning($"Нет позы атласа: actor={state.id}, artwork={artworkKey}, pose={pose}. Отображение пропущено.",this);
                missingPose=pose;view.enabled=false;return;
            }
            missingPose=null;view.enabled=true;var sprite=clip.Sample(state);
            if(view.sprite!=sprite)
            {if(generatedView) SpriteArtworkMaterial.Apply(view,sprite);else view.sprite=sprite;}
        }
    }
}
