using System;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Remains.Networking;
using Remains.Persistence;
using kcp2k;
using UnityEngine.UI;
using Remains.Content;
using System.Collections.Generic;

namespace Remains.Editor
{
    public static class VisualChecks
    {
        public static void Render()
        {
            WeaponCatalog.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RoomVisual.Build(RoomLayout.Load());
            PonyVisual.Prepare();
            var idle=new GameObject("Пони: стояние").AddComponent<PonyVisual>();
            idle.Bind(new ActorState { id="visual-idle",x=-1,pose="idle",hair=0 });idle.DrawPose(0);idle.DrawPose(0.5f);
            var running=new GameObject("Пони: бег").AddComponent<PonyVisual>();
            var runningState=new ActorState { id="visual-run",x=2,pose="run",hair=1 };
            running.Bind(runningState);running.DrawPose(0);running.DrawPose(0.2f);
            var down=new GameObject("Пони: нокдаун").AddComponent<PonyVisual>();
            down.Bind(new ActorState {id="visual-down",x=5,down=true});down.DrawPose(0);
            var camera=new GameObject("Камера проверки").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=4;
            camera.transform.position=new Vector3(2,2.5f,-10);camera.backgroundColor=new Color(0.04f,0.06f,0.05f);
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            camera.Render();RenderTexture.active=target;
            var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/visual"));
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"scene.png");File.WriteAllBytes(path,image.EncodeToPNG());
            runningState.facing=-1;running.DrawPose(0.2f);
            camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"scene-turned.png"),image.EncodeToPNG());
            var network=new GameObject("Проверка интерфейса");network.AddComponent<KcpTransport>();
            var session=network.AddComponent<CoopSession>();session.Saves=new SaveStore(Path.Combine(directory,"hud-save"));
            session.ConfigureRules(WeaponCatalog.Rules);
            var world=new WorldState();var uiActor=new ActorState{id="ui-player",character="Проверка",player=true,connected=true,x=-1.5f,health=65,mana=70};
            WeaponAmmunition.Initialize(uiActor,true);world.actors.Add(uiActor);
            var container=new ContainerState();world.containers.Add(container);
            container.items.Add(SharedInventory.Create("caps",20));container.items.Add(SharedInventory.Create("heal",3));
            world.Find("ui-player").inventory.Add(SharedInventory.Create("heal",5));
            typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
            typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,"ui-player");
            var hud=new GameObject("Интерфейс проверки").AddComponent<PrototypeHud>();hud.Initialize(session);hud.ToggleInventory();
            hud.DepositRowRect(2).GetComponent<Button>().onClick.Invoke();
            var canvas=hud.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.sortingOrder=32767;
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"hud.png"),image.EncodeToPNG());
            var containerPosition=container.Position;container.Position=new Vector2(100,0);
            hud.ToggleInventory();hud.ToggleInventory();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"inventory-no-container.png"),image.EncodeToPNG());
            foreach(var text in hud.GetComponentsInChildren<Text>())
                if(text.text.StartsWith("Аптечка\n",StringComparison.Ordinal) || text.text=="Рядом нет доступного контейнера.")
                {
                    Debug.Log($"Описание выбранной аптечки: требуется={text.preferredHeight}, доступно={text.rectTransform.rect.height}.");
                    if(text.preferredHeight>text.rectTransform.rect.height+.1f)
                        throw new InvalidOperationException("Описание или причина недоступной передачи обрезается.");
                }
            container.Position=containerPosition;
            var bodyInventory=new ActorState {id="ui-body",character="Союзник",player=true,connected=false,down=true,x=uiActor.x,y=uiActor.y};
            bodyInventory.inventory.Add(SharedInventory.Create("heal",6));bodyInventory.inventory.Add(SharedInventory.Create("grenade",4));
            world.actors.Add(bodyInventory);hud.ToggleInventory();hud.ToggleInventory();
            if(hud.ShownInventorySourceId!=InventorySource.BodyId(bodyInventory.id)) throw new InvalidOperationException("UI не выбрал тело для снимка инвентаря.");
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"body-inventory.png"),image.EncodeToPNG());
            world.actors.Remove(bodyInventory);
            hud.ToggleInventory();hud.PauseMenu.Toggle();
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"pause-menu.png"),image.EncodeToPNG());
            hud.gameObject.SetActive(false);
            var dialog=new GameObject("Проверка сравнения контента");var dialogCanvas=dialog.AddComponent<Canvas>();
            dialogCanvas.renderMode=RenderMode.ScreenSpaceCamera;dialogCanvas.worldCamera=camera;dialogCanvas.planeDistance=1;dialogCanvas.sortingOrder=32767;
            var scaler=dialog.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var panel=ui.Panel(dialog.transform,"Сравнение",new Vector2(90,-70),new Vector2(1100,570));
            var rows=new List<ContentDifference>();
            for(int i=0;i<4;i++) rows.Add(new ContentDifference {id=i==0?"base":"example.balance."+i,
                saved="#"+(i+1)+" · версия 1.0.0\n"+new string('A',64),current="#"+(4-i)+" · версия 2.1.0\n"+new string('F',64)});
            ContentComparisonView.Draw(panel.transform,rows,"Контент персонажа отличается. Продолжение может изменить игровой результат.",0,_=>{},_=>{});
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"content-comparison.png"),image.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("Снимок рендера Unity: "+path);
        }
    }
}
