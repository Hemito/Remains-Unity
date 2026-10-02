using System.IO;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class HealthWarningRender
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(960,240,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Шкалы").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=1;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var texture=Resources.Load<Texture2D>("Original/Hud/health");
            var normal=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
            var warning=new HealthBarWarning(normal,Resources.Load<CombatHudView>("Presentation/CombatHud").HealthWarningFrames);
            foreach(var sample in new[]{(0, .2f,0f,"20% · обычный"),(1,.19f,.26667f,"19% · предупреждение"),(2,.2f,.3f,"20% · после лечения")})
            {
                ui.Label(canvas.transform,sample.Item4,new Vector2(20+sample.Item1*320,-30),new Vector2(300,40));
                var image=ui.Panel(canvas.transform,"Заполнение",new Vector2(20+sample.Item1*320,-90),new Vector2(280,35)).GetComponent<Image>();
                image.color=Color.white;
                if(sample.Item1==1) warning.Refresh(image,.19f,true,0);
                warning.Refresh(image,sample.Item2,true,sample.Item3);
            }
            Canvas.ForceUpdateCanvases();
            var result=new Texture2D(960,240,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,960,240),0,0);result.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/health-warning.png")),result.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(result);}
        }
    }
}

