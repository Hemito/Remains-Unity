using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Начальный лут контейнера",fileName="ContainerLoot")]
    public sealed class ContainerLootAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Таблица (количество 0 означает пустой результат)")] private ContainerLootTable table=new();
        public ContainerLootTable Read() {table.Validate();return StateJson.Copy(table);}
        public void Configure(ContainerLootTable value) {value.Validate();table=StateJson.Copy(value);}
        private void OnValidate()
        {try {table.Validate();} catch(InvalidOperationException error) {Debug.LogWarning($"Таблица {name}: {error.Message}",this);}}
    }
}
