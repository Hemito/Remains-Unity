using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class PonyPaletteEntry
    {
        [InspectorName("Постоянный ID")] public int id;
        [InspectorName("Название")] public string title;
        [InspectorName("Цвет гривы и хвоста")] public Color hair;
    }
    [CreateAssetMenu(menuName="Remains/Палитра персонажей",fileName="PonyPalette")]
    public sealed class PonyPaletteAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Цвета (ID не менять после выпуска)")] private PonyPaletteEntry[] colors=Array.Empty<PonyPaletteEntry>();
        private readonly Dictionary<int,Color> lookup=new();
        private readonly HashSet<int> warned=new();
        private static PonyPaletteAsset loaded;
        public static PonyPaletteAsset Load()
        {
            if(loaded!=null) return loaded;
            var asset=Resources.Load<PonyPaletteAsset>("Content/pony-palette");
            if(asset==null) throw new InvalidOperationException("Нет палитры Content/pony-palette.");
            asset.Validate();loaded=asset;return loaded;
        }
        public void Validate()
        {
            lookup.Clear();
            if(colors==null || colors.Length<2) throw new InvalidOperationException($"Палитра {name}: нужны цвета 0 и 1 для существующих персонажей.");
            foreach(var entry in colors)
            {
                if(entry==null || entry.id<0 || string.IsNullOrWhiteSpace(entry.title) || !Finite(entry.hair) || !lookup.TryAdd(entry.id,entry.hair))
                    throw new InvalidOperationException($"Палитра {name}: неверная запись или повтор ID {entry?.id}.");
            }
            if(!lookup.ContainsKey(0) || !lookup.ContainsKey(1)) throw new InvalidOperationException($"Палитра {name}: отсутствуют исходные ID 0/1.");
        }
        private static bool Finite(Color color)=>float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b) && color.r>=0 && color.r<=1 && color.g>=0 && color.g<=1 && color.b>=0 && color.b<=1 && color.a==1;
        public Color Hair(int id)
        {
            if(lookup.TryGetValue(id,out var color)) return color;
            if(warned.Add(id)) Debug.LogWarning($"Палитра {name}: отсутствует цвет ID={id}. Для старого порядкового номера показан исходный цвет по чётности; ID в сохранении не изменён.",this);
            return lookup[id%2==0?0:1];
        }
        public int ForNewCharacter(IEnumerable<ActorState> actors)
        {
            var counts=actors.Where(actor=>actor.player).GroupBy(actor=>actor.hair).ToDictionary(group=>group.Key,group=>group.Count());
            return colors.OrderBy(entry=>counts.TryGetValue(entry.id,out int count)?count:0).ThenBy(entry=>entry.id).First().id;
        }
        private void OnValidate()
        {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
