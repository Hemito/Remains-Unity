using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // Compare opaque visible surfaces against isolated renders of complete actors.
    public static class CrossingActorRender
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();
            var artwork=WeaponArtwork.Load();var layers=new ActorVisualLayers();
            var groups=new ActorVisualGroup[3];
            var actors=new[]
            {
                new ActorState {id="a",x=-.45f,pose="idle",hair=0,weapon="pistol"},
                new ActorState {id="b",x=.1f,pose="crouch",crouched=true,hair=1,weapon="energy",facing=-1},
                new ActorState {id="c",x=.7f,pose="run",hair=0,weapon="crowbar"}
            };
            for(int i=0;i<actors.Length;i++)
            {
                var actor=actors[i];
                var visual=new GameObject("Пони "+actor.id).AddComponent<PonyVisual>();
                visual.Bind(actor);visual.DrawPose(.12f);
                groups[i]=layers.Attach(actor.id,visual.transform);
                WeaponPose.Aim(actor,actor.Position+new Vector2(i==1?-2:2,1.1f));
                var weapon=new GameObject("Оружие "+actor.id).AddComponent<SpriteRenderer>();
                var sprite=artwork[actor.weapon];SpriteArtworkMaterial.Apply(weapon,sprite);weapon.sortingOrder=100;
                bool ranged=WeaponRigs.Get(actor.weapon).HasMuzzle;
                weapon.transform.position=ranged?WeaponPose.Mount(actor):WeaponPose.Origin(actor)+WeaponPose.Direction(actor)*(actor.weaponReach-sprite.bounds.max.x);
                weapon.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);weapon.flipY=WeaponPose.Flip(actor);
                layers.Attach(actor.id,weapon.transform);
            }
            layers.Synchronize(new[]{"a","b","c"});
            var camera=new GameObject("Пересечение составных персонажей").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.5f;
            camera.transform.position=new Vector3(.2f,.8f,-10);camera.backgroundColor=Color.clear;
            var target=new RenderTexture(1200,600,24);target.Create();camera.targetTexture=target;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",".reference/tests/crossing-actors"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach(bool reverse in new[]{false,true})
                {
                    int[] order=reverse?new[]{2,1,0}:new[]{0,1,2};
                    for(int i=0;i<3;i++) groups[order[i]].Order=ActorVisualLayers.FirstActorOrder+i;
                    foreach(var group in groups) group.gameObject.SetActive(false);
                    var isolated=new Color32[3][];
                    for(int i=0;i<3;i++)
                    {
                        groups[order[i]].gameObject.SetActive(true);
                        camera.clearFlags=CameraClearFlags.SolidColor;
                        SortingGroup.UpdateAllSortingGroups();camera.Render();
                        var single=Read(target);
                        try {isolated[i]=single.GetPixels32();}
                        finally {UnityEngine.Object.DestroyImmediate(single);}
                        groups[order[i]].gameObject.SetActive(false);
                    }
                    foreach(var group in groups) group.gameObject.SetActive(true);
                    camera.clearFlags=CameraClearFlags.SolidColor;
                    SortingGroup.UpdateAllSortingGroups();camera.Render();
                    var actual=Read(target);
                    try
                    {
                        var a=actual.GetPixels32();int wrong=0,visible=0;
                        var compared=new int[3];
                        for(int i=0;i<a.Length;i++)
                        {
                            int front=2;
                            while(front>=0 && isolated[front][i].a==0) front--;
                            if(front<0) {if(a[i].a>0) wrong++;continue;}
                            visible++;
                            var expected=isolated[front][i];
                            // Translucent edges depend on blending with the layers beneath them.
                            if(expected.a!=255) continue;
                            compared[front]++;
                            if(Math.Abs(a[i].r-expected.r)>2 || Math.Abs(a[i].g-expected.g)>2 ||
                                Math.Abs(a[i].b-expected.b)>2 || a[i].a!=255) wrong++;
                        }
                        string name=reverse?"reverse":"forward";
                        File.WriteAllBytes(Path.Combine(directory,name+".png"),actual.EncodeToPNG());
                        if(Array.Exists(compared,count=>count<1000) || wrong!=0) throw new InvalidOperationException($"Пересечение актёров: порядок={name}, проверено по слоям={string.Join(",",compared)}, ошибок={wrong}.");
                        Debug.Log($"Пересечение актёров: PASS, порядок={name}, актёров=3, оружий=3, видимых пикселей={visible}, проверено по слоям={string.Join(",",compared)}, ошибок={wrong}.");
                    }
                    finally {UnityEngine.Object.DestroyImmediate(actual);}
                }
            }
            finally {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        private static Texture2D Read(RenderTexture target)
        {
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            RenderTexture.active=previous;return image;
        }
    }
}
