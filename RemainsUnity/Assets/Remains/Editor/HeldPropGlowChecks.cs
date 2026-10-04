using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class HeldPropGlowChecks
    {
        public static void RenderAndBuild()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PropCatalog.Load();var artwork=new PropArtwork();
            for(int i=0;i<3;i++)
            {
                var state=new ContainerState {id="glow-"+i,owner="remote",x=(i-1)*1.6f,held=i==1};
                state.items.Add(SharedInventory.Create("heal",3));
                string before=Remains.Serialization.StateJson.ToJson(state);
                var view=artwork.CreateView("prototype-crate",state.id,1.2f);
                view.DrawMotion(state,"local",false,.016f);
                if(view.HeldGlowVisible!=state.held) throw new Exception("Свечение не соответствует удержанию.");
                state.held=true;view.DrawMotion(state,"local",true,.016f);
                state.held=i==1;view.DrawMotion(state,"local",true,.016f);
                if(view.HeldGlowVisible!=state.held || Remains.Serialization.StateJson.ToJson(state)!=before)
                    throw new Exception("Отпускание/пауза изменили предмет или оставили свечение.");
            }
            var camera=new GameObject("Камера сравнения удержания").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.65f;camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.08f,.08f);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/held-prop-glow.png")),image.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
            Debug.Log("Свечение предмета: PASS — удержание, отпускание на паузе, сохранность заполненного контейнера.");
            PrototypeBuild.Build();
        }
    }
}
