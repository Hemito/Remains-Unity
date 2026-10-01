using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    [Serializable] public sealed class StaticActorArtwork
    {public string id,resource;public int width,height;public float pixelsPerUnit,pivotX,pivotY;}
    public sealed class StaticActorVisual : ActorVisual
    {
        private static readonly Dictionary<string,(Sprite sprite,string resource)> cache=new();
        private ActorState state;
        private SpriteRenderer view;
        private string artwork;
        public string Resource {get;private set;}
        public override string ActorId=>state?.id;
        public override void Bind(ActorState actor)
        {
            state=actor;string key=ActorTypes.Get(actor.actorType).artwork;
            if(view==null) {view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=20;}
            if(artwork!=key)
            {
                if(string.IsNullOrEmpty(key)) throw new InvalidOperationException($"Не задана графика статического актёра: {actor.id}, type={actor.actorType}.");
                if(!cache.TryGetValue(key,out var entry))
                {
                    var asset=Resources.Load<TextAsset>(key);
                    if(asset==null) throw new InvalidOperationException("Нет описания графики актёра: "+key);
                    var data=JsonUtility.FromJson<StaticActorArtwork>(asset.text);
                    var texture=Resources.Load<Texture2D>(data.resource);
                    if(texture==null || texture.width!=data.width || texture.height!=data.height || data.pixelsPerUnit<=0)
                        throw new InvalidOperationException($"Некорректная графика актёра: {actor.id}, resource={data.resource}.");
                    entry=(Sprite.Create(texture,new Rect(0,0,data.width,data.height),new Vector2(data.pivotX,data.pivotY),data.pixelsPerUnit),data.resource);
                    SpriteArtworkMaterial.Prepare(entry.sprite);
                    cache.Add(key,entry);
                }
                SpriteArtworkMaterial.Apply(view,entry.sprite);Resource=entry.resource;artwork=key;
            }
            Draw();
        }
        private void Update() {if(state!=null) Draw();}
        private void Draw() {transform.position=state.Position;view.flipX=state.facing<0;}
    }
}
