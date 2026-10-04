using System;
using System.IO;
using System.Linq;
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
            var definitions=PropCatalog.All.OrderBy(p=>p.id).ToArray();
            for(int row=0;row<definitions.Length;row++) for(int i=0;i<3;i++)
            {
                var definition=definitions[row];
                var state=new ContainerState {id="glow-"+row+"-"+i,owner="remote",x=(i-1)*1.6f,y=-row*1.5f,held=i==1};
                state.items.Add(SharedInventory.Create("heal",3));
                string before=Remains.Serialization.StateJson.ToJson(state);
                var view=artwork.CreateView(definition.id,state.id,1.2f);
                view.DrawMotion(state,"local",false,.016f);
                if(view.HeldGlowVisible!=state.held) throw new Exception("Свечение не соответствует удержанию.");
                state.held=true;view.DrawMotion(state,"local",true,.016f);
                state.held=i==1;view.DrawMotion(state,"local",true,.016f);
                if(view.HeldGlowVisible!=state.held || Remains.Serialization.StateJson.ToJson(state)!=before)
                    throw new Exception("Отпускание/пауза изменили предмет или оставили свечение.");
            }
            var camera=new GameObject("Камера сравнения удержания").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=Mathf.Max(1.65f,definitions.Length*.75f+.8f);
            camera.transform.position=new Vector3(0,-(definitions.Length-1)*.75f,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.08f,.08f);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/held-prop-glow.png")),image.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
            CheckOverlap(camera,artwork);
            Debug.Log("Свечение предмета: PASS — удержание, отпускание на паузе, сохранность заполненного контейнера.");
            PrototypeBuild.Build();
        }
        private static void CheckOverlap(Camera camera,PropArtwork artwork)
        {
            foreach(var view in UnityEngine.Object.FindObjectsByType<PropView>(FindObjectsSortMode.None)) view.gameObject.SetActive(false);
            var back=artwork.CreateView("prototype-crate","Задний светящийся ящик",1.2f);
            back.DrawMotion(new PropState {id="back",held=true,x=-.35f},"local",false,0);
            var front=artwork.CreateView("prototype-crate","Передний ящик",1.2f);
            front.DrawMotion(new PropState {id="front",x=.15f},"local",false,0);
            front.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder++;
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            camera.transform.position=new Vector3(0,0,-10);camera.orthographicSize=1.5f;
            camera.backgroundColor=new Color(0,0,0,0);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
                var combined=image.GetPixels32();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/held-prop-overlap.png")),image.EncodeToPNG());
                back.gameObject.SetActive(false);
                camera.Render();image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();var alone=image.GetPixels32();
                var min=camera.WorldToScreenPoint(front.Artwork.bounds.min);var max=camera.WorldToScreenPoint(front.Artwork.bounds.max);
                int compared=0;
                for(int y=Mathf.CeilToInt(min.y)+10;y<Mathf.FloorToInt(max.y)-10;y++)
                    for(int x=Mathf.CeilToInt(min.x)+10;x<Mathf.FloorToInt(max.x)-10;x++)
                    {
                        int index=y*1920+x;
                        // The source crate has translucent pixels inside its rectangular bounds.
                        if(alone[index].a!=255) continue;
                        compared++;
                        if(!combined[index].Equals(alone[index])) throw new Exception("Свечение заднего ящика перекрыло непрозрачную часть переднего.");
                    }
                if(compared<1000) throw new Exception("Недостаточно пикселей для проверки перекрытия ящиков.");
                Debug.Log($"Целостность ящиков: PASS, сравнение {compared} пикселей переднего предмета.");
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
