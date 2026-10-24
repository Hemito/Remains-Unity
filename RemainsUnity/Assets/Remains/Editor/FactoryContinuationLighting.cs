using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryContinuationLighting
    {
        public static RoomAuthoring Template=>FactoryContinuationSource.Asset<GameObject>(FactoryLadderLandingAuthoring.EntryPath).GetComponent<RoomAuthoring>();
        public static RoomLightProfile TemplateProfile=>Template.GetComponent<RoomLightingAuthoring>()?.CreateProfile(Template)??
            throw new InvalidOperationException("Продолжение27: нет explicit authored lighting входа46.");
        public static RoomLight Expected(FactoryContinuationSource.SourceRecord record)
        {
            string kind=FactoryContinuationSource.Value(record,"id");FactoryContinuationSource.Require(kind is "light2" or "light4","Не назначен fixture type="+kind+", record="+record.recordId);
            var anchor=new Vector2(FactoryContinuationSource.Number(record,"x")*.8f,(25-FactoryContinuationSource.Number(record,"y"))*.8f);
            return FactoryModuleLightTemplate.FromAuthoredFixture(Template,kind,anchor,record.recordId);
        }
        public static void Install(GameObject root,FactoryContinuationSource.Data data)
        {
            var room=root.GetComponent<RoomAuthoring>();var profile=TemplateProfile;
            var records=data.source.room.records.Where(r=>r.tag=="back" && FactoryContinuationSource.Value(r,"id") is "light2" or "light4").ToArray();
            FactoryContinuationSource.Require(records.Length==5 && records.Count(r=>FactoryContinuationSource.Value(r,"id")=="light2")==3,"Изменён source набор3light2/2light4.");
            var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(profile.ambient);lighting.SetBackgroundAmbientScale(profile.backgroundAmbientScale);
            foreach(var record in records)
            {
                var placement=data.manifest.decorPlacements.Single(p=>p.recordId==record.recordId);
                var artwork=FactoryContinuationArtwork.Find(room,data.decor.artwork[placement.artworkIndex]);
                var lamp=Expected(record);var texture=GameAssets.Load<Texture2D>(lamp.mask);
                FactoryContinuationSource.Require(texture!=null && texture.isReadable,"Нет читаемой shared light mask="+lamp.mask+", record="+record.recordId);
                var node=new GameObject("Светильник · "+record.recordId);node.transform.SetParent(artwork.transform.parent,false);
                node.AddComponent<RoomLightAuthoring>().Configure(lamp,texture);artwork.transform.SetParent(node.transform,true);
            }
            Debug.LogWarning("Продолжение27: пять source fixture/mask anchors сохранены; ambient/radius/intensity — явный Unity профиль входа46. Исходный red filter/photometry original runtime не подтверждены.",root);
        }
    }
}
