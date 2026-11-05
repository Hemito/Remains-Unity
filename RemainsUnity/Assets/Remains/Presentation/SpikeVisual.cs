using System;
using Remains.Content;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Trap.as: both static layers share the same registration; their random frames are independent.
    // The nested front SortingGroup sorts at the world root but retains activation/lifetime of its level.
    public sealed class SpikeVisual : MonoBehaviour
    {
        [SerializeField,InspectorName("Задний слой")] private SpriteRenderer rear;
        [SerializeField,InspectorName("Передний слой")] private SpriteRenderer front;
        [SerializeField,InspectorName("Мировая сортировка переднего слоя")] private SortingGroup frontGroup;
        public SpriteRenderer Rear=>rear;
        public SpriteRenderer Front=>front;
        public SortingGroup FrontGroup=>frontGroup;
        public string StableId {get;private set;}
        public void Configure(SpriteRenderer back,SpriteRenderer foreground,SortingGroup group)
        {rear=back;front=foreground;frontGroup=group;Validate();}
        public void Validate()
        {
            if(rear==null || front==null || frontGroup==null || frontGroup.transform!=front.transform ||
                rear.transform.parent!=transform || front.transform.parent!=transform || rear==front ||
                rear.sharedMaterial==null || front.sharedMaterial==null ||
                rear.sortingLayerID!=0 || rear.sortingOrder!=RoomTerrainView.ObjectOrder ||
                frontGroup.sortingLayerID!=0 || frontGroup.sortingOrder!=WorldVisualOrderPolicy.ForegroundFixtureOrder || !frontGroup.sortAtRoot ||
                front.sortingLayerID!=0 || front.sortingOrder!=0)
                throw new InvalidOperationException("Шипы: префаб требует два слоя с общей привязкой и отдельную мировую сортировку переднего слоя.");
            if(rear.transform.localPosition!=Vector3.zero || front.transform.localPosition!=Vector3.zero ||
                rear.transform.localScale!=Vector3.one || front.transform.localScale!=Vector3.one ||
                rear.transform.localRotation!=Quaternion.identity || front.transform.localRotation!=Quaternion.identity)
                throw new InvalidOperationException("Шипы: слои должны сохранять исходные координаты и масштаб.");
        }
        public void Initialize(SpikeArtworkDefinition definition,string stableId)
        {
            Validate();definition.Validate();
            if(string.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("Шипы: нужен устойчивый ID рисунка.");
            StableId=stableId;
            rear.sprite=definition.rear[FrameIndex(stableId,"rear",definition.rear.Length)].sprite;
            front.sprite=definition.front[FrameIndex(stableId,"front",definition.front.Length)].sprite;
        }
        // Explicit FNV-1a, independent of runtime string hashing and UnityEngine.Random state.
        public static int FrameIndex(string stableId,string layer,int count)
        {
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(layer) || count<1) throw new ArgumentException("Шипы: неверный ID/слой/число кадров.");
            uint hash=2166136261;
            unchecked {foreach(char value in stableId+"|"+layer) {hash^=value;hash*=16777619;}}
            return (int)(hash%(uint)count);
        }
    }
}
