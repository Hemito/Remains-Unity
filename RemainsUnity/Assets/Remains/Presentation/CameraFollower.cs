using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class CameraFollower
    {
        private readonly Camera camera;
        public RoomLayout Room { get; set; }
        public CameraFollower(Camera camera,RoomLayout room) {this.camera=camera;Room=room;}
        public void Step(ActorState actor,float dt)
        {
            float halfWidth=camera.orthographicSize*camera.aspect;
            float x=Room.right-Room.left<=halfWidth*2?(Room.left+Room.right)/2:Mathf.Clamp(actor.x,Room.left+halfWidth,Room.right-halfWidth);
            var target=new Vector3(x,Mathf.Max(3,actor.y+1),-10);
            camera.transform.position=Vector3.Lerp(camera.transform.position,target,1-Mathf.Exp(-8*dt));
        }
    }
}
