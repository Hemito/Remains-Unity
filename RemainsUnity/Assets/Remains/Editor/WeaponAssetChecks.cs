using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponAssetChecks
    {
        public static void Run()
        {
            var asset=GameAssets.Load<WeaponAsset>("Content/Weapons/spear");
            if(asset==null) throw new InvalidOperationException("Нет ассета копья для проверки изоляции данных.");
            var first=asset.CreateDefinition();var second=asset.CreateDefinition();
            first.damage+=999;first.meleeMotion[0].angle+=999;
            var third=asset.CreateDefinition();
            if(second.damage!=third.damage || second.meleeMotion[0].angle!=third.meleeMotion[0].angle)
                throw new InvalidOperationException("Изменение рабочего определения затронуло общий ассет оружия.");
            var catalog=GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog");var before=catalog.CreateJson();
            WeaponCatalog.Load();WeaponCatalog.Get("spear").damage+=999;WeaponCatalog.Load();
            if(catalog.CreateJson()!=before || WeaponCatalog.Get("spear").damage!=third.damage)
                throw new InvalidOperationException("Каталог оружия изменяет исходные ассеты.");
            Debug.Log("ScriptableObject оружия: PASS — рабочие определения и вложенные ключи изолированы от ассетов.");
        }
    }
}
