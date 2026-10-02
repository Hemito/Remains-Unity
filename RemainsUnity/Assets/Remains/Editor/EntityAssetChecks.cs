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
            foreach(var definition in catalog.CreateProps())
                if(!catalog.TryGetPropTexture(definition.id,definition.resource,out var texture) || texture==null)
                    throw new InvalidOperationException("Нет явной ссылки текстуры: "+definition.id);
            var propAsset=Resources.Load<PropAsset>("Content/props/mcrate1");var originalTexture=propAsset.Texture;
            var replacement=new Texture2D(2,2);
            try
            {
                propAsset.SetTexture(replacement);
                var artwork=new Remains.Presentation.PropArtwork();
                if(artwork.Get("mcrate1").texture!=replacement) throw new InvalidOperationException("Отрисовка игнорирует явную ссылку текстуры SO.");
                propAsset.SetTexture(null);bool rejected=false;
                try {catalog.TryGetPropTexture("mcrate1",propAsset.CreateDefinition().resource,out _);}
                catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Отсутствие ссылки текстуры скрыто загрузкой старого ресурса.");
            }
            finally {propAsset.SetTexture(originalTexture);UnityEngine.Object.DestroyImmediate(replacement);}
            Debug.Log("SO сущностей: PASS — независимые копии и загрузка персонажей/физических объектов.");
        }
    }
}
