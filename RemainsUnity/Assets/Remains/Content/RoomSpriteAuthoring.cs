using System;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // A decor or object picture of a generated room prefab (RoomSpriteLayout): which placement of the
    // template it is, for light (lamps emit over the silhouette, objects absorb) and the background erase.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RoomSpriteAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Вид")] private RoomSpriteKind kind;
        [SerializeField,InspectorName("Номер в шаблоне")] private int index;
        [SerializeField,InspectorName("Определение")] private string definition;
        [SerializeField,InspectorName("Кадр")] private int frame;
        [SerializeField,InspectorName("Стирание фона (окно)")] private Sprite erase;

        public RoomSpriteKind Kind=>kind;
        public int Index=>index;
        public string Definition=>definition;
        public int Frame=>frame;
        public Sprite Erase=>erase;

        public void Configure(RoomPlacedSprite placed,Sprite eraseSprite)
        {
            if(placed==null) throw new ArgumentNullException(nameof(placed));
            kind=placed.Kind;index=placed.Index;definition=placed.Definition.Id;frame=placed.Frame;erase=eraseSprite;
        }
    }
}
