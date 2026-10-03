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
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            texture.SetPixels(new[]{new Color(1,0,0,0),new Color(0,1,0,1),new Color(0,0,1,.5f),Color.white});texture.Apply();
            var mask=new RoomLightMask(texture,new Rect(-2,3,4,2));
            Require(mask.Sample(new Vector2(-1,3.5f)).r==0,"Невидимый цвет попал в маску света");
            Require(mask.Sample(new Vector2(1,3.5f)).g>.99f,"Нарушена горизонтальная привязка маски");
            Require(Mathf.Abs(mask.Sample(new Vector2(-1,4.5f)).b-.5f)<.01f,"Нарушена прозрачность или вертикальная привязка маски");
            Require(Mathf.Abs(mask.Sample(new Vector2(-1,4.5f)).a-.5f)<.01f,"Альфа маски возведена в квадрат вместо сохранения непрозрачности");
            Require(mask.Sample(new Vector2(3,4)).a==0,"Маска затемняет за пределами своего прямоугольника");
            Require(mask.Sample(new Vector2(3,4)).maxColorComponent<=1 && mask.Sample(new Vector2(3,4)).r==0,"Маска повторяется за границей");
            Require(Mathf.Abs(mask.Sample(new Vector2(0,4)).r-.25f)<.01f,"Интерполяция маски смешала скрытый RGB");
            var shadowProfile=new RoomLightProfile {ambient=Color.white,lights=new[]{new RoomLight {id="shadow",shadow=true,
                radius=1,intensity=.8f,maskTexture=texture,maskX=-2,maskY=3,maskWidth=4,maskHeight=2}}};
            float transmission=1-texture.GetPixel(0,1).a*.8f; // RGBA32 quantizes the half-alpha input.
            float expected=QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(transmission):transmission;
            var shaded=RoomLightMap.Sample(room,shadowProfile,new Vector2(-1,4.5f));
            Require(Mathf.Abs(shaded.r-expected)<.001f && Mathf.Abs(shaded.g-expected)<.001f && Mathf.Abs(shaded.b-expected)<.001f,
                "Затемнение зависит от RGB маски или неверно читает её непрозрачность");
            Require(RoomLightMap.Sample(room,shadowProfile,new Vector2(3,4)).r==1,"Область тени изменила остальную комнату");
            shadowProfile.lights=Array.Empty<RoomLight>();
            Require(RoomLightMap.Sample(room,shadowProfile,Vector2.zero).r==1,"Выключение тени не восстановило свет");
            UnityEngine.Object.DestroyImmediate(texture);
            Debug.Log("Освещение: PASS — затухание, стена, открытая балка, общий шейдер.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
