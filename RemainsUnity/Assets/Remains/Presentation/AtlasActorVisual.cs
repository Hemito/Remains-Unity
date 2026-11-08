using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    [DefaultExecutionOrder(1000)] // Read the same completed runtime projection as weapon/item visuals.
    public sealed class AtlasActorVisual : ActorVisual
    {
        private const float InvertedLift=.8f;
        // A received snapshot replaces an unarmed creature's state with an older pose time (a sleeping creature does
        // not send its state): the same pose then rewinds. The drawn phase keeps going from where it was; a new pose
        // or a pause (time standing still) is followed exactly. Armed types keep the exact phase (weapon sockets).
        private string phasePose;private uint phaseSequence;private float lastPoseTime,phaseOffset;
        private float DrawnPoseTime(string pose)
        {
            if(ActorTypes.Get(state.actorType).hasWeapon) return state.poseTime;
            if(pose!=phasePose || state.poseSequence!=phaseSequence) {phasePose=pose;phaseSequence=state.poseSequence;phaseOffset=0;}
            else if(state.poseTime<lastPoseTime-.0001f && !state.climbing) phaseOffset+=lastPoseTime-state.poseTime;
            lastPoseTime=state.poseTime;
            return state.poseTime+phaseOffset;
        }
        private bool wasInverted,wasClinging;
        // A ball that turns as it rolls (UnitRoller.animate: osn.rotation += dx × 1.5° a frame, dx in px per frame, kept in
        // the air): about its body centre, from its replicated velocity — appearance only, from a random first angle.
        [SerializeField,InspectorName("Катится (поворот по скорости)")] private bool rolls;
        public const float RollDegreesPerMetre=50f*1.5f;
        private float rollAngle=float.NaN;
        public void ConfigureRolling(bool value)=>rolls=value;
        public bool Rolls=>rolls;
        private sealed class Clip
        {
            public Sprite[] frames;public AtlasActorClip definition;public Vector2[] verticalBounds;
            public Sprite Sample(ActorState actor,float time)
            {
                return frames[ActorPose.IsTacticallyProne(actor) || definition.UsesMotion && !actor.down?
                    definition.Sample(actor):definition.FrameIndex(time,ActorPose.StationaryClimb(actor))];
            }
        }
        private sealed class Artwork
        {public string resource;public bool facesLeft;public readonly Dictionary<string,Clip> clips=new(StringComparer.Ordinal);}
        private static readonly Dictionary<string,Artwork> cache=new(StringComparer.Ordinal);
        private ActorState state;
        private Func<ActorState,bool> inVisibleArea;
        private bool culled;
        public void ConfigureVisibleArea(Func<ActorState,bool> test)=>inVisibleArea=test;
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer view;
        public void SetRenderer(SpriteRenderer renderer)=>view=renderer;
        private bool generatedView;
        private HeldPropGlow heldGlow;
        private string heldRejection;
        public bool HeldGlowVisible=>heldGlow!=null && heldGlow.Visible;
        public override void ApplyHeldAppearance(HeldPropColorSource colors)
        {
            bool active=state!=null && ActorHoldRules.Active(state);
            if(!active) {heldGlow?.Draw(false);heldRejection=null;return;}
            Color color=default;string reason=null;
            if(colors==null || !colors.TryResolve(state,out color,out reason))
            {
                heldGlow?.Draw(false);
                string rejection=colors==null?"Нет источника цвета держателя.":reason;
                if(heldRejection!=rejection) Debug.LogWarning($"Свечение тела {state.id}, type={state.actorType}, holder={state.hold.holder}: {rejection} Свечение пропущено.",this);
                heldRejection=rejection;return;
            }
            heldRejection=null;
            color.a=.7f;
            if(heldGlow==null)
            {
                var frames=new List<Sprite>();
                foreach(var clip in artwork.clips.Values) frames.AddRange(clip.frames);
                var child=new GameObject("Свечение удерживаемого тела");child.transform.SetParent(view.transform,false);
                heldGlow=child.AddComponent<HeldPropGlow>();heldGlow.Initialize(view,color,.08f,frames.ToArray());
                heldGlow.Renderer.forceRenderingOff=culled;
            }
            heldGlow.SetColor(color);heldGlow.SetFrame(view.sprite);heldGlow.Draw(view.enabled);
        }
        [SerializeField,HideInInspector] private bool customDeadPresentation;
        [SerializeField,HideInInspector] private bool deadFlipY;
        [SerializeField,HideInInspector] private float deadLift;
        [SerializeField,HideInInspector] private bool fitDeadToFrame;
        [SerializeField,HideInInspector] private Color deadTint=Color.white;
        public void ConfigureDeadPresentation(bool flipY,float lift,Color tint)
        {
            if(!float.IsFinite(lift)) throw new InvalidOperationException("Некорректное смещение изображения тела: "+name);
            customDeadPresentation=true;deadFlipY=flipY;deadLift=lift;deadTint=tint;fitDeadToFrame=true;
        }
        private Artwork artwork;
        private string artworkKey,missingPose;
        public string Resource=>artwork?.resource;
        public override string ActorId=>state?.id;
        public override void AppendLightOccluders(List<LightOccluder> output)=>AppendSpriteOccluder(view,output);

        public override void Bind(ActorState actor)
        {
            state=actor;string key=ActorTypes.Get(actor.actorType).artwork;
            if(view==null) {view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=WorldVisualOrderPolicy.ActorPartOrder;generatedView=true;}
            if(artworkKey!=key)
            {
                if(heldGlow!=null) {heldGlow.Draw(false);Destroy(heldGlow.gameObject);heldGlow=null;}
                if(string.IsNullOrEmpty(key)) throw new InvalidOperationException($"Нет атласа актёра: {actor.id}, type={actor.actorType}.");
                if(!cache.TryGetValue(key,out artwork)) {artwork=Load(key);cache.Add(key,artwork);}
                artworkKey=key;
            }
            Draw();
        }
        private static Artwork Load(string key)
        {
            var saved=GameAssets.Load<AtlasSpriteAsset>(key);
            var data=AtlasAnimationData.Get(key);
            if(data==null || string.IsNullOrEmpty(data.resource) || data.clips==null || data.clips.Length==0 ||
                data.cellWidth<=0 || data.cellHeight<=0 || !float.IsFinite(data.pixelsPerUnit) || data.pixelsPerUnit<=0 ||
                !float.IsFinite(data.pivotX) || !float.IsFinite(data.pivotY) || data.pivotX<0 || data.pivotX>1 || data.pivotY<0 || data.pivotY>1)
                throw new InvalidOperationException("Некорректное описание атласа: "+key);
            var texture=saved!=null?saved.Texture:GameAssets.Load<Texture2D>(data.resource);
            if(texture==null || texture.width!=data.width || texture.height!=data.height ||
                data.width%data.cellWidth!=0 || data.height%data.cellHeight!=0)
                throw new InvalidOperationException($"Неверный размер атласа: {key}, resource={data.resource}, expected={data.width}x{data.height}.");
            var result=new Artwork {resource=data.resource,facesLeft=data.facesLeft};
            foreach(var entry in data.clips)
            {
                if(entry==null || string.IsNullOrEmpty(entry.pose) || result.clips.ContainsKey(entry.pose) || entry.row<0 ||
                    entry.row>=data.height/data.cellHeight || entry.count<1 || entry.column<0 || entry.column+entry.count>data.width/data.cellWidth ||
                    !float.IsFinite(entry.framesPerSecond) || entry.framesPerSecond<=0)
                    throw new InvalidOperationException($"Некорректный клип атласа: {key}, pose={entry?.pose}.");
                var clip=new Clip {frames=saved!=null?saved.Frames(entry.pose):new Sprite[entry.count],definition=entry};
                if(saved!=null && saved.HasOpaqueVerticalBounds) clip.verticalBounds=saved.OpaqueVerticalBounds(entry.pose);
                for(int frame=0;saved==null && frame<entry.count;frame++)
                {
                    var sprite=Sprite.Create(texture,new Rect((entry.column+frame)*data.cellWidth,data.height-(entry.row+1)*data.cellHeight,data.cellWidth,data.cellHeight),
                        new Vector2(data.pivotX,data.pivotY),data.pixelsPerUnit);
                    sprite.name=$"{entry.pose}:{frame}";clip.frames[frame]=sprite;
                }
                foreach(var sprite in clip.frames) SpriteArtworkMaterial.Prepare(sprite);
                result.clips.Add(entry.pose,clip);
            }
            return result;
        }
        private void Update()
        {
            if(state==null) return;
            using var section=Remains.Diagnostics.FrameHitchLog.Section("AtlasActorVisual.Update");
            bool hidden=inVisibleArea!=null && !inVisibleArea(state);
            if(hidden!=culled)
            {
                culled=hidden;view.forceRenderingOff=hidden;
                if(heldGlow!=null) heldGlow.Renderer.forceRenderingOff=hidden;
            }
            if(!hidden) Draw();
        }
        public void Draw()
        {
            var canonical=state;state=ProjectState(state);
            try {DrawProjected();}
            finally {state=canonical;}
        }
        private void DrawProjected()
        {
            // A buried zombie is not drawn until it digs out (UnitZombie.animate: vis.visible = false).
            bool hidden=ActorPresence.Hidden(state);
            if(view.enabled==hidden) view.enabled=!hidden;
            if(hidden) return;
            // A creature hanging upside down (slime on a ceiling, UnitSlime.setVisPos) is drawn turned over, 0.8 m higher.
            bool inverted=state.creature?.inverted==true && !state.down;
            transform.position=state.Position+Vector2.up*(state.down?deadLift:inverted?InvertedLift:Anatomy.ProneLift(state));view.flipX=(state.facing<0)!=artwork.facesLeft;
            if(customDeadPresentation) {view.flipY=state.down && deadFlipY;view.color=state.down?deadTint:Color.white;}
            if(inverted) view.flipY=true;
            else if(wasInverted) view.flipY=customDeadPresentation && state.down && deadFlipY;
            wasInverted=inverted;
            // A spider mine clinging to a ceiling or a wall (UnitMsp.setVisPos) is drawn turned with its feet to it: at the
            // top of its body turned over, or at the wall side of it turned a quarter.
            int cling=state.down?NpcCreatureState.NoCling:state.creature?.cling??NpcCreatureState.NoCling;
            if(cling!=NpcCreatureState.NoCling)
            {
                var hull=Anatomy.Movement(state);
                transform.position=state.Position+(cling==NpcCreatureState.Ceiling?Vector2.up*hull.height:
                    new Vector2(cling==NpcCreatureState.LeftWall?-hull.width*.5f:hull.width*.5f,hull.height*.5f));
                transform.rotation=Quaternion.Euler(0,0,cling==NpcCreatureState.Ceiling?180:cling==NpcCreatureState.LeftWall?-90:90);
            }
            else if(rolls && !state.down)
            {
                if(float.IsNaN(rollAngle)) rollAngle=UnityEngine.Random.value*360;
                rollAngle=Mathf.Repeat(rollAngle-state.vx*RollDegreesPerMetre*Time.deltaTime,360);
                var turn=Quaternion.Euler(0,0,rollAngle);var centre=Vector2.up*(Anatomy.Movement(state).height*.5f);
                transform.rotation=turn;transform.position=state.Position+centre-(Vector2)(turn*centre);
            }
            else if(wasClinging || rolls) transform.rotation=Quaternion.identity;
            wasClinging=cling!=NpcCreatureState.NoCling;
            string pose=ActorPose.Clip(state);
            if(!artwork.clips.TryGetValue(pose,out var clip))
            {
                if(missingPose!=pose) Debug.LogWarning($"Нет позы атласа: actor={state.id}, artwork={artworkKey}, pose={pose}. Отображение пропущено.",this);
                missingPose=pose;view.enabled=false;heldGlow?.Draw(false);return;
            }
            missingPose=null;view.enabled=true;var sprite=clip.Sample(state,DrawnPoseTime(pose));
            if(state.down && fitDeadToFrame)
            {
                if(clip.verticalBounds==null) throw new InvalidOperationException($"Не подготовлена опора изображения тела: actor={state.id}, atlas={artworkKey}, pose={pose}.");
                int frame=Array.IndexOf(clip.frames,sprite);
                float offset=DeadFrameOffset(clip.verticalBounds[frame],view.flipY);
                transform.position=state.Position+Vector2.up*offset;
            }
            if(view.sprite!=sprite)
            {if(generatedView) SpriteArtworkMaterial.Apply(view,sprite);else view.sprite=sprite;}
            if(heldGlow!=null)
            {
                heldGlow.SetFrame(sprite);
                heldGlow.Draw(ActorHoldRules.Active(state) && heldRejection==null);
            }
        }
        public static float DeadFrameOffset(Vector2 verticalBounds,bool flipped)=>flipped?verticalBounds.y:-verticalBounds.x;
    }
}
