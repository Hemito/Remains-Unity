using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    // Only reviewed ordinary physical types, with no extra source behavior or inventory.
    public sealed class FactoryPropAdapter : MonoBehaviour
    {
        public const string FirstPatch="base:factory-generated:physical-v1";
        [SerializeField,InspectorName("Поддержанные физические объекты")] private PropAsset[] definitions=Array.Empty<PropAsset>();
        // Types added after the first generated layout: a world saved before receives only their objects (additive update,
        // finished by FactoryContainerAdapter, which writes the level's physical version).
        [SerializeField,InspectorName("Добавлены после первой версии (physical-v3)")] private PropAsset[] laterDefinitions=Array.Empty<PropAsset>();
        private bool installed;
        public int Installed {get;private set;}
        public IReadOnlyList<string> LaterAdditions {get;private set;}=Array.Empty<string>();
        public void Configure(PropAsset[] assets,PropAsset[] later)
        {definitions=(PropAsset[])assets.Clone();laterDefinitions=(PropAsset[])later.Clone();}
        public void Install(FactoryLevelRoot root)
        {
            if(installed || root==null || root.Prepared==null || root.transform!=transform || root.Rooms.childCount!=root.Prepared.Grid.Rooms.Count)
                throw new InvalidOperationException("Адаптер физических объектов: нужен подготовленный корень; повторная установка запрещена.");
            var types=new Dictionary<string,PropAsset>(StringComparer.Ordinal);
            foreach(var asset in definitions.Concat(laterDefinitions))
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
            var created=new List<GameObject>();var laterIds=new List<string>();
            try
            {
                foreach(var entry in accepted)
                {
                    var node=new GameObject("Физический объект: "+entry.source.Id);created.Add(node);
                    node.transform.SetParent(transform,false);node.transform.localPosition=entry.source.Position;
                    string localId=$"{entry.source.Room.Cell.X},{entry.source.Room.Cell.Y}:{entry.source.Source.Id}";
                    node.AddComponent<RoomPropPlacement>().Configure(localId,entry.asset);
                    if(Array.IndexOf(laterDefinitions,entry.asset)>=0) laterIds.Add(localId);
                }
                gameObject.AddComponent<RoomPhysicalContent>().Configure(FirstPatch);
                Installed=accepted.Count;LaterAdditions=laterIds.ToArray();installed=true;
            }
            catch
            {
                foreach(var node in created) {if(Application.isPlaying) Destroy(node);else DestroyImmediate(node);}
                throw;
            }
            foreach(var entry in accepted) root.HideObjectArtwork(entry.source);
            var radioactive=accepted.Select(entry=>entry.asset.CreateDefinition()).Where(data=>data.radioactivity>0)
                .GroupBy(data=>(data.id,data.radioactivity)).Select(group=>$"{group.Key.id} ×{group.Count()} (rad={group.Key.radioactivity})").ToArray();
            if(radioactive.Length>0)
                Debug.LogWarning($"Завод {root.Prepared.Level}: радиоактивные объекты поставлены как обычные физические тела — радиация ещё не реализована (TD-37), игрок рядом с ними не облучается: {string.Join("; ",radioactive)}.",this);
            if(skipped.Count>0) Debug.LogWarning($"Завод {root.Prepared.Level}: параметры, содержимое или связи физических объектов ещё не поддержаны; пропущено {skipped.Count}. ID: {string.Join("; ",skipped)}",this);
        }
    }
}
