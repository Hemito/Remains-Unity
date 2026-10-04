namespace Remains.Content
{
    public sealed class RoomContainerPlacement : RoomPhysicalPlacement
    {
        [UnityEngine.SerializeField,UnityEngine.InspectorName("Начальное содержимое (только при создании)")]
        private Remains.Domain.ItemStack[] initialItems=System.Array.Empty<Remains.Domain.ItemStack>();
        public Remains.Domain.ItemStack[] ReadInitialItems()=>Remains.Serialization.StateJson.Copy(initialItems);
    }
}
