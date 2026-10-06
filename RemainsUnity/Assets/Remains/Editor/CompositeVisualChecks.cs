using System;
using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // Pixel evidence: an opaque front actor must hide every part of the rear actor.
    public static class CompositeVisualChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PonyVisual.Prepare();
            var rear=new GameObject("Задний персонаж").AddComponent<PonyVisual>();
            var front=new GameObject("Передний персонаж").AddComponent<PonyVisual>();
            var rearState=new ActorState {id="a-rear",hair=0};
            var frontState=new ActorState {id="b-front",hair=1};
            rear.Bind(rearState);front.Bind(frontState);
            var layers=new ActorVisualLayers();
            var world=new WorldState();world.actors.Add(rearState);world.actors.Add(frontState);layers.Orders.Reconcile(world,rearState.level);
            var rearGroup=layers.Attach("a-rear",rear.transform);layers.Attach("b-front",front.transform);
            var weapon=new GameObject("Оружие заднего персонажа").AddComponent<SpriteRenderer>();
            var artwork=WeaponArtwork.Load();SpriteArtworkMaterial.Apply(weapon,artwork["pistol"]);
            weapon.sortingOrder=100;weapon.transform.position=new Vector3(0,1.2f,0);
            layers.Attach("a-rear",weapon.transform);layers.Synchronize(world,rearState.level);
            var camera=new GameObject("Камера наложения").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.8f;
            camera.transform.position=new Vector3(0,.8f,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var target=new RenderTexture(640,640,24);target.Create();camera.targetTexture=target;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/composite-visual"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach(string pose in new[]{"idle","run","down"})
                foreach(int facing in new[]{1,-1})
                {
                    rearState.pose=frontState.pose=pose;rearState.down=frontState.down=pose=="down";
                    layers.Orders.Reconcile(world,rearState.level);
                    rearState.facing=frontState.facing=facing;
                    rear.DrawPose(.3f);front.DrawPose(.3f);
                    rearGroup.gameObject.SetActive(false);
                    var expected=Capture(camera,target);
                    rearGroup.gameObject.SetActive(true);
                    var actual=Capture(camera,target);
                    try
                    {
                        var a=actual.GetPixels32();var e=expected.GetPixels32();int compared=0,wrong=0;
                        for(int i=0;i<a.Length;i++)
                        {
                            if(e[i].a<255) continue;
                            compared++;
                            if(Math.Abs(a[i].r-e[i].r)>2 || Math.Abs(a[i].g-e[i].g)>2 || Math.Abs(a[i].b-e[i].b)>2) wrong++;
                        }
                        File.WriteAllBytes(Path.Combine(directory,$"{pose}-{facing}.png"),actual.EncodeToPNG());
                        if(compared<1000 || wrong!=0)
                            throw new InvalidOperationException($"Части персонажей перемешались: поза={pose}, направление={facing}, пикселей={compared}, ошибок={wrong}.");
                        Debug.Log($"Целостность персонажа: поза={pose}, направление={facing}, проверено={compared}, ошибок={wrong}.");
                    }
                    finally {UnityEngine.Object.DestroyImmediate(expected);UnityEngine.Object.DestroyImmediate(actual);}
                }
            }
            finally {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }

        private static Texture2D Capture(Camera camera,RenderTexture target)
        {
            var previous=RenderTexture.active;
            // Batch rendering does not advance the normal LateUpdate sorting pass.
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            camera.Render();RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            RenderTexture.active=previous;
            return image;
        }
    }
}
