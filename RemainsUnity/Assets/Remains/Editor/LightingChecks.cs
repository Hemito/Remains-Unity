using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class LightingChecks
    {
        public static void Run()
        {
            var room=new RoomLayout {id="light-check",platforms=new[]{new RoomSurface {x=0,y=-2,width=.3f,height=4}}};
            var profile=new RoomLightProfile {ambient=new Color(.2f,.2f,.2f),lights=new[]{new RoomLight {id="test",x=-1,y=0,radius=5,intensity=.8f,color=Color.white}}};
            Require(RoomLightMap.Sample(room,profile,new Vector2(-.5f,0)).r>.7f,"Свет отсутствует рядом с источником");
            Require(Mathf.Abs(RoomLightMap.Sample(room,profile,new Vector2(1,0)).r-.2f)<.001f,"Свет проходит сквозь стену");
            room.platforms[0].oneWay=true;
            Require(RoomLightMap.Sample(room,profile,new Vector2(1,0)).r>.4f,"Открытая балка блокирует свет как стена");
            Require(Mathf.Abs(RoomLightMap.Sample(room,profile,new Vector2(9,0)).r-.2f)<.001f,"Радиус света не ограничен");
            var material=WorldArtworkMaterial.Create(Texture2D.whiteTexture);
            Require(material.shader.name=="Remains/WorldLit","Отсутствует общий материал мира");
            UnityEngine.Object.DestroyImmediate(material);
            Debug.Log("Освещение: PASS — затухание, стена, открытая балка, общий шейдер.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
