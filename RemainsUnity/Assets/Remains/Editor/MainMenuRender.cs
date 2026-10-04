using System.IO;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class MainMenuRender
    {
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(2560,1080)}) RenderAt(size.x,size.y);
        }
        public static readonly string LongWorldDescription=new string('Ш',48)+" · ревизия 9223372036854775807\nВетвь 1122aabb · снимок 9223372036854775807";
        public static void RenderLongName()
        {
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1920,1080),new Vector2Int(2560,1080)}) RenderAt(size.x,size.y,true);
        }
        private static void RenderAt(int width,int height,bool longName=false)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера меню").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Меню").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var menu=new MainMenuPanel(canvas.transform,()=>{},()=>{},()=>{},()=>{},()=>{});
            menu.Character.text="Литлпип";
            menu.World.text="Выбран: Мир 7f65d82c\n«Миры и сохранения»: выбор, имя и удаление.";
            if(longName) menu.World.text=LongWorldDescription;
            Canvas.ForceUpdateCanvases();
            Debug.Log($"Описание мира: longName={longName}, требуется={menu.World.preferredHeight}, доступно={menu.World.rectTransform.rect.height}.");
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", $".reference/tests/main-menu-{(longName?"long-name-":"")}{width}x{height}.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Главное меню: кадр сохранён "+path);
                if(width==1024)
                {
                    var store=new Remains.Persistence.SaveStore(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/world-management-render")));
                    new Remains.Persistence.WorldLibrary(store).Rename("menu-preview",longName?new string('Ш',48):"Проверочный мир");
                    WorldManagementPanel.Open(canvas.transform,store,"menu-preview",()=>{});
                    var management=canvas.GetComponentInChildren<WorldManagementView>();
                    if(longName) management.Delete.onClick.Invoke();
                    Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                    File.WriteAllBytes(Path.ChangeExtension(path,"world-management.png"),image.EncodeToPNG());
                    if(management.Status.preferredHeight>management.Status.rectTransform.rect.height+.1f)
                        throw new System.InvalidOperationException($"Подтверждение удаления обрезано: нужно {management.Status.preferredHeight}, доступно {management.Status.rectTransform.rect.height}.");
                }
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}


