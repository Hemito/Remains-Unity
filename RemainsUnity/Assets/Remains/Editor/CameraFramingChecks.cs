using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CameraFramingChecks
    {
        // Exercise the edge transition while position changes; the older check
        // below covers a stationary body across graphic frames only.
        private static void CheckEdgeTravel(Camera camera)
        {
            var room=new Remains.Domain.RoomLayout {id="camera-edge-travel",left=0,right=80,bottom=0,top=30};
            var clips=Resources.Load<Remains.Content.PonyRenderAsset>("Original/Pony/render").Clips;
            foreach(float aspect in new[]{16f/9,4f/3,1280f/534})
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            foreach(int edge in new[]{0,1,2,3})
            {
                camera.aspect=aspect;
                var actor=new Remains.Domain.ActorState {id=$"camera-edge-{edge}-{dt:R}",x=edge==0?.5f:edge==1?79.5f:40,
                    y=edge==2?.1f:edge==3?28.5f:15};
                var direction=edge==0?Vector2.right:edge==1?Vector2.left:edge==2?Vector2.up:Vector2.down;
                var follower=new CameraFollower(camera,room);
                camera.transform.position=new Vector3(actor.x,actor.y,-10);
                follower.Step(actor,10);
                var previous=camera.transform.position;
                for(int sample=0;sample<Mathf.CeilToInt(4/dt);sample++)
                {
                    actor.Position+=direction*(3*dt);
                    actor.pose=clips[sample%clips.Length].id;actor.poseTime=sample*dt;
                    actor.climbing=sample%2==0;actor.facing=sample%2==0?1:-1;
                    follower.Step(actor,dt);
                    var current=camera.transform.position;
                    var displacement=new Vector2(current.x-previous.x,current.y-previous.y);
                    if(Vector2.Dot(displacement,direction)<-.0001f)
                        throw new InvalidOperationException($"Камера колеблется при монотонном уходе от края: edge={edge}, dt={dt:R}, sample={sample}, previous={previous}, current={current}.");
                    float transverse=edge<2?displacement.y:displacement.x;
                    if(Mathf.Abs(transverse)>.0001f)
                        throw new InvalidOperationException($"Смена позы двигает камеру по неподвижной оси: edge={edge}, dt={dt:R}, sample={sample}, delta={displacement}.");
                    var bounds=PonyVisual.CameraBounds(actor);
                    float halfWidth=camera.orthographicSize*camera.aspect;
                    if(current.x-halfWidth>bounds.min.x-.149f || current.x+halfWidth<bounds.max.x+.149f ||
                        current.y-camera.orthographicSize>bounds.min.y-.149f || current.y+camera.orthographicSize<bounds.max.y+.149f)
                        throw new InvalidOperationException($"Камера обрезает общую границу поз при уходе от края: edge={edge}, dt={dt:R}, sample={sample}, camera={current}, bounds={bounds}.");
                    previous=current;
                }
            }
        }
        public static void Run()
        {
            CameraFollowMotionChecks.Run();
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
                CheckEdgeTravel(camera);
                CheckVariableEdges(camera);
                CheckContextReset(camera);
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
        private static void CheckVariableEdges(Camera camera)
        {
            var room=new Remains.Domain.RoomLayout {id="camera-variable-edge",left=0,right=80,bottom=0,top=30};
            camera.aspect=16f/9;camera.orthographicSize=5;
            foreach(int fps in new[]{30,60,144}) foreach(int edge in new[]{0,1,2,3})
            {
                var actor=new Remains.Domain.ActorState {id=$"variable-{fps}-{edge}",x=edge==0?.5f:edge==1?79.5f:40,
                    y=edge==2?.1f:edge==3?28.5f:15};
                var direction=edge==0?Vector2.right:edge==1?Vector2.left:edge==2?Vector2.up:Vector2.down;
                var follower=new CameraFollower(camera,room);camera.transform.position=new Vector3(actor.x,actor.y,-10);
                follower.Step(actor,10);float elapsed=0;int sample=0;
                while(elapsed<6)
                {
                    float dt=sample%19==7?.0532103f:1f/fps;dt=Mathf.Min(dt,6-elapsed);
                    var previousCamera=(Vector2)camera.transform.position;var previousBody=actor.Position;
                    actor.Position+=direction*(3.15f*dt);
                    follower.Step(actor,dt);var current=(Vector2)camera.transform.position;
                    var delta=current-previousCamera;
                    float screenDelta=Vector2.Dot(actor.Position-previousBody-delta,direction);
                    if(Vector2.Dot(delta,direction)<-.0001f || screenDelta<-.0001f)
                        throw new InvalidOperationException($"Камера/тело движется обратно на экране при переменном кадре: fps={fps}, edge={edge}, sample={sample}, dt={dt:R}, cameraDelta={delta}, screenDelta={screenDelta:R}.");
                    if(Mathf.Abs(edge<2?delta.y:delta.x)>.0001f)
                        throw new InvalidOperationException($"Дрейф неподвижной оси при переменном кадре: fps={fps}, edge={edge}, sample={sample}, delta={delta}.");
                    var bounds=PonyVisual.CameraBounds(actor);float halfWidth=camera.orthographicSize*camera.aspect;
                    if(current.x-halfWidth>bounds.min.x-.1499f || current.x+halfWidth<bounds.max.x+.1499f ||
                        current.y-5>bounds.min.y-.1499f || current.y+5<bounds.max.y+.1499f)
                        throw new InvalidOperationException($"Камера использует старое тело у края: fps={fps}, edge={edge}, sample={sample}, camera={current}, bounds={bounds}.");
                    elapsed+=dt;sample++;
                }
            }
        }
        private static void CheckContextReset(Camera camera)
        {
            foreach(int reason in new[]{0,1,2,3,4,5,6})
            {
                var room=new Remains.Domain.RoomLayout {id="camera-reset",left=0,right=80,bottom=0,top=30};
                var actor=new Remains.Domain.ActorState {id="before",level="hub",generation=1,lifecycleSequence=1,x=40,y=13};
                camera.aspect=16f/9;camera.transform.position=new Vector3(40,14,-10);
                var follower=new CameraFollower(camera,room);follower.Step(actor,0);
                actor.y=14;follower.Step(actor,.1f);actor.y=15;
                if(reason==0) {room=new Remains.Domain.RoomLayout {id="after",left=0,right=80,bottom=0,top=30};follower.Room=room;}
                if(reason==1) actor.id="after";
                if(reason==2) actor.generation++;
                if(reason==3) actor.lifecycleSequence++;
                if(reason==4) actor.level="factory:6";
                if(reason==5) camera.aspect=4f/3;
                if(reason==6) follower.ResetMotion();
                var before=camera.transform.position;
                new CameraFollower(camera,room).Step(actor,.1f);var expected=camera.transform.position;
                camera.transform.position=before;follower.Step(actor,.1f);
                if(Vector3.Distance(camera.transform.position,expected)>.00001f)
                    throw new InvalidOperationException($"История предыдущего контекста камеры не сброшена: reason={reason}, actual={camera.transform.position}, expected={expected}.");
            }
        }
    }
}
