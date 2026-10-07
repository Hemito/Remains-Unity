using System;
using System.Threading;

namespace Remains.Content
{
    // Construct and use on the Unity main thread. The service retains the actual
    // authored asset graph, never a mutable layout or a copied room index.
    public sealed class RoomCatalogLifetime
    {
        private readonly Func<RoomCatalogAsset> loader;
        private readonly string resourceKey;
        private readonly int owningThread;
        private RoomCatalogAsset catalog;
        private string acquiredContext;
        private int activeLeases;
        private long epoch;
        private bool acquiring;

        public RoomCatalogLifetime(Func<RoomCatalogAsset> loader,string resourceKey)
        {
            this.loader=loader??throw new ArgumentNullException(nameof(loader));
            if(string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("Не указан ресурс каталога комнат.",nameof(resourceKey));
            this.resourceKey=resourceKey;
            owningThread=Thread.CurrentThread.ManagedThreadId;
        }

        public int ActiveLeases {get {RequireThread();return activeLeases;}}
        public long Epoch {get {RequireThread();return epoch;}}

        public Lease Acquire(string context)
        {
            RequireThread();
            if(string.IsNullOrWhiteSpace(context))
                throw new ArgumentException("Не указан владелец каталога комнат: resource="+resourceKey+".",nameof(context));
            if(acquiring)
                throw new InvalidOperationException($"Повторный вход в загрузку каталога комнат: resource={resourceKey}, context={context}.");
            if(activeLeases>0)
            {
                // A destroyed asset under live owners is an error, never a reload.
                Require();
                if(activeLeases==int.MaxValue)
                    throw new InvalidOperationException($"Исчерпан счётчик владельцев каталога комнат: resource={resourceKey}, epoch={epoch}, context={context}.");
                var shared=new Lease(this,epoch);
                activeLeases++;
                return shared;
            }
            if(epoch==long.MaxValue)
                throw new InvalidOperationException($"Исчерпана эпоха каталога комнат: resource={resourceKey}, context={context}.");

            acquiring=true;
            try
            {
                RoomCatalogAsset candidate;
                try
                {
                    candidate=loader();
                    if(candidate==null)
                        throw new InvalidOperationException("Ресурс отсутствует или его native-объект уничтожен.");
                    candidate.Validate();
                }
                catch(Exception error)
                {
                    throw new InvalidOperationException($"Не удалось подготовить каталог комнат: resource={resourceKey}, context={context}, nextEpoch={epoch+1}. {error.Message}",error);
                }
                // Publish only after loading, validation and lease creation succeed.
                long nextEpoch=epoch+1;
                var first=new Lease(this,nextEpoch);
                catalog=candidate;
                acquiredContext=context;
                epoch=nextEpoch;
                activeLeases=1;
                return first;
            }
            finally {acquiring=false;}
        }

        public RoomCatalogAsset Require()
        {
            RequireThread();
            if(activeLeases==0)
                throw new InvalidOperationException($"Каталог комнат не подготовлен: resource={resourceKey}, epoch={epoch}, activeLeases=0. Сначала явно приобретите каталог для runtime.");
            // Unity's native-object null check also rejects a destroyed SO whose
            // managed wrapper still exists. Do not hide it with Resources.Load.
            if(catalog==null)
                throw new InvalidOperationException($"Уничтожен удерживаемый каталог комнат: resource={resourceKey}, epoch={epoch}, activeLeases={activeLeases}, context={acquiredContext}. Владельцы должны завершить текущую эпоху.");
            return catalog;
        }

        private void Release(long leaseEpoch)
        {
            RequireThread();
            // An old token cannot change a later content owner or its asset.
            if(leaseEpoch!=epoch) return;
            if(activeLeases<=0)
                throw new InvalidOperationException($"Нарушен счётчик владельцев каталога комнат: resource={resourceKey}, epoch={epoch}.");
            activeLeases--;
            if(activeLeases!=0) return;
            catalog=null;
            acquiredContext=null;
            // Shared authored assets belong to Unity; never Destroy/UnloadAsset.
        }

        private void RequireThread()
        {
            if(Thread.CurrentThread.ManagedThreadId!=owningThread)
                throw new InvalidOperationException($"Каталог комнат вызван с другого потока: resource={resourceKey}, ownerThread={owningThread}, actualThread={Thread.CurrentThread.ManagedThreadId}. Создавайте и используйте сервис на главном потоке Unity.");
        }

        public sealed class Lease : IDisposable
        {
            private readonly RoomCatalogLifetime owner;
            private readonly long epoch;
            private bool disposed;
            internal Lease(RoomCatalogLifetime owner,long epoch) {this.owner=owner;this.epoch=epoch;}
            public void Dispose()
            {
                if(disposed) return;
                owner.Release(epoch);
                disposed=true;
            }
        }
    }
}
