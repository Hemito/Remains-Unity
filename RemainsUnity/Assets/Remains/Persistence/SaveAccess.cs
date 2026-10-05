using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Remains.Persistence
{
    // A library operation owns its directory; an atomic publisher owns its file.
    // Named mutexes also coordinate separate local game processes using that library.
    public static class SaveAccess
    {
        public static IDisposable Enter(string path,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            string absolute=SavePath.Absolute(path);
            string identity=absolute;
            string root=Path.GetPathRoot(identity);
            while(identity.Length>root.Length && (identity[identity.Length-1]==Path.DirectorySeparatorChar || identity[identity.Length-1]==Path.AltDirectorySeparatorChar))
                identity=identity.Substring(0,identity.Length-1);
            if(Environment.OSVersion.Platform==PlatformID.Win32NT) identity=identity.ToUpperInvariant();
            using var hash=SHA256.Create();
            string key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
            var mutex=new Mutex(false,"Remains.SaveAccess."+key);
            bool acquired=false;
            try
            {
                try
                {
                    if(cancellation.CanBeCanceled)
                    {
                        acquired=WaitHandle.WaitAny(new[]{mutex,cancellation.WaitHandle})==0;
                        if(!acquired) cancellation.ThrowIfCancellationRequested();
                    }
                    else acquired=mutex.WaitOne();
                }
                catch(AbandonedMutexException)
                {
                    acquired=true;
                    Debug.LogWarning($"Предыдущая файловая операция была прервана: path={absolute}. Продолжается атомарная публикация; оставшиеся временные файлы не считаются сохранениями.");
                }
                cancellation.ThrowIfCancellationRequested();
                return new Lease(mutex);
            }
            catch
            {
                if(acquired) mutex.ReleaseMutex();
                mutex.Dispose();throw;
            }
        }
        private sealed class Lease : IDisposable
        {
            private Mutex mutex;
            public Lease(Mutex value)=>mutex=value;
            public void Dispose()
            {
                if(mutex==null) return;
                mutex.ReleaseMutex();mutex.Dispose();mutex=null;
            }
        }
    }
}
