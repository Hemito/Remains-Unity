using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Remains.Persistence
{
    // Workers receive immutable captured data. Completion callbacks run only in
    // Pump on the game thread. A receipt is emitted after its durable write.
    public sealed class OrderedSaveWrites
    {
        private sealed class Pending
        {
            public Task task;
            public Action completed;
            public Action<Exception> failed;
        }
        private readonly Queue<Pending> pending = new();
        private Task tail = Task.CompletedTask;
        public int PendingCount => pending.Count;
        public void Enqueue(Action write, Action completed, Action<Exception> failed)
        {
            if (write == null || failed == null) throw new ArgumentNullException();
            Task prior = tail;
            tail = Task.Run(async () => { await prior.ConfigureAwait(false); write(); });
            pending.Enqueue(new Pending { task = tail, completed = completed, failed = failed });
        }
        public void Pump()
        {
            while (pending.Count > 0 && pending.Peek().task.IsCompleted)
            {
                var operation = pending.Dequeue();
                try { operation.task.GetAwaiter().GetResult(); }
                catch (Exception error) { operation.failed(error); continue; }
                operation.completed?.Invoke();
            }
            if (pending.Count == 0) tail = Task.CompletedTask;
        }
        // Shutdown may wait for disk. It must not acknowledge old network state
        // or schedule new gameplay operations while the session is being closed.
        public void Flush()
        {
            Exception first = null;
            while (pending.Count > 0)
            {
                var operation = pending.Dequeue();
                try { operation.task.GetAwaiter().GetResult(); }
                catch (Exception error) { first ??= error; }
            }
            tail = Task.CompletedTask;
            if (first != null) throw new System.IO.IOException("Не удалось завершить очередь записи сохранений.", first);
        }
    }
}
