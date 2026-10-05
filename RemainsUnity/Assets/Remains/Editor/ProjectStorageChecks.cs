using System;
using System.IO;
using Remains.Runtime;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ProjectStorageChecks
    {
        public static void Run()
        {
            var company=Path.Combine(Path.GetFullPath("."),"StorageIdentity","Company");
            var legacy=Path.Combine(company,ProjectStorage.DataFolderName);
            foreach(string product in new[]{"Remains Unity","Remains Unity Renamed","Remains_ Unity"})
                if(ProjectStorage.FromEngineDirectory(Path.Combine(company,product))!=legacy)
                    throw new InvalidOperationException("Отображаемое имя изменило постоянный каталог профилей/настроек.");
            foreach(string invalid in new[]{null,"","relative","..",Path.GetPathRoot(company)})
            {
                bool rejected=false;
                try {ProjectStorage.FromEngineDirectory(invalid);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Принят неявный/относительный каталог данных: "+invalid);
            }
            if(PlayerSettings.productName!=ProjectStorage.DisplayName)
                throw new InvalidOperationException($"Название приложения отличается от согласованного: actual={PlayerSettings.productName}, expected={ProjectStorage.DisplayName}.");
            Debug.Log("ProjectStorageChecks PASS: новое имя приложения сохраняет постоянный каталог данных; неявные пути отвергнуты.");
        }
    }
}
