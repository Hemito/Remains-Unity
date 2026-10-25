using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponPrefabChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();WeaponRigs.Load();
            foreach(var rig in WeaponRigs.All)
            {
                var source=WeaponRigs.Prefab(rig.id);
                var registration=source.RestReference;registration.Validate("Контракт префаба "+rig.id);
                var minimum=new Vector2(-rig.width*rig.pivotX,-rig.height*rig.pivotY)/rig.pixelsPerUnit;
                var maximum=minimum+new Vector2(rig.width,rig.height)/rig.pixelsPerUnit;
                if(rig.pixelsPerUnit!=registration.PixelsPerUnit || Vector2.Distance(minimum,registration.Bounds.min)>.00001f ||
                    Vector2.Distance(maximum,registration.Bounds.max)>.00001f)
                    throw new InvalidOperationException("Каталог изменил плотность пикселей или размеры оружия в мире: "+rig.id);
                var view=UnityEngine.Object.Instantiate(source);
                view.SetFrame(view.ResolveRestSprite()); // Edit-mode instances do not execute Awake.
                try
                {
                    foreach(bool flip in new[]{false,true})
                    foreach(float angle in new[]{-130f,0f,47f,180f})
                    {
                        var actor=new ActorState {weapon=rig.id,x=2,y=3,weaponAngle=angle,weaponReach=.8f,weaponFlipY=flip};
                        view.transform.position=WeaponPose.Mount(actor);
                        view.transform.rotation=Quaternion.Euler(0,0,angle);view.SetMirrored(flip);
                        if(rig.HasMuzzle && Vector2.Distance(view.Muzzle.position,WeaponPose.Muzzle(actor))>.00001f)
                            throw new InvalidOperationException("Префаб и расчёт выстрела расходятся: "+rig.id);
                    }
                    if(source.transform.localScale!=Vector3.one || source.Artwork.flipY)
                        throw new InvalidOperationException("Экземпляр изменил исходный префаб: "+rig.id);
                    var attachment=new GameObject("Дополнительная часть оружия").AddComponent<SpriteRenderer>();
                    attachment.transform.SetParent(view.transform,false);attachment.sprite=view.Artwork.sprite;
                    var disabledPart=new GameObject("Скрытая часть оружия");disabledPart.transform.SetParent(view.transform,false);disabledPart.SetActive(false);
                    view.SetVisible(false);
                    if(view.Artwork.gameObject.activeInHierarchy || attachment.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Скрытое оружие оставило видимой дочернюю часть: "+rig.id);
                    view.SetVisible(true);
                    if(!attachment.gameObject.activeInHierarchy || disabledPart.activeSelf || !source.gameObject.activeSelf)
                        throw new InvalidOperationException("Возврат оружия изменил авторскую видимость частей или исходный префаб: "+rig.id);
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            CheckGrenadeRelease();
            Debug.Log("Префабы оружия: PASS — индивидуальная плотность пикселей, размеры в мире, точка дула, отражение, независимость экземпляров и непрерывность изображения гранаты при выпуске.");
        }
        private static void CheckGrenadeRelease()
        {
            var held=UnityEngine.Object.Instantiate(WeaponRigs.Prefab("grenade"));
            var flight=UnityEngine.Object.Instantiate(GrenadeArtwork.LoadPrefab());
            try
            {
                foreach(string stance in new[]{"standing","crouching","climbing"})
                foreach(float aimX in new[]{-4f,4f})
                {
                    var actor=new ActorState {id="grenade-release",weaponInventoryVersion=1,x=2,y=3,
                        crouched=stance=="crouching",climbing=stance=="climbing"};
                    var item=SharedInventory.Create("grenade",2);actor.inventory.Add(item);
                    if(!WeaponInventory.Select(actor,item.id,out var reason)) throw new InvalidOperationException(reason);
                    var aim=actor.Position+new Vector2(aimX,2);
                    WeaponPose.Aim(actor,aim);
                    held.transform.position=WeaponPose.Mount(actor);held.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);
                    held.SetMirrored(WeaponPose.Flip(actor));
                    var request=Remains.Networking.GrenadeLaunchRequest.Capture(actor,aim);
                    flight.transform.position=new Vector3(request.originX,request.originY,0);
                    flight.transform.rotation=Quaternion.Euler(0,0,request.angle);flight.flipY=request.mirrored;
                    for(int corner=0;corner<4;corner++)
                        if(Vector2.Distance(Corner(held.Artwork,corner),Corner(flight,corner))>.00001f)
                            throw new InvalidOperationException($"Граната меняет положение/размер при выпуске: стойка={stance}, прицел={aimX}, угол={corner}.");
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(held.gameObject);UnityEngine.Object.DestroyImmediate(flight.gameObject);}
        }
        private static Vector3 Corner(SpriteRenderer renderer,int corner)
        {
            var bounds=renderer.sprite.bounds;
            var point=new Vector3(corner%2==0?bounds.min.x:bounds.max.x,corner<2?bounds.min.y:bounds.max.y,0);
            if(renderer.flipX) point.x=-point.x;if(renderer.flipY) point.y=-point.y;
            return renderer.transform.TransformPoint(point);
        }
    }
}
