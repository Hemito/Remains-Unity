namespace Remains.Content
{
    public sealed class RoomContainerPlacement : RoomPhysicalPlacement
    {
        [UnityEngine.SerializeField,UnityEngine.InspectorName("Начальное содержимое (только при создании)")]
        private Remains.Domain.ItemStack[] initialItems=System.Array.Empty<Remains.Domain.ItemStack>();
        [UnityEngine.SerializeField,UnityEngine.InspectorName("Дополнительный начальный лут (необязательно)")]
        private ContainerLootAsset loot;
        public ContainerLootTable ReadLoot()=>loot==null?null:loot.Read();
        public void SetLoot(ContainerLootAsset value)=>loot=value;
        public Remains.Domain.ItemStack[] ReadInitialItems()=>Remains.Serialization.StateJson.Copy(initialItems);
    }
}
