using System;
using System.IO;
using UnityEngine;

namespace Remains.Editor
{
    public static class AuthoringInputPathChecks
    {
        public static void Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"remains-authoring-input-"+Guid.NewGuid().ToString("N"));
            string setting="REMAINS_INPUT_CHECK_"+Guid.NewGuid().ToString("N");
            string previous=Environment.GetEnvironmentVariable(setting),working=Environment.CurrentDirectory;
            Directory.CreateDirectory(root);
            try
            {
                string relative=Path.Combine("исходники с пробелом","текст.xml"),expected=Path.Combine(root,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(expected));File.WriteAllText(expected,"<data />");
                string elsewhere=Path.Combine(root,"другая папка");Directory.CreateDirectory(elsewhere);
                Environment.CurrentDirectory=elsewhere;Environment.SetEnvironmentVariable(setting,null);
                Require(AuthoringInputPaths.RequireFile(root,setting,relative,"Вход")==expected,"Стандартный путь зависит от текущей папки.");
                Environment.SetEnvironmentVariable(setting,relative);
                Require(AuthoringInputPaths.RequireFile(root,setting,"unused","Вход")==expected,"Относительная конфигурация не от корня репозитория.");
                Environment.SetEnvironmentVariable(setting,expected);
                Require(AuthoringInputPaths.RequireFile(root,setting,"unused","Вход")==expected,"Абсолютная конфигурация потеряна.");
                Environment.SetEnvironmentVariable(setting,"missing.xml");
                Reject<FileNotFoundException>(()=>AuthoringInputPaths.RequireFile(root,setting,relative,"Вход"),setting);
                Environment.SetEnvironmentVariable(setting,elsewhere);
                Reject<FileNotFoundException>(()=>AuthoringInputPaths.RequireFile(root,setting,relative,"Вход"),setting);
                Environment.SetEnvironmentVariable(setting," ");
                Reject<InvalidDataException>(()=>AuthoringInputPaths.RequireFile(root,setting,relative,"Вход"),setting);
                Require(File.ReadAllText(expected)=="<data />","Выбор входа изменил исходный файл.");
            }
            finally
            {
                Environment.CurrentDirectory=working;Environment.SetEnvironmentVariable(setting,previous);Directory.Delete(root,true);
            }
            Debug.Log("Пути авторинга: PASS — явные абсолютные/относительные входы, независимость от CWD, Unicode/пробелы, отсутствие silent fallback и запись только в частную временную папку проверки.");
        }
        private static void Reject<T>(Action action,string setting) where T:Exception
        {
            try {action();}
            catch(T error) {Require(error.Message.Contains(setting),"Диагностика не называет настройку входа.");return;}
            throw new InvalidOperationException("Не отклонён отсутствующий/пустой вход: "+setting);
        }
        private static void Require(bool value,string reason) {if(!value)throw new InvalidOperationException(reason);}
    }
}
