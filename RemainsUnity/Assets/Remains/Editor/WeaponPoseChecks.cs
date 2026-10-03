using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponPoseChecks
    {
        public static void Run()
        {
            foreach(string weapon in new[]{"pistol","energy"})
            foreach(float angle in new[]{0f,90f,180f,270f})
            {
                var actor=new ActorState {weapon=weapon,climbing=true,weaponReach=1};
                Require(Vector2.Distance(WeaponPose.Mount(actor),WeaponPose.RangedAnchor(actor))<.0001f,"Лестница сохранила вынос из предыдущей стойки");
                var target=WeaponPose.RangedAnchor(actor)+new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*5;
                WeaponPose.Aim(actor,target);
                Require(actor.weaponReach==0 && Mathf.Abs(Vector3.Cross(target-WeaponPose.Muzzle(actor),WeaponPose.Direction(actor)).z)<.0001f,
                    "На лестнице точка крепления или направление дула неверны");
                var copy=actor.Copy();
                Require(Vector2.Distance(WeaponPose.Muzzle(copy),WeaponPose.Muzzle(actor))<.0001f,"Копия потеряла позу оружия на лестнице");
                actor.climbing=false;WeaponPose.Aim(actor,WeaponPose.RangedAnchor(actor)+Vector2.right*5);
                Require(actor.weaponReach>.5f,"Выход с лестницы оставил ограничение выноса");
            }
            foreach(string weapon in new[]{"pistol","energy"})
            foreach(float angle in new[]{0,45,90,135,180,225,270,315})
            foreach(float distance in new[]{1f,3f,20f})
            {
                var actor=new ActorState {weapon=weapon};
                var target=WeaponPose.RangedAnchor(actor)+new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*distance;
                WeaponPose.Aim(actor,target);
                var mount=WeaponPose.Mount(actor);var muzzle=WeaponPose.Muzzle(actor);var direction=WeaponPose.Direction(actor);
                float error=Mathf.Abs(Vector3.Cross(target-muzzle,direction).z);
                Require(error<.0001f && Vector2.Dot(target-muzzle,direction)>0,$"Луч ствола не проходит через курсор: weapon={weapon}, angle={angle}, distance={distance}, error={error}");
                Require(Vector2.Distance(mount,WeaponPose.RangedAnchor(actor))<=WeaponPose.AimRadius+.0001f,"Оружие вышло за радиус");
                Require(Mathf.Abs(mount.y-WeaponPose.RangedAnchor(actor).y)<.0001f,"Поворот ствола изменил высоту удержания");
                var copy=actor.Copy();
                Require(WeaponPose.Muzzle(copy)==muzzle,"Копия сетевого состояния меняет положение дула");
            }
            foreach(string weapon in new[]{"spear","crowbar"})
            foreach(float angle in new[]{0f,90,180,270})
            foreach(float distance in new[]{0f,.2f,.7f,10f})
            {
                var actor=new ActorState {weapon=weapon,weaponReach=1};
                var direction=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
                var origin=WeaponPose.Origin(actor);
                WeaponPose.Aim(actor,origin+direction*distance);
                var tip=origin+WeaponPose.Direction(actor)*actor.weaponReach;
                var expected=origin+direction*Mathf.Min(distance,WeaponCatalog.Get(weapon).range);
                Require(Vector2.Distance(tip,expected)<.0001f,$"Ближнее оружие не следует близкому курсору: {weapon}, {distance}, {angle}");
                var copy=actor.Copy();
                Require(Vector2.Distance(WeaponPose.Origin(copy)+WeaponPose.Direction(copy)*copy.weaponReach,tip)<.0001f,
                    "Копия состояния меняет положение ближнего оружия");
            }
            Debug.Log("Поза оружия: PASS — дуло, радиус, близкий курсор ближнего оружия и копирование состояния.");
        }

        public static void Render()=>Render(false);
        public static void RenderMelee()=>Render(true);
        public static void RenderSwing()=>Render(true,true);
        public static void RenderLadder()=>Render(false,false,true);
        private static void Render(bool melee,bool motion=false,bool climbing=false)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();WeaponCatalog.Load();Run();PonyAnimationData.Load();PonyVisual.Prepare();
            var artwork=WeaponArtwork.Load();int index=0;
            foreach(string weapon in melee?new[]{"spear","crowbar"}:new[]{"pistol","energy"})
            foreach(float angle in new[]{0f,45,135,180})
            {
                var actor=new ActorState {id=weapon+angle,weapon=weapon,x=(index%4)*4,y=-(index/4)*3,pose=climbing?"climb":"idle",climbing=climbing};
                float distance=!motion && melee && index%4<2 ? (index%4==0?.2f:.7f):4;
                var anchor=melee?WeaponPose.Origin(actor):WeaponPose.RangedAnchor(actor);
                float aimAngle=motion?0:angle;
                var aim=anchor+new Vector2(Mathf.Cos(aimAngle*Mathf.Deg2Rad),Mathf.Sin(aimAngle*Mathf.Deg2Rad))*distance;
                WeaponPose.Aim(actor,aim);
                if(motion)
                {
                    float progress=new[]{0f,1f/6,.5f,1f}[index%4];
                    var sweep=new MeleeSweep(weapon,anchor,WeaponPose.Direction(actor),actor.weaponReach);
                    sweep.Advance(anchor,sweep.Duration*progress,_=>{});
                    var delta=sweep.Tip-anchor;
                    actor.weaponReach=delta.magnitude;actor.weaponAngle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
                    var label=new GameObject("Фаза удара").AddComponent<TextMesh>();
                    label.text=$"{progress:P0}";label.fontSize=48;label.characterSize=.08f;
                    label.transform.position=new Vector3(actor.x-1,actor.y-.2f,-1);
                }
                var pony=new GameObject(actor.id).AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
                var sprite=new GameObject("Оружие "+actor.id).AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(sprite,artwork[weapon]);sprite.sortingOrder=100;
                sprite.transform.position=melee?WeaponPose.Origin(actor)+WeaponPose.Direction(actor)*(actor.weaponReach-artwork[weapon].bounds.max.x):WeaponPose.Mount(actor);sprite.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);sprite.flipY=WeaponPose.Flip(actor);
                var rig=WeaponRigs.Get(weapon);
                var visualMuzzle=(Vector2)sprite.transform.TransformPoint(melee?new Vector3(artwork[weapon].bounds.max.x,0,0):new Vector3(rig.muzzleX,sprite.flipY?-rig.muzzleY:rig.muzzleY,0));
                var expected=melee?WeaponPose.Origin(actor)+WeaponPose.Direction(actor)*actor.weaponReach:WeaponPose.Muzzle(actor);
                Require(Vector2.Distance(visualMuzzle,expected)<.0001f,"Конец картинки оружия отличается от игровой позы");
                var line=new GameObject("Направление из дула").AddComponent<LineRenderer>();
                line.material=new Material(Shader.Find("Sprites/Default"));line.positionCount=2;
                line.SetPosition(0,visualMuzzle);line.SetPosition(1,visualMuzzle+WeaponPose.Direction(actor)*.7f);
                line.startWidth=line.endWidth=.018f;line.startColor=line.endColor=Color.yellow;line.sortingOrder=120;
                index++;
            }
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Камера поз оружия").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=4;camera.transform.position=new Vector3(6,-.3f,-10);camera.backgroundColor=new Color(.15f,.16f,.17f);
            var target=new RenderTexture(1920,960,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", climbing?".reference/tests/weapon-ladder-poses.png":motion?".reference/tests/melee-swing-poses.png":melee?".reference/tests/melee-idle-poses.png":".reference/tests/weapon-poses.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("Кадр поз оружия: "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Поза оружия: "+message);}
    }
}
