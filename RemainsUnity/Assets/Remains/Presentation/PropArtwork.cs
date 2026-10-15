using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    public sealed class PropArtwork
    {
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,PropView> prefabs=new();
        private readonly InventoryArtwork items=new();
        private readonly PropView droppedPrefab;
        public PropArtwork()
        {
            items.PrepareWorldMaterials();
            droppedPrefab=Resources.Load<PropView>("Presentation/DroppedItem");
            if(droppedPrefab==null || droppedPrefab.Artwork==null)
                throw new InvalidOperationException("Нет сохранённого префаба брошенного предмета Presentation/DroppedItem или ссылки на его спрайт.");
            var builtIn=EntityAssetCatalog.Load();
            foreach(var definition in PropCatalog.All)
            {
                // Registered external definitions retain the resource contract; built-in assets use explicit references.
                if(builtIn.TryGetPropPrefab(definition.id,definition.resource,out var prefab))
                {
                    if(prefab.ContainerArtwork!=null)
                    {
                        prefab.ContainerArtwork.Validate(definition.id);
                        SpriteArtworkMaterial.Prepare(prefab.ContainerArtwork.Closed);
                        if(prefab.ContainerArtwork.Mode==ContainerArtworkMode.ClosedOpened) SpriteArtworkMaterial.Prepare(prefab.ContainerArtwork.Opened);
                    }
                    prefabs.Add(definition.id,prefab);sprites.Add(definition.id,prefab.Artwork.sprite);continue;
                }
                var texture=Resources.Load<Texture2D>(definition.resource);
                if(texture==null) throw new InvalidOperationException($"Нет графики объекта: type={definition.id}, resource={definition.resource}.");
                var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),50);
                SpriteArtworkMaterial.Prepare(sprite);sprites.Add(definition.id,sprite);
            }
        }
        public Sprite Get(string id)=>sprites.TryGetValue(id,out var sprite)?sprite:throw new InvalidOperationException("Нет спрайта физического объекта: "+id);
        public PropView CreateContainerView(ContainerState state)
        {
            if(state.presentationItem==null)
            {
                var containerView=CreateView(state.definition,"Контейнер "+state.id,state.width);
                containerView.ApplyContainerAppearance(state);return containerView;
            }
            var view=UnityEngine.Object.Instantiate(droppedPrefab);view.name="Брошенный предмет "+state.id;
            var renderer=view.Artwork;
            SpriteArtworkMaterial.Apply(renderer,items.Require(state.presentationItem));
            renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;view.SetArtwork(renderer);
            // A mine item is the mine itself (original artwork at world scale, pivot at its feet), not a shrunk icon.
            if(MineRules.PlacedWeapon(state.presentationItem)!=null) view.ConfigureFeetArtwork();
            view.FitWidth(state.width);view.PrepareHeldGlow();return view;
        }
        public PropView CreateView(string id,string name,float width)
        {
            PropView view;
            if(prefabs.TryGetValue(id,out var prefab)) view=UnityEngine.Object.Instantiate(prefab);
            else
            {
                view=new GameObject(name).AddComponent<PropView>();var renderer=view.gameObject.AddComponent<SpriteRenderer>();
                SpriteArtworkMaterial.Apply(renderer,Get(id));renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;view.SetArtwork(renderer);
            }
            view.name=name;view.FitWidth(width);view.PrepareHeldGlow();return view;
        }
    }
}
