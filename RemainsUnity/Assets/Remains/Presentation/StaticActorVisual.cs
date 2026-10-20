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
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer view;
        [SerializeField,InspectorName("ID графики")] private string authoredArtwork;
        [SerializeField,InspectorName("ID ресурса")] private string authoredResource;
        public void Configure(SpriteRenderer renderer,string key,string resource) {view=renderer;authoredArtwork=key;authoredResource=resource;}
        private string artwork;
        private HeldPropGlow heldGlow;
        private readonly HeldActorAppearance heldAppearance=new();
        public bool HeldGlowVisible=>heldGlow!=null && heldGlow.Visible;
        public override void ApplyHeldAppearance(HeldPropColorSource colors)
        {
            heldAppearance.Apply(state,colors,this);
            if(!heldAppearance.Active){heldGlow?.Draw(false);return;}
            if(heldGlow==null)
            {
                var child=new GameObject("Свечение удерживаемого тела");child.transform.SetParent(view.transform,false);
                heldGlow=child.AddComponent<HeldPropGlow>();heldGlow.Initialize(view,heldAppearance.Color,.08f);
            }
            var color=heldAppearance.Color;color.a=.7f;heldGlow.SetColor(color);heldGlow.SetFrame(view.sprite);heldGlow.Draw(view.enabled);
        }
        public string Resource {get;private set;}
        public override string ActorId=>state?.id;
        public override void AppendLightOccluders(List<RoomRadianceField.LightOccluder> output)=>AppendSpriteOccluder(view,output);
        public override void Bind(ActorState actor)
        {
            state=actor;string key=ActorTypes.Get(actor.actorType).artwork;
            if(!string.IsNullOrEmpty(authoredArtwork))
            {
                if(authoredArtwork!=key || view==null || view.sprite==null)
                    throw new InvalidOperationException($"Префаб {name}: отсутствует спрайт или не совпадает графика {key}.");
                Resource=authoredResource;Draw();return;
            }
            if(view==null) {view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=WorldVisualOrderPolicy.ActorPartOrder;}
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
        private void Update() {if(state==null) return;using(Remains.Diagnostics.FrameHitchLog.Section("StaticActorVisual.Update")) Draw();}
        private void Draw() {transform.position=state.Position;view.flipX=state.facing<0;}
    }
}
