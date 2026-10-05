using System;
using System.IO;
using System.Threading.Tasks;
using Remains.Persistence;

namespace Remains.Editor
{
    public static class AtomicSaveChecks
    {
        public static void Run()
        {
            string directory=Path.Combine(Path.GetTempPath(),"remains-atomic-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"world.json");
            try
            {
                AtomicSaveFile.Write(path,"first");
                AtomicSaveFile.Write(path,"second");
                Require(File.ReadAllText(path)=="second" && File.ReadAllText(path+".previous")=="first","Публикация сохраняет предыдущую версию");
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    bool rejected=false;
                    try {AtomicSaveFile.Write(path,"pending");} catch(IOException) {rejected=true;}
                    var pending=Directory.GetFiles(directory,"world.json.tmp-*");
                    Require(rejected && File.ReadAllText(path)=="second" && pending.Length==1 && File.ReadAllText(pending[0])=="pending","Постоянная блокировка не подтверждает запись и сохраняет исходник/ожидающие данные в уникальном временном файле");
                }
                var transient=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                var release=Task.Run(()=>{System.Threading.Thread.Sleep(35);transient.Dispose();});
                try {AtomicSaveFile.Write(path,"third");} finally {release.Wait();transient.Dispose();}
                Require(File.ReadAllText(path)=="third" && File.ReadAllText(path+".previous")=="second","После краткой блокировки публикуется новая версия с резервной копией");
            }
            finally {Directory.Delete(directory,true);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
