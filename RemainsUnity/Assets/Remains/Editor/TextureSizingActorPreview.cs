using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // The real equipped draw plan and weapon prefab, sampled once, at closest-camera 4K density.
    // -executeMethod Remains.Editor.TextureSizingActorPreview.RunBatch -textureActorOut <file.png>
    public static class TextureSizingActorPreview
    {
        private const float PixelsPerMetre=216;
        private const int Layer=31;
        public static void RunBatch()
        {
            try
            {
                string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-textureActorOut");
                if(index<0 || index+1>=args.Length) throw new ArgumentException("Нужен -textureActorOut <файл.png>.");
                Render(Path.GetFullPath(args[index+1]));EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Render(string output)
        {
            PonyVisual pony=null;WeaponView weapon=null;GameObject cameraRoot=null;
            RenderTexture target=null;Texture2D image=null;var previous=RenderTexture.active;
            float oldWorldLight=Shader.GetGlobalFloat("_WorldLightEnabled"),oldRadiance=Shader.GetGlobalFloat("_RemainsRadianceEnabled");
            try
            {
                ActorTypes.Load();PropCatalog.Load();Anatomy.Load();WeaponCatalog.Load();PonyVisual.Prepare();WeaponRigs.Load();
                var actor=new ActorState
                {
                    id="texture-sizing-preview",character="texture-sizing-preview",actorType="pony",anatomy="pony",
                    player=true,connected=true,level="hub",pose="idle",poseTime=0,facing=1,x=0,y=0,
                    weapon="pistol",weaponAngle=12,weaponReach=.4f,weaponInventoryVersion=1,equippedWeaponItem="preview-pistol",
                    appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion,hairStyle=5,eyeStyle=6,
                        fur=0xA3A3A3,hair=0x2277CC,eyes=0x22CC44,magic=0x00FF00,secondaryHairEnabled=true,secondaryHair=0x99CCFF},
                    inventory=new List<ItemStack>{new(){id="preview-armor",definition="armor.pip",count=1},new(){id="preview-pistol",definition="pistol",count=1}}
                };
                if(!CosmeticEquipment.TryEquip(actor,"preview-armor",false,out string reason))
                    throw new InvalidOperationException("Предпросмотр текстур: не надет настоящий комплект armor.pip: "+reason);
                pony=ActorVisualFactory.Create(actor) as PonyVisual;
                if(pony==null) throw new InvalidOperationException("Предпросмотр текстур: actor factory не вернула PonyVisual.");
                pony.ConfigureMagicPreview(1);pony.Bind(actor);pony.DrawPose(0,true);
                if(!pony.CurrentDrawPlan.HasEquipment || !pony.CurrentDrawPlan.VisualIds.Contains("pony.armor.pip") ||
                    !pony.TryGetDrawnBounds(out var bounds) || pony.DrawnPartCount==0)
                    throw new InvalidOperationException("Предпросмотр текстур: настоящие части пони/брони не нарисованы.");
                pony.GetComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player);
                string weaponId=WeaponRigs.PresentationId(actor.weapon);
                weapon=UnityEngine.Object.Instantiate(WeaponRigs.Prefab(weaponId));
                var pose=WeaponPose.SampleRanged(actor);
                weapon.transform.position=pose.Root;weapon.transform.rotation=Quaternion.Euler(0,0,pose.Angle);weapon.SetMirrored(pose.Mirrored);
                weapon.SetVisible(true);weapon.Artwork.sortingOrder=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Player)+1;
                if(weapon.Artwork.sprite==null || weapon.Artwork.sharedMaterial==null) throw new InvalidOperationException("Предпросмотр текстур: нет рисунка/материала префаба оружия.");
                bounds.Encapsulate(weapon.Artwork.bounds);bounds.Expand(.4f);
                foreach(var root in new[]{pony.gameObject,weapon.gameObject})
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=Layer;
                int width=Mathf.CeilToInt(bounds.size.x*PixelsPerMetre),height=Mathf.CeilToInt(bounds.size.y*PixelsPerMetre);
                cameraRoot=new GameObject("Камера сравнения текстур пони");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.orthographicSize=height/(2*PixelsPerMetre);
                camera.aspect=width/(float)height;camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
                camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.16f,.16f,.19f,1);camera.allowHDR=false;camera.allowMSAA=false;
                Shader.SetGlobalFloat("_WorldLightEnabled",0);Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
                target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();camera.targetTexture=target;
                camera.Render();RenderTexture.active=target;
                image=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                int nonBackground=image.GetPixels32().Count(pixel=>pixel.g>pixel.r+15 || pixel.b>pixel.r+15);
                if(nonBackground<500) throw new InvalidOperationException("Предпросмотр текстур: слишком мало цветных пикселей, возможно реальный пони не отрисован: "+nonBackground);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));File.WriteAllBytes(output,image.EncodeToPNG());
                Debug.Log($"Предпросмотр текстур: {output}, {width}×{height}, {PixelsPerMetre} пикс./м, поза={pony.DrawnClip}/{pony.DrawnFrame}, частей={pony.DrawnPartCount}, броня=armor.pip, оружие={weaponId}, цветных пикселей={nonBackground}.");
            }
            finally
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",oldWorldLight);Shader.SetGlobalFloat("_RemainsRadianceEnabled",oldRadiance);
                RenderTexture.active=previous;if(target!=null) target.Release();
                foreach(var item in new UnityEngine.Object[]{pony?.gameObject,weapon?.gameObject,cameraRoot,target,image})
                    if(item!=null) UnityEngine.Object.DestroyImmediate(item);
            }
        }
    }
}
