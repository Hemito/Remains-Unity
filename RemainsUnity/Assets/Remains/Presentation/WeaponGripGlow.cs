using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    // A small soft field at the authored grip, independent of sprite/shot/reload frames.
    // It is a child of the actual weapon, so there is no second pose calculation.
    public sealed class WeaponGripGlow : MonoBehaviour
    {
        private SpriteRenderer source,renderer;
        private Sprite quad;
        private Material material;
        public SpriteRenderer Renderer=>renderer;
        public static bool UsesLevitatedWeapon(Remains.Domain.ActorState actor)
        {
            var type=ActorTypes.Get(actor.actorType);
            return !type.atlasWeaponSocket && (type.presentation=="pony" ||
                type.raider?.attacker==Remains.Domain.RaiderAttacker.Levitated);
        }
        public void Initialize(SpriteRenderer artwork,Vector2 center,Vector2 size)
        {
            if(renderer!=null) throw new InvalidOperationException("Дымка рукоятки уже подготовлена.");
            if(artwork==null || !float.IsFinite(center.x) || !float.IsFinite(center.y) ||
                !float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x<=0 || size.y<=0)
                throw new ArgumentException("Некорректная область дымки рукоятки.");
            source=artwork;
            var shader=GameAssets.Load<Shader>("Presentation/WeaponGripGlow");
            if(shader==null) throw new InvalidOperationException("Нет шейдера дымки рукоятки.");
            var child=new GameObject("Дымка рукоятки");child.transform.SetParent(transform,false);
            child.transform.localPosition=center;child.transform.localScale=new Vector3(size.x,size.y,1);
            renderer=child.AddComponent<SpriteRenderer>();
            var texture=Texture2D.whiteTexture;
            quad=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),texture.width,0,SpriteMeshType.FullRect);
            material=new Material(shader){name="Дымка рукоятки "+name};
            renderer.sprite=quad;renderer.sharedMaterial=material;renderer.enabled=false;
        }
        public void SetColor(Color color) {color.a=.65f;renderer.color=color;}
        public void Draw(bool visible)
        {
            renderer.enabled=visible && source.enabled && !source.forceRenderingOff;
            renderer.sortingLayerID=source.sortingLayerID;renderer.sortingOrder=source.sortingOrder+1;
        }
        private void OnDestroy()
        {
            if(Application.isPlaying) {Destroy(quad);Destroy(material);}
            else {DestroyImmediate(quad);DestroyImmediate(material);}
        }
    }
}
