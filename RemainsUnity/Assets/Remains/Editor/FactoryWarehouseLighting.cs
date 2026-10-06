using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    internal static class FactoryWarehouseLighting
    {
        internal static RoomAuthoring Template=>FactoryWarehouseSource.Asset<GameObject>(FactoryLadderLandingAuthoring.EntryPath).GetComponent<RoomAuthoring>();
        internal static RoomLightProfile TemplateProfile(RoomAuthoring room)=>room.GetComponent<RoomLightingAuthoring>()?.CreateProfile(room)??
            throw new InvalidOperationException("Склад40: вход46 не имеет явного authored lighting profile.");
        internal static RoomLight Expected(RoomAuthoring template,FactoryWarehouseSource.SourceRecord record)
        {
            string kind=FactoryWarehouseSource.Value(record,"id");
            FactoryWarehouseSource.Require(kind is "light2" or "light4","Не назначен explicit fixture kind="+kind+", record="+record.recordId);
            var fixture=template.CreateLayout().artwork.Where(a=>a.texture=="Factory46/decor-"+kind+"-1").OrderBy(a=>a.x).ThenBy(a=>a.y).FirstOrDefault();
            FactoryWarehouseSource.Require(fixture!=null,"Нет явно выбранного leftmost вход46 fixture, kind="+kind);
            string mask="Original/Room/Factory46/light-mask-"+kind+"-1";
            var candidates=TemplateProfile(template).lights.Where(l=>l.mask==mask && Mathf.Abs(l.x-fixture.x-fixture.width*.5f)<.001f && Mathf.Abs(l.y-fixture.y-fixture.height*.5f)<.001f).ToArray();
            FactoryWarehouseSource.Require(candidates.Length==1,"Не найден единственный authored lamp/mask у template fixture, kind="+kind);
            var anchor=new Vector2(FactoryWarehouseSource.Number(record,"x")*.8f,(25-FactoryWarehouseSource.Number(record,"y"))*.8f);
            // Existing entry46 calibrated masks were authored relative to source left/top,
            // not the padded canvas corner of our newly exported diffuse light2.
            return FactoryModuleLightTemplate.Translate(candidates[0],new Vector2(fixture.x,fixture.y+fixture.height),anchor,record.recordId);
        }
        internal static void Install(GameObject root,FactoryWarehouseSource.Data data,FactoryModuleAuthoringInput input)
        {
            var template=Template;var profile=TemplateProfile(template);var room=root.GetComponent<RoomAuthoring>();
            var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(profile.ambient);lighting.SetBackgroundAmbientScale(profile.backgroundAmbientScale);
            var records=data.source.room.records.Where(r=>r.tag=="back" && FactoryWarehouseSource.Value(r,"id").StartsWith("light",StringComparison.Ordinal)).ToArray();
            FactoryWarehouseSource.Require(records.Length==4,"Изменены четыре исходных светильника.");
            var artwork=root.GetComponentsInChildren<RoomArtworkAuthoring>();
            var parent=root.GetComponentInChildren<RoomMeshLifetime>().transform;
            foreach(var record in records)
            {
                string kind=FactoryWarehouseSource.Value(record,"id"),key="Factory40/decor-"+kind+"-1";
                var resource=input.resources.Single(r=>r.textureKey==key);var canvas=resource.sourceBoundsPixelsYDown;
                var anchor=new Vector2(FactoryWarehouseSource.Number(record,"x")*.8f,(25-FactoryWarehouseSource.Number(record,"y"))*.8f);
                float x=anchor.x+canvas[0]/resource.sourcePixelsPerMetre,y=anchor.y-canvas[3]/resource.sourcePixelsPerMetre;
                var actual=artwork.Single(a=>{var d=a.Read(room);return d.texture==key && Mathf.Abs(d.x-x)<.001f && Mathf.Abs(d.y-y)<.001f;});
                var lamp=Expected(template,record);var texture=Resources.Load<Texture2D>(lamp.mask);
                FactoryWarehouseSource.Require(texture!=null && texture.isReadable,"Нет читаемой mask="+lamp.mask+", record="+record.recordId);
                var node=new GameObject("Светильник · "+record.recordId);node.transform.SetParent(parent,false);
                node.AddComponent<RoomLightAuthoring>().Configure(lamp,texture);actual.transform.SetParent(node.transform,true);
            }
            foreach(var art in artwork.Where(a=>a.Read(room).order<-10))
            {
                var renderer=art.GetComponent<MeshRenderer>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetFloat("_BackgroundLighting",1);renderer.SetPropertyBlock(block);
            }
            Debug.LogWarning("Склад40: четыре source fixture/mask registration сохранены; ambient/radius/intensity взяты из явного профиля входа46 Unity. Source green filter и original photometric equivalence не проверены.",root);
        }
    }
}
