using System.IO;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponArtworkChecks
    {
        [System.Serializable] private sealed class ReserveWeaponFrame
        {
            public int schema=1,width,height;
            public bool explicitDiagnosticFixture=true,originalRuntimeVerified=false;
            public float cameraSize;
            public string pngSha256;
            public ReserveWeapon[] weapons;
            public CombatHudRender.ReserveSpriteFrame casing;
        }
        [System.Serializable] private sealed class ReserveWeapon
        {
            public string id;
            public CombatHudRender.ReserveSpriteFrame sprite;
            public float[] position,rendererBounds,muzzle;
        }
        public static void RenderVectorReserve()
        {
            VectorArtworkReserveChecks.VerifyEntry("spear");VectorArtworkReserveChecks.VerifyEntry("casing");
            WeaponCatalog.Load();PonyVisual.Prepare();WeaponRigs.Load();CasingEffectCatalog.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var views=new List<WeaponView>();int index=0;
            foreach(string id in new[]{"spear","crowbar","pistol","energy","grenade"})
            {
                var actor=new ActorState {id=id,x=index*3,pose="idle"};
                var pony=new GameObject(id+" пони").AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
                var weapon=Object.Instantiate(WeaponRigs.Prefab(id));weapon.name=id;
                weapon.transform.position=actor.Position+new Vector2(.8f,.8f);views.Add(weapon);index++;
            }
            if(!CasingEffectCatalog.TryGet("pistol",out var casingProfile))
                throw new System.InvalidOperationException("Рендер векторного резерва: нет действительного профиля гильзы пистолета.");
            var casing=new GameObject("Гильза — неподвижный диагностический рисунок").AddComponent<SpriteRenderer>();
            casing.sprite=casingProfile.Sprite;casing.color=Color.white;casing.transform.position=new Vector3(7.2f,.4f,-.01f);
            var material=WorldArtworkMaterial.Create(casing.sprite.texture);casing.sharedMaterial=material;
            var camera=new GameObject("Камера векторного оружия").AddComponent<Camera>();
            // Same horizontal span/density as legacy1920×480/size2, now a full16:9 frame.
            camera.orthographic=true;camera.orthographicSize=4.5f;
            camera.transform.position=new Vector3(6.3f,.8f,-10);camera.backgroundColor=new Color(.16f,.17f,.18f);
            string folder=VectorArtworkReserveManifest.RepositoryPath(".reference/tests/vector-artwork-r59/render");Directory.CreateDirectory(folder);
            try
            {
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160)})
                {
                    var target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                    var image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
                    try
                    {
                        foreach(var view in views) CheckVisible(camera,view.Artwork.bounds,view.Id);
                        CheckVisible(camera,casing.bounds,"pistol-casing");
                        CombatHudRender.RenderReserveCamera(camera,target);RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        string path=Path.Combine(folder,$"weapon-{size.x}x{size.y}.png");File.WriteAllBytes(path,image.EncodeToPNG());
                        var evidence=new ReserveWeaponFrame {width=size.x,height=size.y,cameraSize=camera.orthographicSize,
                            pngSha256=VectorArtworkReserveManifest.Hash(path),casing=CombatHudRender.DescribeReserveSprite(casing.sprite),
                            weapons=views.ConvertAll(view=>new ReserveWeapon {id=view.Id,sprite=CombatHudRender.DescribeReserveSprite(view.Artwork.sprite),
                                position=new[]{view.transform.position.x,view.transform.position.y,view.transform.position.z},
                                rendererBounds=new[]{view.Artwork.bounds.min.x,view.Artwork.bounds.min.y,view.Artwork.bounds.max.x,view.Artwork.bounds.max.y},
                                muzzle=view.Muzzle==null?null:new[]{view.Muzzle.position.x,view.Muzzle.position.y,view.Muzzle.position.z}}).ToArray()};
                        File.WriteAllText(Path.ChangeExtension(path,".json"),StateJson.ToJson(evidence,true));
                        Debug.Log("Векторный резерв оружия: действительные Sprite/профиль гильзы, "+size+", "+path);
                    }
                    finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
                }
            }
            finally {Object.DestroyImmediate(material);}
        }
        private static void CheckVisible(Camera camera,Bounds bounds,string id)
        {
            foreach(var corner in new[]{bounds.min,new Vector3(bounds.min.x,bounds.max.y,bounds.center.z),
                new Vector3(bounds.max.x,bounds.min.y,bounds.center.z),bounds.max})
            {
                var viewport=camera.WorldToViewportPoint(corner);
                if(viewport.x<0 || viewport.x>1 || viewport.y<0 || viewport.y>1 || viewport.z<camera.nearClipPlane)
                    throw new System.InvalidOperationException("Рендер векторного резерва: рисунок вышел за кадр, id="+id+", viewport="+viewport);
            }
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PonyVisual.Prepare();Remains.Content.WeaponRigs.Load();int index=0;
            foreach(string id in new[]{"spear","crowbar","pistol","energy","grenade"})
            {
                var actor=new ActorState {id=id,x=index*3,pose="idle"};
                var pony=new GameObject(id+" пони").AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
                var weapon=Object.Instantiate(Remains.Content.WeaponRigs.Prefab(id));weapon.name=id;
                weapon.transform.position=actor.Position+new Vector2(.8f,.8f);
                index++;
            }
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Камера оружия").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=2;
            camera.transform.position=new Vector3(6.3f,.8f,-10);camera.backgroundColor=new Color(.16f,.17f,.18f);
            var target=new RenderTexture(1920,480,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,480,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1920,480),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/weapon-artwork.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("Размеры оружия относительно пони: "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
