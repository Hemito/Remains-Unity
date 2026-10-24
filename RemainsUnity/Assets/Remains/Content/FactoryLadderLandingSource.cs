using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // Explicit reviewed content, not a rule for inferring landings on arbitrary ladders.
    [Serializable]
    public sealed class FactoryLadderLandingSource
    {
        public const string ResourceKey="Content/factory-ladder-landings";
        public int schema;
        public FactoryLadderLandingRecord[] records;
        public FactoryLadderLandingRecord[] Room(int sourceRoom)=>records.Where(r=>r.sourceRoom==sourceRoom).ToArray();
        public static FactoryLadderLandingSource Load()
        {
            var asset=GameAssets.Load<TextAsset>(ResourceKey);
            if(asset==null) throw new InvalidOperationException("Нет явных определений верхних посадок: "+ResourceKey);
            var result=JsonUtility.FromJson<FactoryLadderLandingSource>(asset.text);
            if(result==null) throw new InvalidOperationException("Верхние посадки: пустой JSON источника "+ResourceKey);
            result.Validate();return result;
        }
        public void Validate()
        {
            if(schema!=1 || records==null || records.Length==0)
                throw new InvalidOperationException("Верхние посадки: неподдерживаемая версия или пустые определения, schema="+schema);
            foreach(var record in records) record?.Validate();
            if(records.Any(r=>r==null) || records.Select(r=>r.id).Distinct(StringComparer.Ordinal).Count()!=records.Length)
                throw new InvalidOperationException("Верхние посадки: пустая запись или повтор stable ID.");
        }
    }
    [Serializable]
    public sealed class FactoryLadderLandingRecord
    {
        public string id,expectedToken,rungTexture;
        public int sourceRoom,column,row;
        public float ladderX,ladderTop;
        public RoomSurface surface;
        public RoomArtwork artwork;
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(expectedToken) || string.IsNullOrWhiteSpace(rungTexture) ||
                sourceRoom<0 || column<0 || row<=0 || surface==null || artwork==null || !float.IsFinite(ladderX) || !float.IsFinite(ladderTop))
                throw new InvalidOperationException("Верхняя посадка "+id+": неполные данные исходной клетки/лестницы.");
            float[] values={surface.x,surface.y,surface.width,surface.height,artwork.x,artwork.y,artwork.width,artwork.height};
            if(values.Any(v=>!float.IsFinite(v)) || surface.width<=0 || surface.height<=0 || artwork.width<=0 || artwork.height<=0 ||
                !surface.oneWay || !surface.shotsPassThrough || surface.slopeDirection!=0 || string.IsNullOrWhiteSpace(surface.movementSound) ||
                string.IsNullOrWhiteSpace(surface.texture) || surface.texture!=artwork.texture || artwork.repeat || artwork.slopeDirection!=0 ||
                Mathf.Abs(surface.y+surface.height-ladderTop)>.0001f || ladderX<surface.x || ladderX>surface.x+surface.width)
                throw new InvalidOperationException("Верхняя посадка "+id+": опора, верх лестницы или отдельные границы графики некорректны.");
        }
    }
}
