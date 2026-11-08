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
        public static void Run()=>Run(true);
        // ContractChecks: the same cases without writing before/after frames into the repository.
        public static void RunWithoutCapture()=>Run(false);
        public static void RunTransformsBatch()
        {
            try {Run(false);EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Run(bool capture)
        {
            WeaponRigs.Load();CheckActorRoles();
            int cases=0;
            foreach(var rig in WeaponRigs.All)
            {
                var view=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(rig.id));
                Sprite ownedSprite=null;Material ownedMaterial=null;
                try
                {
                    view.SetFrame(view.ResolveRestSprite());
                    view.PrepareGrip(new Color(.2f,.5f,1));view.DrawGrip(true);
                    // MonoBehaviour teardown is otherwise suppressed in EditMode: opt this fixture into its callbacks.
                    view.GripGlow.runInEditMode=true;
                    var glow=view.GripGlow.Renderer;
                    var sprite=glow.sprite;var material=glow.sharedMaterial;
                    ownedSprite=sprite;ownedMaterial=material;
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
                Require(ownedSprite==null && ownedMaterial==null,"Уничтожение оружия оставило ресурсы дымки: "+rig.id);
            }
            if(capture)Capture();
            Debug.Log("WEAPON_GRIP_GLOW_PASS: transform cases="+cases+"; color, reuse, hide, native material/sprite release; GPU capture="+capture+".");
        }
        public static void RunRolesBatch()
        {
            try {WeaponRigs.Load();CheckActorRoles();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void CheckActorRoles()
        {
            ActorTypes.Load();PonyAppearance.Prepare();
            foreach(string id in new[]{"pony","raider2-mach","raider4-axe","raider4-shovel"})
            {
                var actor=new ActorState {id="grip-"+id,actorType=id};
                Require(WeaponGripGlow.UsesLevitatedWeapon(actor),"Телекинетическое оружие пропущено: "+id);
                var view=UnityEngine.Object.Instantiate(WeaponRigs.Prefab("pistol"));
                Sprite ownedSprite=null;Material ownedMaterial=null;
                try
                {
                    var expected=PonyAppearance.FromRgb(PonyAppearance.Snapshot(actor).Magic);
                    view.PrepareGrip(expected);view.GripGlow.runInEditMode=true;view.DrawGrip(true);
                    var renderer=view.GripGlow.Renderer;ownedSprite=renderer.sprite;ownedMaterial=renderer.sharedMaterial;
                    Require(renderer.enabled && renderer.color.r==expected.r && renderer.color.g==expected.g && renderer.color.b==expected.b,
                        "Цвет телекинетического NPC не следует существующей политике внешности: "+id);
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
                Require(ownedSprite==null && ownedMaterial==null,"Дымка NPC оставила ресурсы после уничтожения: "+id);
            }
            foreach(var type in EntityAssetCatalog.Load().CreateActors())
                if(type.atlasWeaponSocket || type.behavior==NpcCreatureBehaviours.Turret)
                    Require(!WeaponGripGlow.UsesLevitatedWeapon(new ActorState {actorType=type.id}),"Дымка добавлена оружию в зубах/турели: "+type.id);
            Debug.Log("WEAPON_GRIP_ROLES_PASS: pony and three real levitated raiders, existing appearance color policy, socket/turret exclusions, material/sprite release.");
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
                    v.SetMirrored(i%2==1);v.PrepareGrip(new Color(.2f,.7f,1));v.GripGlow.runInEditMode=true;v.DrawGrip(false);views.Add(v);
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
