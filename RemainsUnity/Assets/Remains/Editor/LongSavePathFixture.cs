using System;
using System.IO;
using Remains.Persistence;

namespace Remains.Editor
{
    // Keep intentional MAX_PATH stress outside the checkout and remove only this run's private tree.
    internal sealed class LongSavePathFixture : IDisposable
    {
        private readonly string temporaryRoot;
        public string Root {get;}
        public LongSavePathFixture()
        {
            temporaryRoot=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"pfe-ls-"+Guid.NewGuid().ToString("N")));
            if(Directory.Exists(SavePath.Absolute(temporaryRoot)) || File.Exists(SavePath.Absolute(temporaryRoot)))
                throw new IOException("Временная папка проверки длинного пути уже занята.");
            string root=temporaryRoot;
            while(root.Length<280)
                root=Path.Combine(root,new string('p',Math.Max(1,Math.Min(64,280-root.Length-1))));
            Root=root;
            Directory.CreateDirectory(SavePath.Absolute(temporaryRoot));
        }
        public void Dispose()
        {
            string path=SavePath.Absolute(temporaryRoot);
            if(Directory.Exists(path)) Directory.Delete(path,true);
        }
    }
}
