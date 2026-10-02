using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomLightAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("ID источника")] private string id;
        [SerializeField,InspectorName("Яркость")] private float intensity=1;
        [SerializeField,InspectorName("Использовать исходную маску")] private bool useMask;
        [SerializeField,InspectorName("Маска света")] private Texture2D mask;
        [SerializeField,InspectorName("Смещение маски")] private Vector2 maskOffset;
        [SerializeField,InspectorName("Размер маски")] private Vector2 maskSize;
        [SerializeField,InspectorName("Радиус")] private float radius=5;
        [SerializeField,InspectorName("Цвет")] private Color color=Color.white;
        [SerializeField,HideInInspector] private string fixture,maskId;
        [SerializeField,HideInInspector] private float width,height;
        public void Configure(RoomLight light,Texture2D texture)
        {
            id=light.id;intensity=light.intensity;radius=light.radius;color=light.color;fixture=light.fixture;width=light.width;height=light.height;
            maskId=light.mask;useMask=!string.IsNullOrEmpty(light.mask);mask=texture;
            maskOffset=new Vector2(light.maskX-light.x,light.maskY-light.y);maskSize=new Vector2(light.maskWidth,light.maskHeight);
            transform.localPosition=new Vector3(light.x,light.y,0);
        }
        public RoomLight Read(RoomAuthoring room)
        {
            Validate();
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(Mathf.Abs(rect.width-1)>.00001f || Mathf.Abs(rect.height-1)>.00001f) throw new InvalidOperationException($"Свет {name}: размер задаётся радиусом/маской, масштаб Transform должен быть 1.");
            if(useMask && mask==null) throw new InvalidOperationException($"Свет {id}: отсутствует ссылка на маску.");
            return new RoomLight {id=id,x=rect.x,y=rect.y,intensity=intensity,radius=radius,color=color,fixture=fixture,width=width,height=height,
                mask=useMask?maskId:null,maskTexture=useMask?mask:null,maskX=rect.x+maskOffset.x,maskY=rect.y+maskOffset.y,maskWidth=maskSize.x,maskHeight=maskSize.y};
        }
        private void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || !float.IsFinite(intensity) || intensity<0 || !float.IsFinite(radius) || radius<=0 ||
                useMask && (mask==null || !float.IsFinite(maskOffset.x) || !float.IsFinite(maskOffset.y) || !float.IsFinite(maskSize.x) || !float.IsFinite(maskSize.y) || maskSize.x<=0 || maskSize.y<=0))
                throw new InvalidOperationException($"Свет {name}, ID={id}: проверьте яркость, радиус и ссылку/размеры маски.");
        }
        private void OnValidate()
        {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
