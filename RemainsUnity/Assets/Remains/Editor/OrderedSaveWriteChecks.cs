using System;
using System.Collections.Concurrent;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    public static class OrderedSaveWriteChecks
    {
        public static void Run()
        {
            var writes = new OrderedSaveWrites();
            using var release = new ManualResetEventSlim(false);
            var observed = new ConcurrentQueue<string>();
            int errors = 0;
            writes.Enqueue(() => { if (!release.Wait(5000)) throw new TimeoutException(); observed.Enqueue("write1"); },
                () => observed.Enqueue("ack1"), _ => errors++);
            writes.Enqueue(() => observed.Enqueue("write2"), () => observed.Enqueue("ack2"), _ => errors++);
            var watch = Stopwatch.StartNew(); writes.Pump();
            Require(watch.ElapsedMilliseconds < 50 && observed.Count == 0, "Основной поток ожидал диск или подтвердил незавершённую запись.");
            release.Set();
            watch.Restart();
            while (writes.PendingCount > 0 && watch.ElapsedMilliseconds < 5000) { writes.Pump(); Thread.Sleep(1); }
            Require(errors == 0 && writes.PendingCount == 0 && Array.IndexOf(observed.ToArray(),"write1") < Array.IndexOf(observed.ToArray(),"write2") &&
                Array.IndexOf(observed.ToArray(),"write1") < Array.IndexOf(observed.ToArray(),"ack1") && Array.IndexOf(observed.ToArray(),"write2") < Array.IndexOf(observed.ToArray(),"ack2"),
                "Порядок записей или подтверждение долговечности нарушены.");
            int acknowledged = 0;
            writes.Enqueue(() => throw new System.IO.IOException("fixture"), () => acknowledged++, _ => errors++);
            writes.Enqueue(() => throw new InvalidOperationException("После ошибки не должна исполняться следующая запись."), () => acknowledged++, _ => errors++);
            watch.Restart();
            while (writes.PendingCount > 0 && watch.ElapsedMilliseconds < 5000) { writes.Pump(); Thread.Sleep(1); }
            Require(errors == 2 && acknowledged == 0, "Ошибка записи была скрыта или получила подтверждение.");
            writes.Enqueue(() => observed.Enqueue("final"), () => acknowledged++, _ => errors++);
            writes.Flush();
            Require(Array.IndexOf(observed.ToArray(),"final")>=0 && acknowledged == 0, "Завершение сессии не сохранило данные или исполнило сетевой callback.");
            Debug.Log("Очередь сохранений: PASS — кадр не ждёт диск, строгий порядок, ACK после записи, ошибки без ACK, завершение без сетевых callbacks.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
