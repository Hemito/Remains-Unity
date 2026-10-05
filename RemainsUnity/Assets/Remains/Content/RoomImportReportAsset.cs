using System;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class RoomSourceRecord
    {
        [InspectorName("ID исходной записи")] public string recordId;
        [InspectorName("Исходный тип")] public string kind;
        [InspectorName("Исходный код объекта")] public string code;
        [InspectorName("Столбец исходной клетки")] public float sourceX;
        [InspectorName("Строка исходной клетки")] public float sourceY;
        [InspectorName("Исходные данные содержимого"),TextArea] public string sourceData;
        [InspectorName("Подключено")] public bool installed;
        [InspectorName("Решение импорта"),TextArea] public string reason;
    }
    [CreateAssetMenu(menuName="Remains/Отчёт импорта комнаты")]
    public sealed class RoomImportReportAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Исходный шаблон")] private string sourceId;
        [SerializeField,InspectorName("SHA256 исходного содержимого")] private string sourceHash;
        [SerializeField,InspectorName("Исходные игровые объекты")] private RoomSourceRecord[] records;
        public string SourceId=>sourceId;
        public RoomSourceRecord[] Records {get {Validate();return records;}}
        public void Configure(string id,string hash,RoomSourceRecord[] values) {sourceId=id;sourceHash=hash;records=values;Validate();}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(sourceId) || sourceHash?.Length!=64 || records==null ||
                records.Any(r=>r==null || string.IsNullOrWhiteSpace(r.recordId) || string.IsNullOrWhiteSpace(r.kind) ||
                    string.IsNullOrWhiteSpace(r.code) || string.IsNullOrWhiteSpace(r.reason)) || records.Select(r=>r.recordId).Distinct().Count()!=records.Length ||
                records.Select(r=>r.code).Distinct().Count()!=records.Length)
                throw new InvalidOperationException("Неполный или неоднозначный отчёт импорта комнаты: "+name);
        }
        public void WarnIncomplete(UnityEngine.Object context)
        {
            Validate();var pending=records.Where(r=>!r.installed).ToArray();
            if(pending.Length>0) Debug.LogWarning($"Неполный импорт комнаты {sourceId}, context={context.name}: не подключено {pending.Length} объектов; "+
                string.Join("; ",pending.Select(r=>$"{r.recordId}, type={r.kind}, code={r.code}: {r.reason}")),context);
        }
    }
}
