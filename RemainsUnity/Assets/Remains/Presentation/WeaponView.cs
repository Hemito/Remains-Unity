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
        private MaterialPropertyBlock properties;
        public string Id=>id;
        public SpriteRenderer Artwork=>artwork;
        public Transform Muzzle=>muzzle;
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
            if(artwork==null || artwork.sprite==null || artwork.transform!=transform || transform.localScale!=Vector3.one)
                throw new InvalidOperationException($"Оружие {id}: нужен основной спрайт на корне с единичным масштабом.");
            var sprite=artwork.sprite;var rect=sprite.rect;var pivot=sprite.pivot;
            if(!float.IsFinite(sprite.pixelsPerUnit) || sprite.pixelsPerUnit<=0)
                throw new InvalidOperationException($"Оружие {id}: некорректная плотность пикселей спрайта {sprite.name}.");
            var barrel=muzzle==null?Vector3.zero:transform.InverseTransformPoint(muzzle.position);
            if(muzzle!=null && (!float.IsFinite(barrel.x) || !float.IsFinite(barrel.y) || barrel.x<=0 || Mathf.Abs(barrel.z)>.00001f))
                throw new InvalidOperationException($"Оружие {id}: дуло должно иметь конечные координаты, X>0 и Z=0 относительно корня.");
            return new WeaponRig {id=id,x=(int)rect.x,y=(int)rect.y,width=(int)rect.width,height=(int)rect.height,
                pixelsPerUnit=sprite.pixelsPerUnit,
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
