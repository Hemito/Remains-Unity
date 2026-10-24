using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Каталог персонажей и объектов",fileName="EntityCatalog")]
    public sealed class EntityAssetCatalog : ScriptableObject
    {
        [SerializeField,InspectorName("Типы персонажей")] private ActorTypeAsset[] actors=Array.Empty<ActorTypeAsset>();
        [SerializeField,InspectorName("Физические объекты")] private PropAsset[] props=Array.Empty<PropAsset>();
        public void SetAssets(ActorTypeAsset[] actorAssets,PropAsset[] propAssets)
        {actors=(ActorTypeAsset[])actorAssets.Clone();props=(PropAsset[])propAssets.Clone();}
        public void AddActor(ActorTypeAsset asset)
        {
            if(asset==null) throw new InvalidOperationException("Не задан тип персонажа для каталога.");
            string id=asset.CreateDefinition().id;
            foreach(var current in actors)
            {
                if(current==null) throw new InvalidOperationException("Каталог "+name+": пустая ссылка типа персонажа.");
                if(current.CreateDefinition().id!=id) continue;
                if(current!=asset) throw new InvalidOperationException("Тип персонажа уже зарегистрирован другим ассетом: "+id);
                return;
            }
            Array.Resize(ref actors,actors.Length+1);actors[actors.Length-1]=asset;
        }
        public void AddProp(PropAsset asset)
        {
            if(asset==null || asset.Prefab==null) throw new InvalidOperationException("Не готов ассет физического объекта для каталога.");
            string id=asset.CreateDefinition().id;
            foreach(var current in props)
            {
                if(current==null) throw new InvalidOperationException("Каталог "+name+": пустая ссылка физического объекта.");
                if(current.CreateDefinition().id!=id) continue;
                if(current!=asset) throw new InvalidOperationException("Тип уже зарегистрирован другим ассетом: "+id);
                return;
            }
            Array.Resize(ref props,props.Length+1);props[props.Length-1]=asset;
        }
        public ActorTypeDefinition[] CreateActors()
        {
            var result=new ActorTypeDefinition[actors.Length];
            for(int i=0;i<result.Length;i++) result[i]=actors[i]!=null?actors[i].CreateDefinition():throw new InvalidOperationException($"Каталог {name}: отсутствует персонаж в позиции {i}.");
            return result;
        }
        public PropDefinition[] CreateProps()
        {
            var result=new PropDefinition[props.Length];
            for(int i=0;i<result.Length;i++) result[i]=props[i]!=null?props[i].CreateDefinition():throw new InvalidOperationException($"Каталог {name}: отсутствует объект в позиции {i}.");
            return result;
        }
        public string ActorsJson()=>StateJson.ToJson(new ActorTypeList {actors=CreateActors()});
        public bool TryGetActorPrefab(ActorTypeDefinition definition,out Remains.Presentation.ActorVisual prefab)
        {
            foreach(var asset in actors)
            {
                if(asset==null) throw new InvalidOperationException($"Каталог {name}: отсутствует ссылка на тип персонажа.");
                if(asset.TryGetPrefab(definition,out prefab)) return true;
            }
            prefab=null;return false;
        }
        public string PropsJson()=>StateJson.ToJson(new PropDefinitionList {props=CreateProps()});
        public bool TryGetPropTexture(string id,string resource,out Texture2D texture)
        {
            bool found=TryGetPropPrefab(id,resource,out var prefab);texture=found?prefab.Artwork.sprite.texture:null;return found;
        }
        public bool TryGetPropPrefab(string id,string resource,out Remains.Presentation.PropView prefab)
        {
            foreach(var asset in props)
            {
                if(asset==null) throw new InvalidOperationException($"Каталог {name}: отсутствует ссылка на физический объект.");
                if(!asset.MatchesArtwork(id,resource)) continue;
                prefab=asset.Prefab;
                if(prefab==null || prefab.Artwork==null || prefab.Artwork.sprite==null) throw new InvalidOperationException($"Ассет {asset.name}: отсутствует префаб или основной спрайт объекта {id}.");
                return true;
            }
            prefab=null;return false;
        }
        public static EntityAssetCatalog Load()=>GameAssets.Load<EntityAssetCatalog>("Content/EntityCatalog")??
            throw new InvalidOperationException("Нет каталога Content/EntityCatalog.");
    }
}
