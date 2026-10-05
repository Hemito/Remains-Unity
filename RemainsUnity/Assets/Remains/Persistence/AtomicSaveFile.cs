using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Remains.Persistence
{
    public static class AtomicSaveFile
    {
        public static void Write(string destination,string content,bool uniqueTemporary=true)
            =>Write(destination,content,uniqueTemporary,default);
        public static void Write(string destination,string content,bool uniqueTemporary,CancellationToken cancellation)
        {
            destination=SavePath.Absolute(destination);
            using var access=SaveAccess.Enter(destination,cancellation);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary=destination+".tmp"+(uniqueTemporary?"-"+Guid.NewGuid().ToString("N"):"");
            File.WriteAllText(temporary,content);
            for(int attempt=0;;attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if(File.Exists(destination)) File.Replace(temporary,destination,destination+".previous");
                    else File.Move(temporary,destination);
                    return;
                }
                catch(IOException error) when(attempt<4 && File.Exists(temporary))
                {
                    // Keep the original and pending data intact. Never replace atomic publication with delete/copy.
                    int delay=10<<attempt;
                    Debug.LogWarning($"Не удалось опубликовать сохранение: файл={destination}, попытка={attempt+1}, код={error.HResult}, повтор через {delay} мс. Причина: {error.Message}");
                    if(cancellation.CanBeCanceled) {if(cancellation.WaitHandle.WaitOne(delay)) cancellation.ThrowIfCancellationRequested();}
                    else Thread.Sleep(delay);
                }
            }
        }
    }
}
