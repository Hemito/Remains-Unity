using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class SlopedSupportChecks
    {
        public static void Run()
        {
            foreach(int direction in new[]{-1,1})
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var room=Fixture(direction);var hull=new MovementHull {width=.2f,height=1};
                float Expected(float x)=>Mathf.Clamp(direction>0?x+hull.width/2:2.4f-x+hull.width/2,0,2.4f);
                var position=new Vector2(-1,Expected(-1));
                foreach(int travel in new[]{1,-1})
                    for(int i=0;i<Mathf.RoundToInt(5/dt);i++)
                    {
                        position=ActorTerrainMotion.Move(room,hull,position,new Vector2(travel*dt,-dt),false);
                        Require(Mathf.Abs(position.y-Expected(position.x))<.001f,"Потеря опоры при проходе стыка",direction,dt,position);
                        Require(ActorTerrainMotion.Supported(room,hull,position),"Нет контакта после движения",direction,dt,position);
                    }
                position=ActorTerrainMotion.Move(room,hull,new Vector2(1.2f,5),new Vector2(0,-10),false);
                Require(Mathf.Abs(position.y-Expected(1.2f))<.001f,"Падение пропустило наклон",direction,dt,position);
                float drop=position.y;
                position=ActorTerrainMotion.Move(room,hull,position,new Vector2(.1f,-.3f),false,drop);
                Require(position.y<drop-.25f,"Спуск повторно схватил ту же наклонную опору",direction,dt,position);
                position=ActorTerrainMotion.Move(room,hull,new Vector2(1.2f,-.2f),new Vector2(0,3),false);
                Require(position.y>2,"Подъём снизу заблокирован односторонней опорой",direction,dt,position);
                var prop=new ContainerState {movable=true,width=.6f,height=.6f,x=1.2f,y=5};
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) PropMotion.Step(prop,room,dt,null);
                Require(Mathf.Abs(prop.y-1.5f)<.001f,"Контейнер не остановился на своей опоре",direction,dt,prop.Position);
                for(int i=0;i<Mathf.CeilToInt(1/dt);i++) PropMotion.Step(prop,room,dt,new Vector2(1.2f,-1));
                Require(prop.y<0,"Телекинез не спустил контейнер сквозь наклон",direction,dt,prop.Position);
                var actor=new ActorState {x=-1,y=direction>0?0:2.4f};var motion=new MovementController(room);
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) motion.Step(actor,Vector2.right,false,dt);
                Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position) &&
                    ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),"Контроллер персонажа потерял наклон или стык",direction,dt,actor.Position);
            }
            Ceiling();Authoring();
            Debug.Log("Наклонные опоры: PASS — оба направления, стыки с полом и соседними сегментами, падение, спуск, подъём снизу, контейнер/телекинез/контроллер при 30/60/144 FPS, потолок и авторинг.");
        }
        private static RoomLayout Fixture(int direction)
        {
            var surfaces=new RoomSurface[5];
            surfaces[0]=new RoomSurface {x=-4,y=(direction>0?0:2.4f)-.4f,width=4,height=.4f};
            for(int i=0;i<3;i++) surfaces[i+1]=new RoomSurface {x=i*.8f,y=(direction>0?i:2-i)*.8f,width=.8f,height=.8f,
                slopeDirection=direction,oneWay=true,shotsPassThrough=true,movementSound="metal",texture="Hub/floor"};
            surfaces[4]=new RoomSurface {x=2.4f,y=(direction>0?2.4f:0)-.4f,width=4,height=.4f};
            return new RoomLayout {id="slope-check",left=-4,right=6.4f,bottom=-4,top=8,platforms=surfaces,ladders=Array.Empty<RoomLadder>()};
        }
        private static void Ceiling()
        {
            var room=Fixture(1);Array.Resize(ref room.platforms,6);
            room.platforms[5]=new RoomSurface {x=.5f,y=1.5f,width=1,height=.2f};
            var hull=new MovementHull {width=.2f,height=1};var start=new Vector2(.3f,.4f);
            var moved=ActorTerrainMotion.Move(room,hull,start,new Vector2(.4f,-.01f),false);
            Require(ActorTerrainMotion.IsClear(room,hull,moved) && moved.x<=.4001f,"Наклон протащил голову сквозь потолок",1,0,moved);
        }
        private static void Authoring()
        {
            foreach(int direction in new[]{-1,1})
            {
                var surface=Fixture(direction).platforms[1];
                var layer=new RoomArtwork {x=surface.x,y=surface.y,width=surface.width,height=surface.height,
                    slopeDirection=direction,texture=surface.texture};
                var layout=new RoomLayout {id="slope-art-check",left=0,right=.8f,bottom=0,top=4,platforms=new[]{surface},ladders=Array.Empty<RoomLadder>(),artwork=new[]{layer}};
                var root=new GameObject("Проверка наклона");
                try
                {
                    var authoring=RoomDraftGeometry.Create(root,layout);
                    var visual=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
                    visual.transform.SetParent(root.transform,false);
                    var actual=authoring.CreateLayout();
                    Require(actual.platforms[0].slopeDirection==direction && actual.artwork[0].slopeDirection==direction &&
                        Mathf.Abs(actual.artwork[0].width-.8f)<.0001f && Mathf.Abs(actual.artwork[0].height-.8f)<.0001f,
                        "Графика и авторская опора потеряли направление или размер",direction,0,Vector2.zero);
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        private static void Require(bool valid,string reason,int direction,float dt,Vector2 position)
        {if(!valid) throw new InvalidOperationException($"Наклонная опора: {reason}; direction={direction}, dt={dt}, position={position}.");}
    }
}
