using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    // Only reviewed ordinary physical types, with no extra source behavior or inventory.
    public sealed class FactoryPropAdapter : MonoBehaviour
    {
        [SerializeField,InspectorName("Поддержанные физические объекты")] private PropAsset[] definitions=Array.Empty<PropAsset>();
        private bool installed;
        public int Installed {get;private set;}
        public void Configure(PropAsset[] assets)=>definitions=(PropAsset[])assets.Clone();
        public void Install(FactoryLevelRoot root)
        {
            if(installed || root==null || root.Prepared==null || root.transform!=transform || root.Rooms.childCount!=root.Prepared.Grid.Rooms.Count)
                throw new InvalidOperationException("Адаптер физических объектов: нужен подготовленный корень; повторная установка запрещена.");
            var types=new Dictionary<string,PropAsset>(StringComparer.Ordinal);
            foreach(var asset in definitions)
            {
                if(asset==null || asset.Prefab==null) throw new InvalidOperationException("Адаптер физических объектов: нет определения или его префаба.");
                var data=asset.CreateDefinition();PropCatalog.Validate(data);
                if(!types.TryAdd("remains:obj:"+data.id,asset)) throw new InvalidOperationException("Повтор определения физического объекта: "+data.id);
            }
            if(types.Count==0 || GetComponent<RoomPhysicalContent>()!=null)
                throw new InvalidOperationException("Адаптер физических объектов: пустой каталог или повтор расстановки.");
            var accepted=new List<(FactoryPlacedObject source,PropAsset asset)>();
            var skipped=new List<string>();
            foreach(var placement in root.Objects)
            {
                if(!types.TryGetValue(placement.Source.Type,out var asset)) continue;
                var obj=placement.Source;
                if(obj.Params.Count!=0 || obj.Items.Count!=0 || obj.Links.Count!=0 || obj.Extra!=null)
                {skipped.Add($"{placement.Id} ({obj.Type}, {placement.Room.Template.Template}, code={obj.Code})");continue;}
                accepted.Add((placement,asset));
            }
            var created=new List<GameObject>();
            try
            {
                foreach(var entry in accepted)
                {
                    var node=new GameObject("Физический объект: "+entry.source.Id);created.Add(node);
                    node.transform.SetParent(transform,false);node.transform.localPosition=entry.source.Position;
                    node.AddComponent<RoomPropPlacement>().Configure($"{entry.source.Room.Cell.X},{entry.source.Room.Cell.Y}:{entry.source.Source.Id}",entry.asset);
                }
                gameObject.AddComponent<RoomPhysicalContent>().Configure("base:factory-generated:physical-v1");
                Installed=accepted.Count;installed=true;
            }
            catch
            {
                foreach(var node in created) {if(Application.isPlaying) Destroy(node);else DestroyImmediate(node);}
                throw;
            }
            foreach(var entry in accepted) root.HideObjectArtwork(entry.source);
            if(skipped.Count>0) Debug.LogWarning($"Завод {root.Prepared.Level}: параметры, содержимое или связи физических объектов ещё не поддержаны; пропущено {skipped.Count}. ID: {string.Join("; ",skipped)}",this);
        }
    }
}
