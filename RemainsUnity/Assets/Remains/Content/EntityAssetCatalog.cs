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
        public string PropsJson()=>StateJson.ToJson(new PropDefinitionList {props=CreateProps()});
        public bool TryGetPropTexture(string id,string resource,out Texture2D texture)
        {
            foreach(var asset in props)
            {
                if(asset==null) throw new InvalidOperationException($"Каталог {name}: отсутствует ссылка на физический объект.");
                if(!asset.MatchesArtwork(id,resource)) continue;
                texture=asset.Texture;
                if(texture==null) throw new InvalidOperationException($"Ассет {asset.name}: отсутствует текстура объекта {id}.");
                return true;
            }
            texture=null;return false;
        }
        public static EntityAssetCatalog Load()=>Resources.Load<EntityAssetCatalog>("Content/EntityCatalog")??
            throw new InvalidOperationException("Нет каталога Content/EntityCatalog.");
    }
}
