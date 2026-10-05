using System;
using System.IO;
using UnityEngine;

namespace Remains.Runtime
{
    // Display naming and permanent storage identity are intentionally independent.
    public static class ProjectStorage
    {
        public const string DisplayName="Remains: Unity";
        public const string DataFolderName="Remains Unity";
        private static string rootDirectory;
        public static string RootDirectory=>rootDirectory??=FromEngineDirectory(Application.persistentDataPath);

        public static string FromEngineDirectory(string engineDirectory)
        {
            if(string.IsNullOrWhiteSpace(engineDirectory) || !Path.IsPathFullyQualified(engineDirectory))
                throw new InvalidOperationException($"Не задан абсолютный каталог данных Unity: path={engineDirectory}.");
            var parent=new DirectoryInfo(Path.GetFullPath(engineDirectory)).Parent;
            if(parent==null)
                throw new InvalidOperationException($"У каталога данных Unity нет родительского каталога компании: path={engineDirectory}.");
            return Path.Combine(parent.FullName,DataFolderName);
        }
    }
}
