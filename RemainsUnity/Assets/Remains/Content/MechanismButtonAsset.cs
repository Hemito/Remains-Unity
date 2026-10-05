using System;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Content
{
    [Serializable]
    public sealed class MechanismButtonDefinition : ExtensibleState
    {
        public int schema=1,version=1;
        public string id;
        [InspectorName("Временная настройка Unity: удержание ремонта, секунды")] public double repairDurationSeconds;
        [InspectorName("Временная настройка Unity: радиус взаимодействия")] public float interactionRadius;
        public float graphicOffsetX,graphicOffsetY,graphicWidth,graphicHeight;
        public void Validate()
        {
            if(schema!=1 || version<=0 || string.IsNullOrWhiteSpace(id) || !double.IsFinite(repairDurationSeconds) || repairDurationSeconds<=0 ||
                !float.IsFinite(interactionRadius) || interactionRadius<=0 || !float.IsFinite(graphicOffsetX) || !float.IsFinite(graphicOffsetY) ||
                !float.IsFinite(graphicWidth) || graphicWidth<=0 || !float.IsFinite(graphicHeight) || graphicHeight<=0 ||
                !float.IsFinite(graphicOffsetX+graphicWidth) || !float.IsFinite(graphicOffsetY+graphicHeight))
                throw new InvalidOperationException("Неполное определение кнопки: id="+id+", schema="+schema+", version="+version);
        }
    }
    [CreateAssetMenu(menuName="Remains/Кнопка механизма",fileName="MechanismButton")]
    public sealed class MechanismButtonAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Определение кнопки; ремонт без навыков, стоимости и случайности")] private MechanismButtonDefinition definition=new();
        [SerializeField,InspectorName("Закрытая кнопка: сохранённая графика")] private GameObject closedPrefab;
        [SerializeField,InspectorName("Открытая кнопка: готовый PNG с однократным свечением")] private GameObject openedPrefab;
        public string Id=>definition.id;
        public GameObject ClosedPrefab=>closedPrefab;
        public GameObject OpenedPrefab=>openedPrefab;
        public MechanismButtonDefinition CreateDefinition()=>StateJson.Copy(definition);
        public void Configure(MechanismButtonDefinition value,GameObject closed,GameObject opened)
        {definition=StateJson.Copy(value);closedPrefab=closed;openedPrefab=opened;Validate();}
        public void Validate()
        {
            if(definition==null) throw new InvalidOperationException("Кнопка "+name+": нет определения.");
            definition.Validate();
            if(closedPrefab==null || openedPrefab==null) throw new InvalidOperationException("Кнопка "+name+": нет обоих сохранённых состояний, id="+definition.id);
        }
        private void OnValidate() {try {Validate();}catch(InvalidOperationException error){Debug.LogWarning(error.Message,this);}}
    }
}
