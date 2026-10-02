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
            WeaponRigs.Load();
            foreach(var rig in WeaponRigs.All)
            {
                var source=WeaponRigs.Prefab(rig.id);
                var view=UnityEngine.Object.Instantiate(source);
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
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            Debug.Log("Префабы оружия: PASS — точка дула, отражение и независимость экземпляров.");
        }
    }
}
