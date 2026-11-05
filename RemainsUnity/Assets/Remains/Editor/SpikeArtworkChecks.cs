using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class SpikeArtworkChecks
    {
        public static void CheckCatalog(SpikeArtworkCatalog catalog)
        {
            catalog.Validate();
            Require(catalog.Definitions.Count==2,"Ожидались два определения шипов.");
            foreach(var definition in catalog.Definitions)
            {
                int count=definition.id=="spikes"?4:definition.id=="fspikes"?1:0;
                Require(count>0 && definition.rear.Length==count && definition.front.Length==count,"Неполные слои: "+definition.id);
                foreach(var frame in definition.rear.Concat(definition.front))
                {
                    var path=AssetDatabase.GetAssetPath(frame.sprite);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    var expected=ImportedArtPadding.Pivot(frame.sourceOrigin.x,frame.sourceOrigin.y,frame.zoom,
                        frame.sprite.texture.width,frame.sprite.texture.height);
                    Require(Vector2.Distance(importer.spritePivot,expected)<.00001f,"Изменён исходный якорь: "+path);
                    Require(frame.zoom==(count==4?1:8),"Изменён масштаб растра/вектора: "+path);
                }
            }
        }
        public static void Run()
        {
            var catalog=GameAssets.Load<SpikeArtworkCatalog>(SpikeArtworkCatalog.Resource);
            Require(catalog!=null,"Нет подготовленного каталога.");CheckCatalog(catalog);
            WeaponCatalog.Load();Anatomy.Load();
            // Actual seed104 placements, with the existing hurting boxes checked by SpikeChecks.
            SpikeChecks.Run();
            var world=NewGameWorld.Create(104);var root=FactoryLevelRoot.Create(world,"factory:0");
            try
            {
                if(!root.TryGetComponent<SortingGroup>(out var group)) group=root.gameObject.AddComponent<SortingGroup>();
                group.sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
                int count=FactorySpikeAdapter.Install(root);
                var visuals=root.GetComponentsInChildren<SpikeVisual>(true);
                Require(count>0 && visuals.Length==count,"Не каждый реальный шип получил два слоя.");
                foreach(var visual in visuals)
                {
                    visual.Validate();Require(visual.Rear.sprite!=null && visual.Front.sprite!=null,"Нет одного из рисунков: "+visual.StableId);
                    Require(visual.transform.localPosition==new Vector3(0,.02f,0),"Неверный исходный отступ в один пиксель.");
                    Require(visual.FrontGroup.sortingOrder>WorldVisualOrderPolicy.LastOrder(WorldVisualCategory.Player) &&
                        visual.FrontGroup.sortingOrder<WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Telekinesis),"Передний слой не между актёрами и телекинезом.");
                }
                var before=visuals.Select(v=>(v.Rear.sprite,v.Front.sprite)).ToArray();
                for(int i=0;i<visuals.Length;i++)
                {
                    string type=root.Objects.Single(p=>p.Id==visuals[i].StableId).Source.Type.Substring("remains:obj:".Length);
                    visuals[i].Initialize(catalog.Require(type),visuals[i].StableId);
                    Require(before[i]==(visuals[i].Rear.sprite,visuals[i].Front.sprite),"Кадр изменился при повторной привязке.");
                }
                root.gameObject.SetActive(false);
                Require(visuals.All(v=>!v.gameObject.activeInHierarchy),"Передний слой остался на неактивном уровне.");
                Debug.Log($"PT-20261103-03: PASS — 10кадров/2слоя, исходные масштаб/якорь, {count}шипов seed104, геометрия урона, порядок переднего слоя и неактивный уровень.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root.gameObject);FactoryContentSources.Clear();}
        }
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("Графика шипов: "+message);}
    }
}

