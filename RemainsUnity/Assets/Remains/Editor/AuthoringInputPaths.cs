using System;
using System.IO;

namespace Remains.Editor
{
    // Local research inputs are deliberately outside runtime assets. Relative configuration is repository-relative,
    // so an Editor launch from another working directory cannot silently select a different source file.
    internal static class AuthoringInputPaths
    {
        public static string RequireFile(string repositoryRoot,string setting,string relativeDefault,string description)
        {
            string configured=Environment.GetEnvironmentVariable(setting);
            string selected=configured??relativeDefault;
            if(string.IsNullOrWhiteSpace(selected))
                throw new InvalidDataException($"{description}: переменная {setting} задана пустой. Укажите путь к исходному файлу.");
            string path=Path.GetFullPath(Path.IsPathRooted(selected)?selected:Path.Combine(repositoryRoot,selected));
            if(!File.Exists(path))
                throw new FileNotFoundException($"{description}: исходный файл отсутствует: {path}. Укажите {setting} (абсолютный путь или путь от корня репозитория); стандартный путь: {relativeDefault}.",path);
            return path;
        }
    }
}
