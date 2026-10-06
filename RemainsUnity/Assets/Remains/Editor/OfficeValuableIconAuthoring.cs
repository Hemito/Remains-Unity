using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class OfficeValuableIconAuthoring
    {
        public const string ManifestResource="Content/office-valuable-icons";
        public const string CatalogPath=InventoryIconRegistration.CatalogPath;
        private const string SpritePrefix="office-valuables-";
        public static InventoryIconManifest ReadManifest()=>InventoryIconRegistration.ReadManifest(ManifestResource);
        [MenuItem("Remains/Контент/Добавить иконки офисных ценностей")]
        public static void Run()
        {
            int added=InventoryIconRegistration.Register(ManifestResource,SpritePrefix);
            Debug.Log($"Иконки офисных ценностей зарегистрированы: добавлено={added}. Прежние записи и спрайты сохранены.");
        }
        public static void CheckAuthored()=>InventoryIconRegistration.CheckAuthored(ManifestResource,SpritePrefix);
    }
}
