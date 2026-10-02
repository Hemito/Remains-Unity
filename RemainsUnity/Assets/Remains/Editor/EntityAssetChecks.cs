using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class EntityAssetChecks
    {
        public static void Run()
        {
            var catalog=EntityAssetCatalog.Load();string actors=catalog.ActorsJson(),props=catalog.PropsJson();
            var actorCopies=catalog.CreateActors();var propCopies=catalog.CreateProps();
            if(actorCopies.Length==0 || propCopies.Length==0) throw new InvalidOperationException("Пустой каталог сущностей.");
            actorCopies[0].restoreHealthAfterHit+=100;propCopies[0].mass+=100;
            if(actors!=catalog.ActorsJson() || props!=catalog.PropsJson()) throw new InvalidOperationException("Изменение экземпляров затронуло SO сущностей.");
            ActorTypes.Load();PropCatalog.Load();
            foreach(var definition in catalog.CreateActors()) if(ActorTypes.Get(definition.id).presentation!=definition.presentation) throw new InvalidOperationException("Тип персонажа не загружен из SO.");
            foreach(var definition in catalog.CreateProps()) if(PropCatalog.Get(definition.id).mass!=definition.mass) throw new InvalidOperationException("Масса объекта не загружена из SO.");
            Debug.Log("SO сущностей: PASS — независимые копии и загрузка персонажей/физических объектов.");
        }
    }
}
