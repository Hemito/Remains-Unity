using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class WeaponView : MonoBehaviour
    {
        [SerializeField,InspectorName("ID графики оружия")] private string id;
        [SerializeField,InspectorName("Основной спрайт")] private SpriteRenderer artwork;
        [SerializeField,InspectorName("Дуло (для стрелкового оружия)")] private Transform muzzle;
        [SerializeField,InspectorName("Кадр покоя и исходная регистрация")] private CatalogSpriteReference restReference;
        private MaterialPropertyBlock properties;
        public string Id=>id;
        public SpriteRenderer Artwork=>artwork;
        public Transform Muzzle=>muzzle;
        // Source geometry belongs to authoring, independently of the screen-dependent drawing resolution.
        public CatalogSpriteReference RestReference=>restReference!=null && restReference.IsConfigured?restReference:
            artwork!=null && artwork.sprite!=null?CatalogSpriteReference.FromSprite(artwork.sprite):null;
        public Sprite ResolveRestSprite()=>RestReference?.Resolve()??throw new InvalidOperationException("Нет кадра покоя оружия: "+id);
        private void Awake()
        {
            // AddComponent invokes Awake before authoring Configure; only serialized instances need resolving here.
            if(restReference!=null && restReference.IsConfigured) SetFrame(restReference.Resolve());
        }
        public void ConfigureRest(CatalogSpriteReference value)
        {
            value.Validate("Оружие "+id);restReference=value;
            if(!string.IsNullOrEmpty(value.Key)) artwork.sprite=null;
        }
        public void SetVisible(bool visible)
        {
            if(gameObject.activeSelf!=visible) gameObject.SetActive(visible);
        }
        public void SetMirrored(bool mirrored)
        {
            transform.localScale=new Vector3(1,mirrored?-1:1,1);
            artwork.flipY=false;
        }
        public void Configure(string value,SpriteRenderer renderer,Transform barrel) {id=value;artwork=renderer;muzzle=barrel;}
        public WeaponRig ReadRig()
        {
            if(artwork==null || artwork.transform!=transform || transform.localScale!=Vector3.one)
                throw new InvalidOperationException($"Оружие {id}: нужен основной спрайт на корне с единичным масштабом.");
            var source=RestReference;
            if(source==null) throw new InvalidOperationException("Нет исходной регистрации оружия: "+id);
            source.Validate("Оружие "+id);var rect=source.Rect;var pivot=source.Pivot;
            var barrel=muzzle==null?Vector3.zero:transform.InverseTransformPoint(muzzle.position);
            if(muzzle!=null && (!float.IsFinite(barrel.x) || !float.IsFinite(barrel.y) || barrel.x<=0 || Mathf.Abs(barrel.z)>.00001f))
                throw new InvalidOperationException($"Оружие {id}: дуло должно иметь конечные координаты, X>0 и Z=0 относительно корня.");
            return new WeaponRig {id=id,x=(int)rect.x,y=(int)rect.y,width=(int)rect.width,height=(int)rect.height,
                pixelsPerUnit=source.PixelsPerUnit,
                pivotX=pivot.x/rect.width,pivotY=pivot.y/rect.height,muzzleX=barrel.x,muzzleY=barrel.y};
        }
        public void SetFrame(Sprite sprite)
        {
            if(artwork.sprite==sprite) return;
            properties??=new MaterialPropertyBlock();artwork.GetPropertyBlock(properties);
            properties.SetTexture("_MainTex",sprite.texture);artwork.sprite=sprite;artwork.SetPropertyBlock(properties);
        }
    }
}
