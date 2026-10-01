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
            var control=new GameObject("Контроль освещения").AddComponent<SpriteRenderer>();
            var controlSprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),new Vector2(.5f,.5f),Texture2D.whiteTexture.width);
            SpriteArtworkMaterial.Prepare(controlSprite);SpriteArtworkMaterial.Apply(control,controlSprite);
            control.transform.position=new Vector3(6,2,0);control.transform.localScale=Vector3.one*.4f;
            try
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",0);
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/shot-visuals.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());var bright=image.GetPixels32();
                Shader.SetGlobalTexture("_WorldLightMap",Texture2D.blackTexture);
                Shader.SetGlobalVector("_WorldLightBounds",new Vector4(-10,-10,.05f,.05f));Shader.SetGlobalFloat("_WorldLightEnabled",1);
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
                var dark=image.GetPixels32();
                int changed=0;
                for(int y=0;y<900;y++) for(int x=0;x<950;x++)
                {
                    var a=bright[y*1200+x];var b=dark[y*1200+x];
                    if(Math.Abs(a.r-b.r)>1 || Math.Abs(a.g-b.g)>1 || Math.Abs(a.b-b.b)>1) changed++;
                }
                Require(changed==0,$"Освещение затемняет эффекты: изменено пикселей {changed}.");
                var point=camera.WorldToViewportPoint(control.transform.position);int index=(int)(point.y*900)*1200+(int)(point.x*1200);
                Require(bright[index].r-dark[index].r>100,"Контрольный предмет не реагирует на освещение; сравнение недостоверно.");
                File.WriteAllBytes(Path.ChangeExtension(path,"dark.png"),image.EncodeToPNG());
                Debug.Log("Эффекты выстрелов: PASS — границы, кадры, неизменная яркость в темноте, обычный предмет затемняется: "+path);
            }
            finally {Shader.SetGlobalFloat("_WorldLightEnabled",0);RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException(message);}
    }
}
