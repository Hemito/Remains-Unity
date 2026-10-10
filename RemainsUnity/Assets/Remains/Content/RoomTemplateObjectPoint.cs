using System;
using UnityEngine;

namespace Remains.Content
{
    // Where a template object (<obj> of the original: box, door, unit, spawn point, ...) stands in a
    // generated room prefab, after mirroring. Its type parameters, items and links stay in the template
    // (RoomTemplateInstance.Template, Objects[index]); the game installs the archetype from them.
    // The transform is the object's anchor: bottom centre of its footprint (size cells wide).
    public sealed class RoomTemplateObjectPoint : MonoBehaviour
    {
        [SerializeField,InspectorName("ID в шаблоне")] private string id;
        [SerializeField,InspectorName("Номер в шаблоне")] private int index;
        [SerializeField,InspectorName("Код (ключ связей оригинала)")] private string code;
        [SerializeField,InspectorName("Тип")] private string type;
        [SerializeField,InspectorName("Клетка (после зеркала)")] private Vector2Int cell;
        [SerializeField,InspectorName("Ширина, клеток")] private int width;

        public string Id=>id;   // persistent local ID (room-format §4.3); world IDs add factory and cell
        public int Index=>index;
        public string Code=>code;
        public string Type=>type;
        public Vector2Int Cell=>cell;
        public int Width=>width;

        public void Configure(string objectId,int objectIndex,string objectCode,string objectType,Vector2Int objectCell,int objectWidth)
        {
            if(string.IsNullOrEmpty(objectId)) throw new ArgumentException($"Точка объекта {objectType} {objectCode}: нет постоянного ID (пересоберите комнаты конвертером).");
            if(string.IsNullOrEmpty(objectType)) throw new ArgumentException("Точка объекта без типа.");
            id=objectId;index=objectIndex;code=objectCode;type=objectType;cell=objectCell;width=objectWidth;
        }
    }
}
