using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;
namespace Remains.Presentation
{
    public sealed class PropArtwork
    {
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,PropView> prefabs=new();
        public PropArtwork()
        {
            var builtIn=EntityAssetCatalog.Load();
            foreach(var definition in PropCatalog.All)
            {
                // Registered external definitions retain the resource contract; built-in assets use explicit references.
                if(builtIn.TryGetPropPrefab(definition.id,definition.resource,out var prefab))
                {prefabs.Add(definition.id,prefab);sprites.Add(definition.id,prefab.Artwork.sprite);continue;}
                var texture=Resources.Load<Texture2D>(definition.resource);
                if(texture==null) throw new InvalidOperationException($"Нет графики объекта: type={definition.id}, resource={definition.resource}.");
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),50);
                SpriteArtworkMaterial.Prepare(sprite);sprites.Add(definition.id,sprite);
            }
        }
        public Sprite Get(string id)=>sprites.TryGetValue(id,out var sprite)?sprite:throw new InvalidOperationException("Нет спрайта физического объекта: "+id);
        public PropView CreateView(string id,string name,float width)
        {
            PropView view;
            if(prefabs.TryGetValue(id,out var prefab)) view=UnityEngine.Object.Instantiate(prefab);
            else
            {
                view=new GameObject(name).AddComponent<PropView>();var renderer=view.gameObject.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(renderer,Get(id));renderer.sortingOrder=ActorVisualLayers.WorldObjectOrder;view.SetArtwork(renderer);
            }
            view.name=name;view.FitWidth(width);view.PrepareHeldGlow();return view;
        }
    }
}
