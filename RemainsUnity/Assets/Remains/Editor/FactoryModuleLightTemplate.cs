using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    // One translation of our authored point/mask. No original lighting algorithm.
    public static class FactoryModuleLightTemplate
    {
        public static RoomLight FromAuthoredFixture(RoomAuthoring template,string kind,Vector2 newSourceAnchor,string sourceRecord)
        {
            if(template==null || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(sourceRecord))
                throw new InvalidOperationException("Световой шаблон: нет комнаты/типа/исходной записи, record="+sourceRecord+", kind="+kind);
            var profile=template.GetComponent<RoomLightingAuthoring>()?.CreateProfile(template)??
                throw new InvalidOperationException("Световой шаблон: отсутствует authored lighting, room="+template.name);
            var fixture=template.CreateLayout().artwork.Where(a=>a.texture=="Factory46/decor-"+kind+"-1").OrderBy(a=>a.x).ThenBy(a=>a.y).FirstOrDefault()??
                throw new InvalidOperationException("Световой шаблон: нет явно выбранного leftmost fixture, kind="+kind+", record="+sourceRecord);
            string mask="Original/Room/Factory46/light-mask-"+kind+"-1";
            var candidates=profile.lights.Where(l=>l.mask==mask && Mathf.Abs(l.x-fixture.x-fixture.width*.5f)<.001f && Mathf.Abs(l.y-fixture.y-fixture.height*.5f)<.001f).ToArray();
            if(candidates.Length!=1)
                throw new InvalidOperationException("Световой шаблон: неоднозначная fixture/mask registration, kind="+kind+", record="+sourceRecord+", count="+candidates.Length);
            return Translate(candidates[0],new Vector2(fixture.x,fixture.y+fixture.height),newSourceAnchor,sourceRecord);
        }
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
