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
            CheckMaskColorSpace();
            CheckBackgroundSeparation();
            CheckArtworkRoundTrip();
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
        private static void CheckArtworkRoundTrip()
        {
            var layout=new RoomLayout {id="artwork-light-contract",left=0,right=4,bottom=0,top=2,
                platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),artwork=new RoomArtwork[4]};
            for(int i=0;i<4;i++) layout.artwork[i]=new RoomArtwork {texture="Hub/concrete",x=i,width=1,height=1,
                selfIlluminated=(i&1)!=0,backgroundLighting=(i&2)!=0};
            var room=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
            try
            {
                var authoring=room.AddComponent<Remains.Content.RoomAuthoring>();
                var layers=room.GetComponentsInChildren<Remains.Content.RoomArtworkAuthoring>();
                Require(layers.Length==4,"Потеряны слои тестового оформления");
                for(int i=0;i<4;i++)
                {
                    var restored=JsonUtility.FromJson<RoomArtwork>(JsonUtility.ToJson(layers[i].Read(authoring)));
                    Require(restored.selfIlluminated==layout.artwork[i].selfIlluminated && restored.backgroundLighting==layout.artwork[i].backgroundLighting,
                        "Чтение/сериализация оформления потеряли роль освещения: "+i);
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(room);}
            Debug.Log("Оформление: PASS — роли света сохранены при построении, чтении и сериализации общего ресурса.");
        }
        private static void CheckBackgroundSeparation()
        {
            var room=new RoomLayout {id="background-check",platforms=Array.Empty<RoomSurface>()};
            var original=new RoomLightProfile {ambient=new Color(.4f,.4f,.4f),backgroundAmbientScale=.225f,
                lights=new[]{new RoomLight {id="fill",radius=5,intensity=.4f,backgroundIntensity=.05f},
                    new RoomLight {id="fixture",radius=5,intensity=.1f,maskTexture=Texture2D.whiteTexture}}};
            var background=original.ForBackground();
            Require(Mathf.Abs(background.ambient.r-.09f)<.0001f && background.lights[0].intensity==.05f,"Фон не получил свои настройки");
            Require(original.lights[0].intensity==.4f && original.ambient.r==.4f,"Настройки фона изменили свет актёров");
            Require(background.lights[1].intensity==.1f && background.lights[1].maskTexture==Texture2D.whiteTexture,"Фон потерял яркость или прямую ссылку на маску светильника");
            original.lights[1].maskTexture=null;background.lights[1].maskTexture=null;
            Require(RoomLightMap.Sample(room,original,Vector2.zero).r>RoomLightMap.Sample(room,background,Vector2.zero).r+.5f,"Разделение профилей не изменило результат освещения");
            background.lights[0].intensity=0;
            Require(original.lights[0].intensity==.4f,"Профили разделяют изменяемые источники");
            var legacy=JsonUtility.FromJson<RoomLightProfile>("{\"ambient\":{\"r\":0.4,\"g\":0.4,\"b\":0.4,\"a\":1},\"lights\":[{\"id\":\"legacy\",\"intensity\":0.5}]}");
            Require(legacy.ForBackground().ambient==legacy.ambient && legacy.ForBackground().lights[0].intensity==.5f,"Старый профиль без новых полей изменил свет");
            Debug.Log("Фоновое освещение: PASS — независимые профили, маски, яркость и совместимость данных.");
        }
        private static void CheckMaskColorSpace()
        {
            foreach(bool linear in new[]{false,true})
            {
                var texture=new Texture2D(1,1,TextureFormat.RGBA32,false,linear);
                try
                {
                    texture.SetPixel(0,0,new Color(.5f,.25f,.75f,.5f));texture.Apply();
                    Color stored=texture.GetPixel(0,0);
                    bool srgb=UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
                    Color decoded=QualitySettings.activeColorSpace==ColorSpace.Linear && srgb?stored.linear:stored;
                    var mask=new RoomLightMask(texture,new Rect(0,0,1,1));
                    Color actual=mask.Sample(new Vector2(.5f,.5f));
                    Debug.Log($"Цвет маски: project={QualitySettings.activeColorSpace}, sRGB={srgb}, stored={stored}, actual={actual}, expectedRGB={decoded*stored.a}.");
                    Require(Mathf.Abs(actual.r-decoded.r*stored.a)<.001f && Mathf.Abs(actual.g-decoded.g*stored.a)<.001f &&
                        Mathf.Abs(actual.b-decoded.b*stored.a)<.001f,"RGB маски не согласован с цветовым пространством карты света.");
                    Require(Mathf.Abs(actual.a-stored.a)<.001f,"Преобразование RGB изменило альфу маски.");
                }
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
