using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponAssemblyChecks
    {
        public static void ImportBatch()
        {
            try
            {
                OriginalItemTextAuthoring.Run();NativeWeaponEmissionAuthoring.Run();
                Debug.Log("Оружие/свет: узкий импорт русских текстов и native emissions завершён.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void CheckBatch()
        {
            try
            {
                OriginalItemTextChecks.Run();NativeWeaponEmissionAuthoring.Check();WorldLightChecks.Run();HornLightChecks.Run();
                Debug.Log("Оружие/свет: проверки русского каталога, вспышек, подвижного света и рога PASS.");EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
