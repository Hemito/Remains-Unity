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
        [SerializeField,InspectorName("Номер в шаблоне")] private int index;
        [SerializeField,InspectorName("Код (ключ связей оригинала)")] private string code;
        [SerializeField,InspectorName("Тип")] private string type;
        [SerializeField,InspectorName("Клетка (после зеркала)")] private Vector2Int cell;
        [SerializeField,InspectorName("Ширина, клеток")] private int width;

        public int Index=>index;
        public string Code=>code;
        public string Type=>type;
        public Vector2Int Cell=>cell;
        public int Width=>width;

        public void Configure(int objectIndex,string objectCode,string objectType,Vector2Int objectCell,int objectWidth)
        {
            if(string.IsNullOrEmpty(objectType)) throw new ArgumentException("Точка объекта без типа.");
            index=objectIndex;code=objectCode;type=objectType;cell=objectCell;width=objectWidth;
        }
    }
}
