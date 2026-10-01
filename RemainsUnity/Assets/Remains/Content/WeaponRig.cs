using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class WeaponRig
    {
        public string id;
        public int x,y,width,height;
        public float pivotX,pivotY,muzzleX,muzzleY;
        public bool HasMuzzle=>muzzleX>0;
    }
    public static class WeaponRigs
    {
        [Serializable] private sealed class Library {public float pixelsPerUnit,aimRadius,anchorHeight;public WeaponRig[] weapons;}
        private static readonly Dictionary<string,WeaponRig> rigs=new(StringComparer.Ordinal);
        public static float PixelsPerUnit {get;private set;}
        public static float AimRadius {get;private set;}
        public static float AnchorHeight {get;private set;}
        public static IEnumerable<WeaponRig> All=>rigs.Values;
        public static WeaponRig Get(string id)=>rigs.TryGetValue(id,out var rig)?rig:
            throw new InvalidOperationException($"Нет графической позы оружия: {id}.");
        public static void Load()
        {
            if(rigs.Count>0) return;
            var data=Resources.Load<TextAsset>("Original/Weapons/artwork");
            if(data==null) throw new InvalidOperationException("Нет Original/Weapons/artwork.");
            var library=JsonUtility.FromJson<Library>(data.text);
            if(library?.weapons==null || !float.IsFinite(library.pixelsPerUnit) || library.pixelsPerUnit<=0 ||
                !float.IsFinite(library.aimRadius) || library.aimRadius<=0 || !float.IsFinite(library.anchorHeight))
                throw new InvalidOperationException("Некорректный масштаб графики оружия.");
            var validated=new Dictionary<string,WeaponRig>(StringComparer.Ordinal);
            foreach(var rig in library.weapons)
            {
                if(string.IsNullOrEmpty(rig.id) || rig.width<=0 || rig.height<=0 || rig.x<0 || rig.y<0 ||
                    !float.IsFinite(rig.pivotX) || !float.IsFinite(rig.pivotY) || !float.IsFinite(rig.muzzleX) || !float.IsFinite(rig.muzzleY))
                    throw new InvalidOperationException($"Некорректная графическая поза оружия: {rig.id}.");
                validated.Add(rig.id,rig);
            }
            PixelsPerUnit=library.pixelsPerUnit;
            AimRadius=library.aimRadius;AnchorHeight=library.anchorHeight;
            foreach(var pair in validated) rigs.Add(pair.Key,pair.Value);
        }
    }
}
