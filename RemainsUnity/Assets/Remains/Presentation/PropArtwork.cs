using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
namespace Remains.Presentation
{
    public sealed class PropArtwork
    {
        private readonly Dictionary<string,Sprite> sprites=new();
        public PropArtwork()
        {
            var builtIn=EntityAssetCatalog.Load();
            foreach(var definition in PropCatalog.All)
            {
                // Registered external definitions retain the resource contract; built-in assets use explicit references.
                if(!builtIn.TryGetPropTexture(definition.id,definition.resource,out var texture))
                    texture=Resources.Load<Texture2D>(definition.resource);
                if(texture==null) throw new InvalidOperationException($"Нет графики объекта: type={definition.id}, resource={definition.resource}.");
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),50);
                SpriteArtworkMaterial.Prepare(sprite);sprites.Add(definition.id,sprite);
            }
        }
        public Sprite Get(string id)=>sprites.TryGetValue(id,out var sprite)?sprite:throw new InvalidOperationException("Нет спрайта физического объекта: "+id);
    }
}
