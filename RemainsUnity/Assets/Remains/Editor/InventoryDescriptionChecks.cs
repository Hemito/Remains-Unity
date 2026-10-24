using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class InventoryDescriptionChecks
    {
        private const int RenderLayer=31;
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly ActorState Actor;
            public readonly PersonalInventoryView View;
            public readonly PersonalInventoryPanel Panel;
            public readonly ItemTextCatalog Texts;
            public Fixture(Transform parent)
            {
                Root=new GameObject("Проверка полного описания",typeof(RectTransform));Root.transform.SetParent(parent,false);
                var session=Root.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                Actor=new ActorState {id="description-owner",character="Описание",owner="description-owner",player=true,connected=true,
                    loadoutVersion=1,weaponInventoryVersion=1,health=70};
                Actor.inventory.Add(new ItemStack {id="description-metal",definition="metal_comp",count=2});
                Actor.inventory.Add(new ItemStack {id="description-book",definition="lbook",count=1});
                var world=new WorldState();world.actors.Add(Actor);
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,Actor.id);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalOwner)).SetValue(session,Actor.owner);
                var window=InventoryWindowView.Create(Root.transform);var rect=(RectTransform)window.transform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
                View=window.PersonalContents;
                Texts=new ItemTextCatalog(GameAssets.Load<TextAsset>(ItemTextCatalog.Resource).text);
                var actions=new ItemShortcutActions(session,_=>throw new InvalidOperationException("Описание выбрало оружие."));
                Panel=new PersonalInventoryPanel(View,session,id=>Texts.Name(id,"InventoryDescriptionChecks"),()=>0,
                    _=>throw new InvalidOperationException("Описание выбрало старое оружие."),new InventoryArtwork(),itemText:Texts,itemActions:()=>actions);
                Panel.Refresh(null);View.Rows[0].onClick.Invoke();
            }
            public void Dispose()=>UnityEngine.Object.DestroyImmediate(Root);
        }
        public static void Run()
        {
            WeaponCatalog.Load();
            var root=new GameObject("Описание инвентаря",typeof(RectTransform),typeof(Canvas));
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            try
            {
                using var fixture=new Fixture(root.transform);
                var view=fixture.View.Description;view.Validate();
                Require(view.Rect.sizeDelta==new Vector2(228,180) && view.Content.fontSize==18,"изменены прежние внешние границы или шрифт");
                string original=StateJson.ToJson(fixture.Actor);
                string longDescription=string.Join("\n",Enumerable.Range(1,24).Select(i=>$"Строка полного описания № {i}: текст модового предмета."))+"\nКОНЕЦ ПОЛНОГО ОПИСАНИЯ";
                fixture.Texts.Register(new ItemTextDefinition {id="metal_comp",name="Металлическая пластина",description=longDescription},replace:true);
                fixture.Panel.Refresh(null);Canvas.ForceUpdateCanvases();CheckVisibleContent(view,"длинный текст");
                Require(view.CanScroll && view.Content.text.Contains(longDescription) && view.Scrollbar.gameObject.activeSelf,"длинное описание сокращено или нет прокрутки");
                int measurements=view.MeasurementCount;view.Scroll.verticalNormalizedPosition=.4f;
                float scroll=view.Scroll.verticalNormalizedPosition;
                for(int i=0;i<20;i++) fixture.Panel.Refresh(null);
                Require(view.MeasurementCount==measurements && Mathf.Abs(view.Scroll.verticalNormalizedPosition-scroll)<.001f,"повторный refresh измеряет или сбрасывает неизменное описание");
                view.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,252);fixture.Panel.Refresh(null);
                Require(view.MeasurementCount>measurements && Mathf.Abs(view.Content.rectTransform.rect.width-view.Viewport.rect.width)<.1f,
                    "изменение ширины не пересчитало перенос полного текста");
                view.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,228);fixture.Panel.Refresh(null);view.Scroll.verticalNormalizedPosition=scroll;
                int first=fixture.Panel.FirstVisibleRow;
                var events=root.AddComponent<EventSystem>();var pointer=new PointerEventData(events) {scrollDelta=new Vector2(0,-1)};
                ExecuteEvents.ExecuteHierarchy(view.Content.gameObject,pointer,ExecuteEvents.scrollHandler);
                Require(fixture.Panel.FirstVisibleRow==first && view.Scroll.verticalNormalizedPosition<scroll,"колесо описания листает вещи либо не двигает текст");
                view.Scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();CheckBottom(view,"длинный текст");
                fixture.View.Rows[1].onClick.Invoke();Require(view.Scroll.verticalNormalizedPosition>.999f,"новый предмет не возвращает описание наверх");
                fixture.View.Rows[0].onClick.Invoke();Require(view.Scroll.verticalNormalizedPosition>.999f,"возврат к прежнему предмету не сбросил scroll");
                view.Scroll.verticalNormalizedPosition=.5f;fixture.Actor.inventory[0].count=3;fixture.Panel.Refresh(null);
                Require(Mathf.Abs(view.Scroll.verticalNormalizedPosition-.5f)<.001f && view.Content.text.Contains("В наличии: 3"),"обновление количества того же предмета сбросило scroll или старое количество");
                fixture.Actor.inventory[0].count=2;Require(StateJson.ToJson(fixture.Actor)==original,"описание/колесо изменили gameplay или выбор экипировки");
                var content=view.Scroll.content;view.Scroll.content=view.Viewport;
                try {view.Validate();throw new Exception("Неверный content ScrollRect принят.");}
                catch(InvalidOperationException error) {Require(error.Message.Contains("Описание инвентаря"),"непонятная диагностика неверного content");}
                finally {view.Scroll.content=content;}
                CheckCurrentTips();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Полное описание инвентаря: PASS — masked ScrollRect, полный длинный текст/низ, колесо без листания вещей, кеш измерений, reset нового выбора, обновление количества без reset и актуальные подсказки.");
        }
        private static void CheckCurrentTips()
        {
            var entries=StateJson.FromJson<ItemTextManifest>(GameAssets.Load<TextAsset>(ItemTextCatalog.Resource).text).items;
            var actions=new StandardItemActions();int count=0;
            foreach(var entry in entries.Where(entry=>entry.description.Contains("Взять в копыта") || entry.description.Contains("Возьмите в копыта")))
            {
                if(actions.Action(new ItemStack {id="description:"+entry.id,definition=entry.id,count=1})!=StandardItemActions.Hold) continue;
                Require(!entry.description.Contains("Назначьте на быструю клавишу"),"устаревшая обязательная инструкция назначения: "+entry.id);count++;
            }
            Require(count>=61,"не проверен полный прежний набор обычных компонентов/красок/ценностей");
        }
        public static void CheckVisibleContent(InventoryDescriptionView view,string context)
        {
            view.Validate();var content=view.Content.rectTransform.rect;var viewport=view.Viewport.rect;
            Require(float.IsFinite(view.Content.preferredHeight) && view.Content.preferredHeight<=content.height+.1f &&
                Mathf.Abs(content.width-viewport.width)<.1f && content.height>=viewport.height-.1f,
                $"текст обрезан внутри content: {context}, preferred={view.Content.preferredHeight}, content={content}, viewport={viewport}");
            Require(view.Scroll.vertical==view.CanScroll && view.Scrollbar.gameObject.activeSelf==view.CanScroll,
                "доступность прокрутки не соответствует реальной высоте: "+context);
        }
        private static void CheckBottom(InventoryDescriptionView view,string context)
        {
            var corners=new Vector3[4];view.Content.rectTransform.GetWorldCorners(corners);
            float bottom=view.Viewport.InverseTransformPoint(corners[0]).y;
            Require(Mathf.Abs(bottom-view.Viewport.rect.yMin)<.15f,
                $"конец полного текста недоступен: {context}, bottom={bottom}, viewport={view.Viewport.rect}");
        }
        public static void Render()
        {
            WeaponCatalog.Load();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                var root=new GameObject("Макет полного описания",typeof(RectTransform));Camera camera=null;RenderTexture target=null;Texture2D image=null;
                var previous=RenderTexture.active;
                try
                {
                    var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                    camera=new GameObject("Камера описания").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);canvas.worldCamera=camera;canvas.planeDistance=1;
                    // Render only this owned UI fixture. Retained authored room previews
                    // must not become foreground objects in an inventory layout proof.
                    camera.cullingMask=1<<RenderLayer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
                    target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                    var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                    var raycaster=root.AddComponent<GraphicRaycaster>();var events=root.AddComponent<EventSystem>();
                    using var fixture=new Fixture(root.transform);var view=fixture.View.Description;
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=RenderLayer;
                    CheckRenderIsolation(root,camera,size);
                    Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                    var center=camera.WorldToViewportPoint(((RectTransform)root.transform).TransformPoint(((RectTransform)root.transform).rect.center));
                    Require(Mathf.Abs(center.x-.5f)<.001f && Mathf.Abs(center.y-.5f)<.001f && Mathf.Abs(center.z-1)<.001f,"native Canvas не подготовлен камерой");
                    // The actual catalog includes ordinary factory items; do not test only short starter/armor descriptions.
                    var entries=StateJson.FromJson<ItemTextManifest>(GameAssets.Load<TextAsset>(ItemTextCatalog.Resource).text).items;
                    foreach(var entry in entries.Where(entry=>new StandardItemActions().Action(new ItemStack {id=entry.id,definition=entry.id,count=1})==StandardItemActions.Hold))
                    {
                        string full=$"{entry.name}\nВ наличии: 2\nДоступно: 2\n\n{entry.description}";
                        view.SetText(full,fixture.Actor.id,entry.id,true);Canvas.ForceUpdateCanvases();CheckVisibleContent(view,$"{size}/{entry.id}");
                        Require(view.Content.text==full,"каталог отображается не полностью: "+entry.id);
                        view.Scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();if(view.CanScroll) CheckBottom(view,$"{size}/{entry.id}");
                    }
                    string longText=string.Join("\n",Enumerable.Range(1,24).Select(i=>$"Строка № {i} полного русского описания."))+"\nКОНЕЦ ПОЛНОГО ОПИСАНИЯ";
                    fixture.Texts.Register(new ItemTextDefinition {id="metal_comp",name="Металлическая пластина",description=longText},replace:true);
                    fixture.Panel.Refresh(null);view.Scroll.verticalNormalizedPosition=1;
                    foreach(var stage in new[]{"top","bottom"})
                    {
                        if(stage=="bottom") view.Scroll.verticalNormalizedPosition=0;
                        Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();CheckVisibleContent(view,$"{size}/{stage}");
                        if(stage=="bottom") CheckBottom(view,$"{size}/{stage}");
                        var contentCorners=new Vector3[4];view.Rect.GetWorldCorners(contentCorners);
                        foreach(var corner in contentCorners) {var point=camera.WorldToViewportPoint(corner);Require(point.x>=0 && point.x<=1 && point.y>=0 && point.y<=1,"viewport вне кадра: "+size);}
                        foreach(var rect in new[]{fixture.View.Use.GetComponent<RectTransform>(),fixture.View.Deposit.GetComponent<RectTransform>(),fixture.View.Portrait.GetComponent<RectTransform>()})
                            Require(!Overlaps(view.Rect,rect),"описание перекрывает портрет или действие: "+size+"/"+rect.name);
                        var icon=fixture.View.DetailIcon.rectTransform;
                        Require(fixture.View.DetailIcon.enabled && fixture.View.DetailIcon.sprite!=null && fixture.View.DetailIcon.gameObject.activeInHierarchy,
                            "нет настоящей иконки выбранной металлической пластины: "+size);
                        Require(!Overlaps(view.Rect,icon) && !Overlaps((RectTransform)view.Scrollbar.transform,icon),
                            "иконка выбранного предмета перекрывает описание/полосу прокрутки: "+size+", icon="+icon.anchoredPosition+", size="+icon.rect.size);
                        foreach(var label in fixture.View.GetComponentsInChildren<Text>())
                            Require(label.preferredHeight<=label.rectTransform.rect.height+.1f,"неописательный текст обрезан: "+size+"/"+label.name);
                        var pointer=new PointerEventData(events) {position=RectTransformUtility.WorldToScreenPoint(camera,fixture.Panel.UseRect.TransformPoint(fixture.Panel.UseRect.rect.center))};
                        var hits=new List<RaycastResult>();raycaster.Raycast(pointer,hits);
                        Require(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(fixture.View.Use.transform),"Use закрыта прокруткой описания: "+size);
                        image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        string directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/inventory-description-ui"));Directory.CreateDirectory(directory);
                        File.WriteAllBytes(System.IO.Path.Combine(directory,$"description-{stage}-{size.x}x{size.y}.png"),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);image=null;
                    }
                }
                finally
                {
                    RenderTexture.active=previous;if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                    if(target!=null) {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                    UnityEngine.Object.DestroyImmediate(root);if(camera!=null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }
            }
            Debug.Log("Макеты полного описания: PASS — весь обычный каталог и длинный модовый текст, верх/низ, native Expand 1024/1280/1920, masked viewport, свободный portrait/Use/raycast и detail icon; камера изолирована от сохранённых изображений мира.");
        }
        private static void CheckRenderIsolation(GameObject root,Camera camera,Vector2Int size)
        {
            Require(camera.cullingMask==(1<<RenderLayer) && camera.clearFlags==CameraClearFlags.SolidColor,
                "камера макета не изолирована: "+size);
            foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                if(renderer.gameObject.scene.IsValid() && renderer.gameObject.activeInHierarchy && renderer.enabled &&
                    renderer.gameObject.layer==RenderLayer && !renderer.transform.IsChildOf(root.transform))
                    throw new InvalidOperationException($"InventoryDescriptionChecks: посторонний renderer на слое макета {RenderLayer}: {renderer.name}, scene={renderer.gameObject.scene.path}, size={size}.");
            foreach(var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                if(canvas.gameObject.scene.IsValid() && canvas.isActiveAndEnabled && canvas.gameObject.layer==RenderLayer &&
                    !canvas.transform.IsChildOf(root.transform))
                    throw new InvalidOperationException($"InventoryDescriptionChecks: посторонний Canvas на слое макета {RenderLayer}: {canvas.name}, scene={canvas.gameObject.scene.path}, size={size}.");
        }
        private static bool Overlaps(RectTransform first,RectTransform second)
        {
            Rect LocalBounds(RectTransform rect)
            {
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                var points=corners.Select(point=>(Vector2)first.parent.InverseTransformPoint(point)).ToArray();
                return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
            }
            return LocalBounds(first).Overlaps(LocalBounds(second));
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("InventoryDescriptionChecks: "+reason);}
    }
}
