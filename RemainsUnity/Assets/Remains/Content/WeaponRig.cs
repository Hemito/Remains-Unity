using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class WeaponRig
    {
        public string id;
        public int x,y,width,height;
        public float pixelsPerUnit;
        public float pivotX,pivotY,muzzleX,muzzleY;
        public bool HasMuzzle=>muzzleX>0;
    }
    public static class WeaponRigs
    {
        [Serializable] private sealed class Library {public int schema=2;public float aimRadius,climbingAimRadius,anchorHeight,raiseHeight,raiseDelay;public HoldingArea ordinaryHoldingArea;public WeaponRig[] weapons;}
        private static readonly Dictionary<string,WeaponRig> rigs=new(StringComparer.Ordinal);
        private static readonly Dictionary<string,Remains.Presentation.WeaponView> prefabs=new(StringComparer.Ordinal);
        private static readonly Dictionary<string,string> attackPresentations=new(StringComparer.Ordinal);
        public static string PresentationId(string attack)=>attackPresentations.TryGetValue(attack,out var id)?id:
            throw new InvalidOperationException("Нет связи атаки с префабом оружия: "+attack);
        public static Remains.Presentation.WeaponView Prefab(string id)=>prefabs.TryGetValue(id,out var value)?value:throw new InvalidOperationException("Нет префаба оружия: "+id);
        public static string ManifestJson()=>JsonUtility.ToJson(new Library {aimRadius=AimRadius,climbingAimRadius=ClimbingAimRadius,anchorHeight=AnchorHeight,raiseHeight=RaiseHeight,raiseDelay=RaiseDelay,ordinaryHoldingArea=OrdinaryHoldingArea,weapons=rigs.Values.ToArray()});
        public static float AimRadius {get;private set;}
        public static float ClimbingAimRadius {get;private set;}
        public static float AnchorHeight {get;private set;}
        public static float RaiseHeight {get;private set;}
        public static float RaiseDelay {get;private set;}
        public static HoldingArea OrdinaryHoldingArea {get;private set;}
        public static IEnumerable<WeaponRig> All=>rigs.Values;
        public static WeaponRig Get(string id)=>rigs.TryGetValue(id,out var rig)?rig:
            throw new InvalidOperationException($"Нет графической позы оружия: {id}.");
        public static void Load()
        {
            var catalog=Resources.Load<WeaponAssetCatalog>("Content/WeaponCatalog");
            if(catalog==null) throw new InvalidOperationException("Нет Content/WeaponCatalog.");
            catalog.OrdinaryHoldingArea.Validate("Content/WeaponCatalog, обычные вещи");
            OrdinaryHoldingArea=catalog.OrdinaryHoldingArea;
            if(!float.IsFinite(catalog.ClimbingAimRadius) || catalog.ClimbingAimRadius<0 || catalog.ClimbingAimRadius>catalog.AimRadius)
                throw new InvalidOperationException("Вынос оружия на лестнице должен быть от нуля до обычного радиуса.");
            if(!float.IsFinite(catalog.RaiseHeight) || catalog.RaiseHeight<0 || !float.IsFinite(catalog.RaiseDelay) || catalog.RaiseDelay<0)
                throw new InvalidOperationException("Некорректная высота или задержка подъёма огнестрела.");
            RaiseHeight=catalog.RaiseHeight;RaiseDelay=catalog.RaiseDelay;
            var presentations=catalog.Presentations().ToArray();
            if(presentations.Length==0) throw new InvalidOperationException("В каталоге нет префабов оружия.");
            var definitions=presentations.Select(view=>view.ReadRig()).ToArray();
            var library=new Library {aimRadius=catalog.AimRadius,anchorHeight=catalog.AnchorHeight,weapons=definitions};
            if(library?.weapons==null ||
                !float.IsFinite(library.aimRadius) || library.aimRadius<=0 || !float.IsFinite(library.anchorHeight))
                throw new InvalidOperationException("Некорректный масштаб графики оружия.");
            var validated=new Dictionary<string,WeaponRig>(StringComparer.Ordinal);
            foreach(var rig in library.weapons)
            {
                if(string.IsNullOrEmpty(rig.id) || rig.width<=0 || rig.height<=0 || rig.x<0 || rig.y<0 ||
                    !float.IsFinite(rig.pixelsPerUnit) || rig.pixelsPerUnit<=0 ||
                    !float.IsFinite(rig.pivotX) || !float.IsFinite(rig.pivotY) || !float.IsFinite(rig.muzzleX) || !float.IsFinite(rig.muzzleY))
                    throw new InvalidOperationException($"Некорректная графическая поза оружия: {rig.id}.");
                validated.Add(rig.id,rig);
            }
            AimRadius=library.aimRadius;AnchorHeight=library.anchorHeight;
            ClimbingAimRadius=catalog.ClimbingAimRadius;
            var bindings=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var weapon in catalog.PresentedWeapons()) bindings.Add(weapon.Id,weapon.Presentation.Id);
            rigs.Clear();prefabs.Clear();attackPresentations.Clear();
            foreach(var pair in validated) rigs.Add(pair.Key,pair.Value);
            foreach(var view in presentations) prefabs.Add(view.Id,view);
            foreach(var pair in bindings) attackPresentations.Add(pair.Key,pair.Value);
        }
    }
}
