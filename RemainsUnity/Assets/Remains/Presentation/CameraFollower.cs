using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class CameraFollower
    {
        private readonly Camera camera;
        private readonly CameraFollowMotion motion=new();
        private bool oversizedWarning;
        private RoomLayout room;
        private string actorId,actorLevel;
        private int actorGeneration;
        private long actorLifecycle;
        private float previousHalfWidth,previousHalfHeight;
        private bool hasContext;
        public RoomLayout Room
        {
            get=>room;
            set {if(!ReferenceEquals(room,value)) {room=value;ResetMotion();}}
        }
        public CameraFollower(Camera camera,RoomLayout room) {this.camera=RequireCamera(camera);Room=room;}
        // Explicit same-level teleports/recovery repairs must discard target history.
        // No distance/speed heuristic: fast continuous gameplay remains continuous.
        public void ResetMotion() {motion.Reset();hasContext=false;}
        public const float AnimationMargin=4;
        public Rect AnimationArea(ActorState actor)=>AnimationArea(PonyVisual.CameraBounds(actor),
            camera.orthographicSize*camera.aspect,camera.orthographicSize,camera.transform.position,
            hasContext && actorLevel==actor.level && actorId==actor.id && actorGeneration==actor.generation && actorLifecycle==actor.lifecycleSequence);
        // FrameAxis keeps the local envelope on screen. Expanding it by two half-extents covers every
        // possible clamped center, including room edges; retain the previous view during smooth following.
        public static Rect AnimationArea(Bounds local,float halfWidth,float halfHeight,Vector2 center,bool includePrevious)
        {
            if(!float.IsFinite(halfWidth) || !float.IsFinite(halfHeight) || halfWidth<=0 || halfHeight<=0)
                throw new System.ArgumentException($"Неверный размер области анимации камеры: {halfWidth}×{halfHeight}.");
            float left=local.min.x-2*halfWidth,right=local.max.x+2*halfWidth;
            float bottom=local.min.y-2*halfHeight,top=local.max.y+2*halfHeight;
            if(includePrevious)
            {left=Mathf.Min(left,center.x-halfWidth);right=Mathf.Max(right,center.x+halfWidth);
                bottom=Mathf.Min(bottom,center.y-halfHeight);top=Mathf.Max(top,center.y+halfHeight);}
            return Rect.MinMaxRect(left-AnimationMargin,bottom-AnimationMargin,right+AnimationMargin,top+AnimationMargin);
        }
        public static Camera RequireCamera(Camera value)
        {
            if(value==null) throw new System.InvalidOperationException("В сцене не назначена активная камера с тегом MainCamera.");
            if(!value.orthographic || !float.IsFinite(value.orthographicSize) || value.orthographicSize<=0)
                throw new System.InvalidOperationException($"Камера {value.name}: требуется ортографическая проекция с положительным конечным размером.");
            return value;
        }
        public void Step(ActorState actor,float dt)
        {
            float halfWidth=camera.orthographicSize*camera.aspect;
            if(!hasContext || actorId!=actor.id || actorLevel!=actor.level || actorGeneration!=actor.generation ||
                actorLifecycle!=actor.lifecycleSequence || previousHalfWidth!=halfWidth || previousHalfHeight!=camera.orthographicSize)
            {
                ResetMotion();hasContext=true;actorId=actor.id;actorLevel=actor.level;actorGeneration=actor.generation;
                actorLifecycle=actor.lifecycleSequence;previousHalfWidth=halfWidth;previousHalfHeight=camera.orthographicSize;
            }
            // Follow the movement anchor, not the sampled body: foot-fitted BodyPosition
            // steps with every walk frame and would shake the whole view on flat floor.
            // The visible pose stays framed by the body-placed envelope below.
            float x=Room.right-Room.left<=halfWidth*2?(Room.left+Room.right)/2:Mathf.Clamp(actor.x,Room.left+halfWidth,Room.right-halfWidth);
            float y=Room.top-Room.bottom<=camera.orthographicSize*2?(Room.top+Room.bottom)/2:
                Mathf.Clamp(actor.y+1,Room.bottom+camera.orthographicSize,Room.top-camera.orthographicSize);
            var target=new Vector3(x,y,-10);
            var position=motion.Step(camera.transform.position,target,dt);
            var bounds=PonyVisual.CameraBounds(actor);
            bool oversized=bounds.size.x>halfWidth*2-.3f || bounds.size.y>camera.orthographicSize*2-.3f;
            if(oversized && !oversizedWarning)
                Debug.LogWarning($"Поза не помещается в камеру: actor={actor.id}, bounds={bounds}, viewport={halfWidth*2}x{camera.orthographicSize*2}. Поза центрирована, требуется настройка масштаба камеры.");
            oversizedWarning=oversized;
            position.x=FrameAxis(position.x,bounds.min.x,bounds.max.x,halfWidth);
            position.y=FrameAxis(position.y,bounds.min.y,bounds.max.y,camera.orthographicSize);
            camera.transform.position=position;
        }
        // Room limits guide composition, but must not crop the local character.
        // The complete animation envelope is compiled once with the visible meshes;
        // individual limb frames must never push the camera back and forth.
        public static float FrameAxis(float center,float minimum,float maximum,float halfSize)
        {
            const float margin=.15f;
            float lower=maximum-halfSize+margin,upper=minimum+halfSize-margin;
            return lower<=upper?Mathf.Clamp(center,lower,upper):(minimum+maximum)*.5f;
        }
    }
}
