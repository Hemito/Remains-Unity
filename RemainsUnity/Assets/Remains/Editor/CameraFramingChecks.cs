using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CameraFramingChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка авторской камеры");
            try
            {
                var camera=root.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=7.25f;
                camera.backgroundColor=Color.magenta;camera.transform.position=new Vector3(1,4,-10);
                _=new CameraFollower(camera,new Remains.Domain.RoomLayout());
                if(camera.orthographicSize!=7.25f || camera.backgroundColor!=Color.magenta || camera.transform.position!=new Vector3(1,4,-10))
                    throw new InvalidOperationException("Запуск камеры затирает авторские параметры сцены.");
                camera.orthographic=false;bool rejected=false;
                try {CameraFollower.RequireCamera(camera);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Перспективная камера молча принята для 2D-комнаты.");
                rejected=false;
                try {CameraFollower.RequireCamera(null);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Отсутствующая камера скрыта runtime-подстановкой.");
                camera.orthographic=true;camera.orthographicSize=5;camera.aspect=1280f/534;
                PonyVisual.Prepare();
                var follower=new CameraFollower(camera,new Remains.Domain.RoomLayout {id="camera-edge",left=0,right=80,bottom=0,top=30});
                var actor=new Remains.Domain.ActorState {id="edge-climber",x=.1f,y=25,climbing=true,pose="climb"};
                follower.Step(actor,10);var stable=camera.transform.position;
                var render=Resources.Load<Remains.Content.PonyRenderAsset>("Original/Pony/render");
                foreach(var clip in render.Clips) foreach(var frame in clip.frames)
                {
                    actor.pose=clip.id;actor.poseTime+=.1f;actor.facing=-actor.facing;
                    var envelope=PonyVisual.CameraBounds(actor);
                    var minimum=frame.bounds.min+new Vector3(actor.x,actor.y,0);
                    var maximum=frame.bounds.max+new Vector3(actor.x,actor.y,0);
                    if(minimum.x<envelope.min.x-.0001f || maximum.x>envelope.max.x+.0001f ||
                        minimum.y<envelope.min.y-.0001f || maximum.y>envelope.max.y+.0001f)
                        throw new InvalidOperationException("Общая граница камеры не вмещает кадр "+clip.id);
                    follower.Step(actor,1f/60);
                    if(Vector3.Distance(camera.transform.position,stable)>.0001f)
                        throw new InvalidOperationException("Кадры анимации двигают камеру неподвижного персонажа у края комнаты");
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            foreach(var sample in new[] {
                new[]{8f,-.7f,2.5f,8f},new[]{-8f,-2.5f,.7f,8f},
                new[]{5f,-.4f,1.8f,5f},new[]{-5f,-1.8f,.4f,5f}})
            {
                float center=CameraFollower.FrameAxis(sample[0],sample[1],sample[2],sample[3]);
                if(center-sample[3]>sample[1]-.149f || center+sample[3]<sample[2]+.149f)
                    throw new InvalidOperationException("Камера обрезает границы позы у края.");
            }
            if(CameraFollower.FrameAxis(2,0,4,5)!=2)
                throw new InvalidOperationException("Камера сдвигает уже видимую позу.");
        }
    }
}
