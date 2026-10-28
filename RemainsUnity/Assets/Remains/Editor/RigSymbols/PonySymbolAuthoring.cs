using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonySymbolAuthoring
    {
        [MenuItem("Remains/Пони/Собрать символы и гардероб")]
        public static void Build()
        {
            WearableRenderCompiler.Build();
            PonyRigMotionCompiler.Build();
            PonyNestedEyeAuthoring.Build();
            PonyAppearanceAuthoring.PrepareCompiled();
            CharacterInterfaceAuthoring.RefreshSavedPortraits();AssetDatabase.SaveAssets();
            Debug.Log("Сборная кукла подготовлена: локальные символы, гардероб 1.04, формы головы, собственный закрытый глаз и сохранённые ссылки портретов.");
        }
    }
}
