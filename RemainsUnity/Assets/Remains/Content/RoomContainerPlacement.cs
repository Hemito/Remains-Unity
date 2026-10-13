namespace Remains.Content
{
    public sealed class RoomContainerPlacement : RoomPhysicalPlacement
    {
        [UnityEngine.SerializeField,UnityEngine.InspectorName("Начальное содержимое (только при создании)")]
        private Remains.Domain.ItemStack[] initialItems=System.Array.Empty<Remains.Domain.ItemStack>();
        [UnityEngine.SerializeField,UnityEngine.InspectorName("Дополнительный начальный лут (необязательно)")]
        private ContainerLootAsset loot;
        [UnityEngine.SerializeField,UnityEngine.HideInInspector] private string sourceState;
        public void SetSourceState(Remains.Domain.ContainerState state)=>sourceState=Remains.Serialization.StateJson.ToJson(state);
        public Remains.Domain.ContainerState ReadSourceState()=>string.IsNullOrEmpty(sourceState)?null:Remains.Serialization.StateJson.FromJson<Remains.Domain.ContainerState>(sourceState);
        public ContainerLootTable ReadLoot()=>loot==null?null:loot.Read();
        public void SetLoot(ContainerLootAsset value)=>loot=value;
        public Remains.Domain.ItemStack[] ReadInitialItems()=>Remains.Serialization.StateJson.Copy(initialItems);
    }
}
