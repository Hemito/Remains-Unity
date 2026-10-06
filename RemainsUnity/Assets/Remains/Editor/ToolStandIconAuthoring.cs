using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ToolStandIconAuthoring
    {
        public const string ManifestResource="Content/tool-stand-icons";
        public const string CatalogPath=InventoryIconRegistration.CatalogPath;
        private const string SpritePrefix="tool-stand-";
        public static InventoryIconManifest ReadManifest()=>InventoryIconRegistration.ReadManifest(ManifestResource);
        [MenuItem("Remains/Контент/Добавить иконки стенда инструментов")]
        public static void Run()
        {
            int added=InventoryIconRegistration.Register(ManifestResource,SpritePrefix);
            Debug.Log($"Иконки стенда зарегистрированы: добавлено={added}. Прежние записи и спрайты сохранены.");
        }
        public static void CheckAuthored()=>InventoryIconRegistration.CheckAuthored(ManifestResource,SpritePrefix);
    }
}
