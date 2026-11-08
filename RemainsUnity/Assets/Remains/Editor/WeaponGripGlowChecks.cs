using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class WeaponGripGlowChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            WeaponRigs.Load();
            int cases=0;
            foreach(var rig in WeaponRigs.All)
            {
                var view=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(rig.id));
                try
                {
                    view.SetFrame(view.ResolveRestSprite());
                    view.PrepareGrip(new Color(.2f,.5f,1));view.DrawGrip(true);
                    var glow=view.GripGlow.Renderer;
                    var sprite=glow.sprite;var material=glow.sharedMaterial;
                    foreach(bool mirrored in new[]{false,true})
                    foreach(float angle in new[]{0f,45f,90f,180f,270f})
                    {
                        view.SetMirrored(mirrored);view.transform.position=new Vector3(3,-2,0);
                        view.transform.rotation=Quaternion.Euler(0,0,angle);
                        var expected=view.transform.TransformPoint(view.GripCenter);
                        Require(Vector3.Distance(glow.transform.position,expected)<.00001f,"Дымка оторвалась от рукоятки: "+rig.id);
                        Require(glow.transform.parent==view.transform,"Дымка не следует за фактическим оружием.");
                        cases++;
                    }
                    for(int i=0;i<1000;i++) {view.DrawGrip(true);view.PrepareGrip(Color.magenta);}
                    Require(sprite==glow.sprite && material==glow.sharedMaterial,"Отрисовка пересоздала ресурсы.");
                    Require(glow.color.r==1 && glow.color.b==1,"Смена цвета держателя потеряна.");
                    view.DrawGrip(false);Require(!glow.enabled,"Отпускание оставило дымку.");
                    view.DrawGrip(true);view.SetVisible(false);Require(!glow.gameObject.activeInHierarchy,"Убранное оружие оставило дымку.");
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            Capture();
            Debug.Log("WEAPON_GRIP_GLOW_PASS: transform cases="+cases+"; color, reuse, hide and actual GPU before/after readback.");
        }
        private static void Capture()
        {
            var previous=GraphicsSettings.defaultRenderPipeline;
            var quality=QualitySettings.renderPipeline;
            var previousTarget=RenderTexture.active;
            var root=new GameObject("Grip capture");
            var target=new RenderTexture(1200,700,24);
            var pixels=new Texture2D(1200,700,TextureFormat.RGB24,false);
            var views=new System.Collections.Generic.List<WeaponView>();
            var plain=new Material(Shader.Find("Sprites/Default"));
            try
            {
                GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
                var camera=root.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.5f;
                camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.06f,.07f,.09f);camera.targetTexture=target;
                string[] ids={"pistol","energy","weapon:sword:grade:0","spear","crowbar","weapon:shotgun:grade:0"};
                for(int i=0;i<ids.Length;i++)
                {
                    var v=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(ids[i]),root.transform);
                    v.SetFrame(v.ResolveRestSprite());v.Artwork.sharedMaterial=plain;
                    v.transform.position=new Vector3((i%3-1)*2.2f,(i/3==0?1:-1),0);
                    v.transform.rotation=Quaternion.Euler(0,0,i*23);
                    v.SetMirrored(i%2==1);v.PrepareGrip(new Color(.2f,.7f,1));v.DrawGrip(false);views.Add(v);
                }
                string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Verification/pt32"));Directory.CreateDirectory(dir);
                Color32[] before=null;
                for(int pass=0;pass<2;pass++)
                {
                    foreach(var v in views) v.DrawGrip(pass==1);
                    camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1200,700),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(dir,pass==0?"before.png":"after.png"),pixels.EncodeToPNG());
                    var data=pixels.GetPixels32();
                    if(pass==0) before=data;
                    else
                    {
                        int changed=0;for(int i=0;i<data.Length;i++) if(!data[i].Equals(before[i])) changed++;
                        Require(changed>100,"Шейдер дымки не изменил фактический GPU кадр.");
                    }
                }
            }
            finally
            {
                RenderTexture.active=previousTarget;GraphicsSettings.defaultRenderPipeline=previous;QualitySettings.renderPipeline=quality;
                UnityEngine.Object.DestroyImmediate(root);target.Release();UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(plain);
            }
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
