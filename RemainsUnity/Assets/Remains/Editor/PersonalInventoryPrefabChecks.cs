using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;
using Remains.Content;
namespace Remains.Editor
{
    public static class PersonalInventoryPrefabChecks
    {
        public static void Run()
        {
            var source=GameAssets.Load<PersonalInventoryView>("Presentation/PersonalInventory");
            if(source==null) throw new InvalidOperationException("Нет личной страницы инвентаря.");source.Validate();
            var first=UnityEngine.Object.Instantiate(source);var second=UnityEngine.Object.Instantiate(source);
            try
            {
                var a=new InventoryPortrait(first.Portrait,first.PortraitParts);var b=new InventoryPortrait(second.Portrait,second.PortraitParts);
                int count=first.Portrait.transform.childCount;bool different=false;
                a.Refresh(new ActorState {hair=1});b.Refresh(new ActorState {hair=0});
                for(int i=0;i<first.PortraitParts.Length;i++)
                {
                    different|=first.PortraitParts[i].color!=second.PortraitParts[i].color;
                    if(second.PortraitParts[i].color!=source.PortraitParts[i].color)
                        throw new InvalidOperationException("Исходный портрет изменён рабочим экземпляром.");
                }
                if(!different || first.Portrait.transform.childCount!=count) throw new InvalidOperationException("Цвет портрета не изменился или структура пересоздаётся.");
                a.Refresh(null);if(first.Portrait.activeSelf || !second.Portrait.activeSelf) throw new InvalidOperationException("Видимость портретов не независима.");
                Debug.Log("Личный инвентарь: PASS — ссылки, сохранённая графика портрета, независимые цвета/видимость.");
            }
            finally {UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);}
        }
    }
}
