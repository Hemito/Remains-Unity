using System;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // Root of a generated room prefab (one per template and mirror; Editor RoomPrefabGenerator). Holds the
    // authored content of the room; walls, floors, ladders and light occluders come from the level grid
    // (FactoryGridBuilder, decision 2026-10-10). The template asset stays the source of truth: object
    // parameters, items and links are read from it by index, not copied. No factory index, layout cell or
    // world ID is stored here: they belong to the placement of the room in a level.
    public sealed class RoomTemplateInstance : MonoBehaviour
    {
        [SerializeField,InspectorName("Шаблон")] private RoomTemplateAsset template;
        [SerializeField,InspectorName("ID шаблона")] private string templateId;
        [SerializeField,InspectorName("Отпечаток шаблона (SHA256 записи)")] private string fingerprint;
        [SerializeField,InspectorName("Версия генератора")] private int generatorVersion;
        [SerializeField,InspectorName("Зеркальный вариант")] private bool mirror;
        [SerializeField,InspectorName("Размер, клеток")] private Vector2Int size;
        [SerializeField,InspectorName("Клетка, м")] private float cell;
        [SerializeField,InspectorName("Разъёмы (после зеркала)")] private int[] connectors=Array.Empty<int>();

        public RoomTemplateAsset Template=>template;
        public string TemplateId=>templateId;
        public string Fingerprint=>fingerprint;
        public int GeneratorVersion=>generatorVersion;
        public bool Mirror=>mirror;
        public Vector2Int Size=>size;
        public float Cell=>cell;
        public int[] Connectors=>(int[])connectors.Clone();

        public void Configure(RoomTemplateAsset source,string sha256,int version,bool mirrored)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            var room=source.Room;
            template=source;templateId=room.Template;fingerprint=sha256;generatorVersion=version;mirror=mirrored;
            size=new Vector2Int(room.Width,room.Height);cell=(float)room.Cell;
            connectors=room.Connectors.Length==0?Array.Empty<int>():mirrored?FactoryLayoutGenerator.Mirror(room.Connectors):(int[])room.Connectors.Clone();
        }
    }
}
