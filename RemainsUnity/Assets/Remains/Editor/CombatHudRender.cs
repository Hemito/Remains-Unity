using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class CombatHudRender
    {
        public static void Render()=>Render(false);
        public static void RenderHealth()=>Render(true);
        public static void RenderReferenceSizes()
        {
            Remains.Content.WeaponCatalog.Load();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Камера HUD").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
                var target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                var canvas=new GameObject("HUD").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                canvas.worldCamera=camera;canvas.planeDistance=1;
                var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                var hud=new CombatHud(canvas.transform,_=>{});
                var actor=new ActorState {id="hud-reference-render",character="Литлпип",weapon="pistol",health=100,mana=100};
                WeaponAmmunition.Initialize(actor,true);
                var image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                try
                {
                    for(int sample=0;sample<3;sample++)
                    {
                        actor.health=new[]{100f,19.1f,0f}[sample];actor.mana=new[]{100f,36f,0f}[sample];actor.down=sample==2;
                        hud.Refresh(actor,true);Canvas.ForceUpdateCanvases();
                        var view=canvas.GetComponentInChildren<CombatHudView>();
                        foreach(var rect in hud.LayoutReservations)
                        {
                            var corners=new Vector3[4];rect.GetWorldCorners(corners);
                            foreach(var corner in corners)
                            {
                                var viewport=camera.WorldToViewportPoint(corner);
                                if(viewport.x<0 || viewport.y<0 || viewport.x>1 || viewport.y>1)
                                    throw new System.InvalidOperationException($"HUD выходит за кадр {size}: {rect.name}, viewport={viewport}.");
                            }
                        }
                        if(view.HealthText.preferredHeight>view.HealthText.rectTransform.rect.height ||
                            view.HealthText.preferredWidth>view.HealthText.rectTransform.rect.width)
                            throw new System.InvalidOperationException($"Числа здоровья не помещаются в поле HUD {size}.");
                        camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests",$"hud-reference-{size.x}x{size.y}-{sample}.png"));
                        File.WriteAllBytes(path,image.EncodeToPNG());
                        Debug.Log($"HUD reference: {size}, HP={actor.health}, mana={actor.mana}, числа={view.HealthText.text}, кадр={path}.");
                    }
                }
                finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
            }
        }
        private static void Render(bool healthSamples)
        {
            Remains.Content.WeaponCatalog.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(1440,320,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("HUD").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=1;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var artwork=new InventoryArtwork();
            for(int i=0;i<3;i++)
            {
                var panel=ui.Panel(canvas.transform,"Состояние",new Vector2(i*480,0),new Vector2(480,320));
                var hud=new CombatHud(panel.transform,_=>{});
                var actor=new ActorState {id="hud-render",character=i==0?"Перезарядка":i==1?"Пустой магазин":"Нет запаса",weapon="pistol",health=100,mana=100};
                if(healthSamples) {actor.health=new[]{19.1f,.4f,0f}[i];actor.down=i==2;actor.character="Проверка здоровья";}
                WeaponAmmunition.Initialize(actor,true);
                WeaponAmmunition.Magazine(actor,"pistol").rounds=0;
                if(i==2) actor.inventory.Clear();
                if(i==0) actor.inventory.Add(SharedInventory.Create("grenade",5));
                hud.Refresh(actor,true,i==0?1.2f:(float?)null);
            }
            Canvas.ForceUpdateCanvases();
            var result=new Texture2D(1440,320,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,1440,320),0,0);result.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,healthSamples?"../../.reference/tests/combat-hud-health.png":"../../.reference/tests/combat-hud-reload.png")),result.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(result);}
        }
    }
}

