using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Remains.Editor
{
    public static class PonyPrefabChecks
    {
        public static void Run()
        {
            PonyVisual.Prepare();
            if(!EntityAssetCatalog.Load().TryGetActorPrefab(ActorTypes.Get("pony"),out var prefab)) throw new InvalidOperationException("Нет префаба пони.");
            var actor=new ActorState {id="prefab-test",pose="idle"};
            var first=(PonyVisual)ActorVisualFactory.Create(actor);var second=(PonyVisual)ActorVisualFactory.Create(actor);
            try
            {
                int count=first.transform.childCount;first.Bind(actor);first.DrawPose(0);
                var other=new ActorState {id="other",pose="run",hair=1,facing=-1};second.Bind(other);second.DrawPose(.1f);
                foreach(var clip in PonyAnimationData.Library.clips)
                {
                    actor.pose=clip.id;first.DrawPose(.1f,true);
                    if(first.transform.childCount!=count) throw new InvalidOperationException("Анимация изменила структуру префаба пони.");
                }
                if(second.DrawnClip!="run" || second.transform.localScale.x!=-1 || prefab.transform.localScale!=Vector3.one)
                    throw new InvalidOperationException("Анимация одного пони изменила другой экземпляр или исходный префаб.");
                Debug.Log("Префаб пони: PASS — готовая структура, все позы, независимость экземпляров.");
            }
            finally {UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);}
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);WeaponCatalog.Load();PonyVisual.Prepare();Run();
            EntityAssetCatalog.Load().TryGetActorPrefab(ActorTypes.Get("pony"),out var prefab);
            UnityEngine.Object.Instantiate(prefab);
            var actor=new ActorState {id="runtime",pose="idle",hair=0,x=3};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.Bind(actor);visual.DrawPose(0);
            var camera=new GameObject("Предпросмотр префаба и игровой позы").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.3f;
            camera.transform.position=new Vector3(1.5f,1,-10);camera.backgroundColor=new Color(.2f,.2f,.2f);
            Remains.Runtime.NetworkFrameCapture.Save(camera,System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/pony-prefab-preview.png")));
        }
        public static void RenderAndCheckLayers() {Render();CrossingActorRender.Render();}
    }
}
