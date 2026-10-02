using System;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropPrefabChecks
    {
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            PropCatalog.Load();var artwork=new PropArtwork();int index=0;
            foreach(var definition in PropCatalog.All)
            {
                var view=artwork.CreateView(definition.id,definition.name,definition.width);
                view.transform.position=new Vector3(index++*2,definition.height/2,0);
            }
            var camera=new GameObject("Проверка префабов").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.25f;
            camera.transform.position=new Vector3(4,.7f,-10);camera.backgroundColor=new Color(.13f,.15f,.17f);
            var target=new RenderTexture(1500,375,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/tests/prop-prefabs.png"));
                System.IO.File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Просмотр префабов сохранён: "+path);
            }
            finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        public static void Run()
        {
            var catalog=EntityAssetCatalog.Load();var artwork=new PropArtwork();
            foreach(var definition in catalog.CreateProps())
            {
                if(!catalog.TryGetPropPrefab(definition.id,definition.resource,out var prefab)) throw new InvalidOperationException("Нет префаба "+definition.id);
                var scale=prefab.transform.localScale;var tint=prefab.Artwork.color;
                PropView first=null,second=null;
                try
                {
                    first=artwork.CreateView(definition.id,"Проверка исходной ширины",definition.width);
                    second=artwork.CreateView(definition.id,"Проверка ширины сохранения",definition.width*2);
                    if(Mathf.Abs(first.Artwork.bounds.size.x-definition.width)>.001f || Mathf.Abs(second.Artwork.bounds.size.x-definition.width*2)>.001f)
                        throw new InvalidOperationException("Размер экземпляра не соответствует состоянию мира: "+definition.id);
                    if(first.Artwork.sharedMaterial!=prefab.Artwork.sharedMaterial || first.Artwork.sprite!=prefab.Artwork.sprite)
                        throw new InvalidOperationException("Экземпляр не использует графику префаба: "+definition.id);
                    first.Artwork.color=Color.magenta;first.SetVisible(false);
                    if(second.Artwork.color!=tint || prefab.Artwork.color!=tint || prefab.transform.localScale!=scale || !second.gameObject.activeSelf)
                        throw new InvalidOperationException("Экземпляры или префаб разделяют изменяемое состояние: "+definition.id);
                }
                finally {if(first!=null) UnityEngine.Object.DestroyImmediate(first.gameObject);if(second!=null) UnityEngine.Object.DestroyImmediate(second.gameObject);}
            }
            var asset=Resources.Load<PropAsset>("Content/props/mcrate1");var original=asset.Prefab;
            try
            {
                asset.SetPrefab(null);bool rejected=false;
                try {new PropArtwork();} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Нет ошибки при удалённой ссылке на префаб.");
            }
            finally {asset.SetPrefab(original);}
            Debug.Log("Префабы объектов: PASS — все типы, материалы, размеры, изоляция экземпляров, отсутствие скрытой подстановки.");
        }
    }
}
