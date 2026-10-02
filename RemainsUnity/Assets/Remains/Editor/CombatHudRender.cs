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
        public static void Render()
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
                WeaponAmmunition.Initialize(actor,true);
                WeaponAmmunition.Magazine(actor,"pistol").rounds=0;
                if(i==2) actor.inventory.Clear();
                hud.Refresh(actor,true,i==0?1.2f:(float?)null);
            }
            Canvas.ForceUpdateCanvases();
            var result=new Texture2D(1440,320,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,1440,320),0,0);result.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/combat-hud-reload.png")),result.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(result);}
        }
    }
}

