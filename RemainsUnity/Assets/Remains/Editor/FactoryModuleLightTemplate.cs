using System;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    // One translation of our authored point/mask. No original lighting algorithm.
    public static class FactoryModuleLightTemplate
    {
        public static RoomLight Translate(RoomLight source,Vector2 oldSourceAnchor,Vector2 newSourceAnchor,string id)
        {
            if(source==null || string.IsNullOrWhiteSpace(id) || !float.IsFinite(oldSourceAnchor.x+oldSourceAnchor.y+newSourceAnchor.x+newSourceAnchor.y))
                throw new InvalidOperationException("Световой шаблон: отсутствует источник, ID или конечная регистрация, id="+id);
            var shift=newSourceAnchor-oldSourceAnchor;
            return new RoomLight {id=id,fixture=source.fixture,shadow=source.shadow,backgroundIntensity=source.backgroundIntensity,
                x=source.x+shift.x,y=source.y+shift.y,radius=source.radius,intensity=source.intensity,color=source.color,width=source.width,height=source.height,
                mask=source.mask,maskTexture=source.maskTexture,maskX=source.maskX+shift.x,maskY=source.maskY+shift.y,maskWidth=source.maskWidth,maskHeight=source.maskHeight};
        }
    }
}
