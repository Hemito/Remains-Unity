using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class CameraFollower
    {
        private readonly Camera camera;
        private bool oversizedWarning;
        public RoomLayout Room { get; set; }
        public CameraFollower(Camera camera,RoomLayout room) {this.camera=camera;Room=room;}
        public void Step(ActorState actor,float dt)
        {
            float halfWidth=camera.orthographicSize*camera.aspect;
            float x=Room.right-Room.left<=halfWidth*2?(Room.left+Room.right)/2:Mathf.Clamp(actor.x,Room.left+halfWidth,Room.right-halfWidth);
            float y=Room.top-Room.bottom<=camera.orthographicSize*2?(Room.top+Room.bottom)/2:
                Mathf.Clamp(actor.y+1,Room.bottom+camera.orthographicSize,Room.top-camera.orthographicSize);
            var target=new Vector3(x,y,-10);
            var position=Vector3.Lerp(camera.transform.position,target,1-Mathf.Exp(-8*dt));
            var bounds=PonyVisual.WorldBounds(actor);
            bool oversized=bounds.size.x>halfWidth*2-.3f || bounds.size.y>camera.orthographicSize*2-.3f;
            if(oversized && !oversizedWarning)
                Debug.LogWarning($"Поза не помещается в камеру: actor={actor.id}, bounds={bounds}, viewport={halfWidth*2}x{camera.orthographicSize*2}. Поза центрирована, требуется настройка масштаба камеры.");
            oversizedWarning=oversized;
            position.x=FrameAxis(position.x,bounds.min.x,bounds.max.x,halfWidth);
            position.y=FrameAxis(position.y,bounds.min.y,bounds.max.y,camera.orthographicSize);
            camera.transform.position=position;
        }
        // Room limits guide composition, but must not crop the local character.
        // Pose bounds are compiled once with the visible meshes, not rebuilt per frame.
        public static float FrameAxis(float center,float minimum,float maximum,float halfSize)
        {
            const float margin=.15f;
            float lower=maximum-halfSize+margin,upper=minimum+halfSize-margin;
            return lower<=upper?Mathf.Clamp(center,lower,upper):(minimum+maximum)*.5f;
        }
    }
}
