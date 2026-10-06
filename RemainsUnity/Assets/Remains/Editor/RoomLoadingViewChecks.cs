using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class RoomLoadingViewChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка экрана загрузки",typeof(RectTransform),typeof(Canvas));
            var foreign=new GameObject("Чужой слой меню",typeof(RectTransform));
            try
            {
                var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Require(font!=null,"отсутствует назначаемый шрифт HUD");
                var menu=PauseMenuView.Create(root.transform);
                var view=RoomLoadingView.Create(root.transform,font,menu.transform);view.Validate();
                Require(!view.IsVisible && view.Caption.font==font && view.Caption.alignment==TextAnchor.MiddleCenter &&
                    view.Caption.fontSize==20 && !view.Caption.resizeTextForBestFit,"начальная видимость или типографика не соответствует HUD");
                Require(view.Rect.anchorMin==Vector2.zero && view.Rect.anchorMax==Vector2.one &&
                    view.Rect.offsetMin==Vector2.zero && view.Rect.offsetMax==Vector2.zero,"экран загрузки не покрывает Canvas");
                menu.Panel.SetActive(true);view.SetVisible(true);
                Require(view.IsVisible && menu.Panel.activeInHierarchy && menu.transform.GetSiblingIndex()>view.transform.GetSiblingIndex(),
                    "загрузка скрыла уже открытый Escape");
                int children=root.transform.childCount,index=view.transform.GetSiblingIndex();
                view.SetVisible(true);
                Require(root.transform.childCount==children && view.transform.GetSiblingIndex()==index,"повтор pending создал или переставил интерфейс");
                menu.Panel.SetActive(false);menu.Panel.SetActive(true);menu.transform.SetAsLastSibling();
                Require(menu.transform.GetSiblingIndex()>view.transform.GetSiblingIndex(),"повторное открытие Escape оказалось под загрузкой");
                view.SetVisible(false);
                Require(!view.IsVisible && menu.Panel.activeInHierarchy,"завершение загрузки закрыло Escape");
                var image=view.GetComponent<Image>();image.color=new Color(.04f,.08f,.07f,.5f);
                Reject(view.Validate,"полупрозрачный fullscreen");image.color=new Color(.04f,.08f,.07f,1);
                Reject(()=>RoomLoadingView.Create(root.transform,null,menu.transform),"нет шрифта");
                Reject(()=>RoomLoadingView.Create(root.transform,font,foreign.transform),"чужой слой Escape");
                canvas.renderMode=RenderMode.WorldSpace;
                Reject(()=>RoomLoadingView.Create(root.transform,font,menu.transform),"WorldSpace Canvas");
                Require(root.transform.childCount==children,"ошибка контракта оставила частично созданный view");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(foreign);}
            CheckPresentationGate();
            Debug.Log("Экран загрузки: PASS — непрозрачный Overlay, назначенный шрифт, Escape выше загрузки, повторный pending без новых объектов, ошибки Canvas/ссылок видимы; камера принимает только готовую пару своей комнаты, старый binding и Dispose не оставляют блокировку.");
        }
        private static void CheckPresentationGate()
        {
            var cameraObject=new GameObject("Камера готовности локации",typeof(Camera));
            var firstObject=new GameObject("Первый свет готовности",typeof(RoomLighting));
            var nextObject=new GameObject("Следующий свет готовности",typeof(RoomLighting));
            var beforeFG=Shader.GetGlobalTexture("_WorldLightMap");var beforeBG=Shader.GetGlobalTexture("_BackgroundLightMap");
            var beforeBounds=Shader.GetGlobalVector("_WorldLightBounds");float beforeEnabled=Shader.GetGlobalFloat("_WorldLightEnabled");
            RoomPresentationGate gate=null;
            try
            {
                var camera=cameraObject.GetComponent<Camera>();const int visibleLayers=31;camera.cullingMask=visibleLayers;
                var first=firstObject.GetComponent<RoomLighting>();var next=nextObject.GetComponent<RoomLighting>();
                var room=GateRoom("loading-first");var other=GateRoom("loading-next");
                var profile=new RoomLightProfile {ambient=new Color(.1f,.12f,.15f),lights=Array.Empty<RoomLight>()};
                gate=new RoomPresentationGate(camera);gate.SetContext(true,room);gate.Bind(first);
                Require(gate.IsPreparing && !gate.IsReady && camera.cullingMask==0,"неподготовленная комната показана камерой");
                first.Initialize(room,profile);
                Require(gate.IsReady && !gate.IsPreparing && camera.cullingMask==visibleLayers && first.ForegroundMap!=null && first.BackgroundMap!=null &&
                    first.ForegroundMap.width>0 && first.BackgroundMap.height>0,"публикация первой пары не открыла камеру");
                gate.SetContext(true,other);
                Require(!gate.IsReady && gate.IsPreparing && camera.cullingMask==0,"готовая карта другой RoomLayout identity принята для новой комнаты");
                gate.Bind(next);
                Require(!gate.IsReady && camera.cullingMask==0,"Bind неприготовленного света показал старую пару");
                next.Initialize(other,profile);
                Require(gate.IsReady && camera.cullingMask==visibleLayers,"новая готовая пара не открыла камеру");
                first.Release();
                Require(gate.IsReady && camera.cullingMask==visibleLayers && Shader.GetGlobalFloat("_WorldLightEnabled")==1,
                    "Release прежнего света изменил текущий binding или выключил shader");
                gate.SetContext(false,null);
                Require(!gate.IsPreparing && !gate.IsReady,"отсутствие LocalActor оставило блокировку загрузки главного меню");
                gate.Dispose();gate=null;next.Release();
                Require(camera.cullingMask==visibleLayers,"Dispose или поздний readiness callback не восстановил исходные слои камеры");
            }
            finally
            {
                gate?.Dispose();
                UnityEngine.Object.DestroyImmediate(firstObject);UnityEngine.Object.DestroyImmediate(nextObject);UnityEngine.Object.DestroyImmediate(cameraObject);
                Shader.SetGlobalTexture("_WorldLightMap",beforeFG);Shader.SetGlobalTexture("_BackgroundLightMap",beforeBG);
                Shader.SetGlobalVector("_WorldLightBounds",beforeBounds);Shader.SetGlobalFloat("_WorldLightEnabled",beforeEnabled);
            }
        }
        private static RoomLayout GateRoom(string id)=>new() {id=id,left=0,right=2,bottom=0,top=2,
            platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()};
        private static void Reject(Action action,string context)
        {
            bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"не отклонён неверный контракт: "+context);
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Экран загрузки: "+reason);}
    }
}
