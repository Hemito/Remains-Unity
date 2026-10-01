using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class ShotVisualChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();ShotArtwork.Prepare();
            var actor=new ActorState {id="target",x=4};
            var attack=new AttackEvent {id="visual-check",attacker="other",weapon="pistol",level=actor.level,originY=.7f,endX=10,endY=.7f};
            float limit=AttackContacts.VisualLimit(new[]{actor},attack,true,1);
            Require(limit>0 && limit<.5f,"След не ограничен корпусом ближайшей цели");
            Require(AttackContacts.VisualLimit(new[]{actor},attack,true,.1f)==.1f,"Цель за стеной удлинила след");
            foreach(string weapon in new[]{"pistol","energy"})
            for(int i=0;i<4;i++)
            {
                float y=(weapon=="pistol"?3:0)-i*.7f;
                attack.weapon=weapon;attack.originX=0;attack.originY=y;attack.endX=6;attack.endY=y;
                Require(ShotArtwork.TryGet(weapon,out var artwork),"Нет графики оружия "+weapon);
                var visual=new GameObject(weapon+i).AddComponent<ShotVisual>();visual.Initialize(attack,.75f,artwork);
                float elapsed=weapon=="pistol"?new[]{.01f,.03f,.055f,.09f}[i]:i*.03f;
                visual.Draw(elapsed);
                var view=visual.GetComponent<SpriteRenderer>();
                if(weapon=="pistol")
                    Require(view.bounds.min.x>=-.001f && view.bounds.max.x<=4.501f,"След вышел за отрезок от дула до попадания");
            }
            var camera=new GameObject("Камера эффектов").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=3;camera.transform.position=new Vector3(3,.5f,-10);camera.backgroundColor=new Color(.10f,.11f,.12f);
            var target=new RenderTexture(1200,900,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1200,900,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/shot-visuals.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Эффекты выстрелов: PASS — границы и кадр "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException(message);}
    }
}
