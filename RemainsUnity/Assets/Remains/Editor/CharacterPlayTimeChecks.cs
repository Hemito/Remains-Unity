using System;
using System.Collections.Generic;
using Stopwatch=System.Diagnostics.Stopwatch;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    // Real files and the production queue, with controlled clocks/write scheduling.
    // No live profiles, world state or networking fixtures are required.
    public static class CharacterPlayTimeChecks
    {
        public static void Run()=>RunAt(Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../.reference/tests/character-time-"+Guid.NewGuid().ToString("N"))));
        public static void RunAt(string root,bool log=true)
        {
            if(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidOperationException("Проверка времени не заменяет существующий каталог: "+root);
            Directory.CreateDirectory(root);
            CheckQueued(Path.Combine(root,"queued"));
            CheckProfileContext(Path.Combine(root,"profiles"));
            CheckClock(Path.Combine(root,"clock"));
            CheckWriteFailure(Path.Combine(root,"write-failure"));
            CheckReadFailure(Path.Combine(root,"read-failure"));
            CheckCorrupt(Path.Combine(root,"corrupt"));
            CheckDirectoryFile(Path.Combine(root,"directory-file"));
            CheckExternalChange(Path.Combine(root,"external-change"));
            if(log) Debug.Log("Время персонажа: PASS — Capture без I/O под file lock, FIFO накопление/unknowns, selected profile/старый context, Flush без callbacks/перезапуск, ошибочные часы, atomic write/read/corrupt/path failure, новый captured retry и single-use.");
        }
        private static void CheckQueued(string profile)
        {
            string source=Seed(profile,100,true);double clock=10;
            var timer=new CharacterPlayTime(()=>clock);var queue=new OrderedSaveWrites();
            var failures=new List<Exception>();int callbacks=0;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            CharacterPlayTime.CapturedCheckpoint first=null,second=null,third=null,fourth=null;
            try
            {
                using(var locked=new FileStream(TimePath(profile),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    first=timer.CaptureCheckpoint(profile);
                    queue.Enqueue(()=>{entered.Set();Wait(release,"разрешение записи");first.Persist();},()=>callbacks++,failures.Add);
                    Wait(entered,"вход worker");
                    clock=15;second=timer.CaptureCheckpoint(profile);clock=22;third=timer.CaptureCheckpoint(profile);
                    clock=30;fourth=timer.CaptureCheckpoint(profile);
                    queue.Enqueue(second.Persist,()=>callbacks++,failures.Add);
                    queue.Enqueue(third.Persist,()=>callbacks++,failures.Add);
                    queue.Enqueue(fourth.Persist,()=>callbacks++,failures.Add);
                    queue.Pump();
                    Require(callbacks==0 && failures.Count==0 && queue.PendingCount==4 && locked.Length==new System.Text.UTF8Encoding(false).GetByteCount(source),
                        "незавершённая запись получила callback или Capture изменил заблокированный источник");
                }
                release.Set();Drain(queue);
            }
            finally {release.Set();queue.Flush();}
            Require(callbacks==4 && failures.Count==0 && first.IsPersisted && second.IsPersisted && third.IsPersisted && fourth.IsPersisted,
                "FIFO потерял/повторил запись или подтверждение");
            SameTime(profile,120,source);Rejected(first.Persist,"повтор успешного work");
            // A later capture at the same clock cannot count an elapsed interval twice.
            timer.CaptureCheckpoint(profile).Persist();SameTime(profile,120,source);
            clock=31.5;var final=timer.CaptureCheckpoint(profile);
            queue.Enqueue(final.Persist,()=>callbacks++,failures.Add);clock=2000;
            queue.Flush();timer.Stop();
            Require(callbacks==4 && final.IsPersisted && queue.PendingCount==0,"Flush зависел от completion callback");
            SameTime(profile,121.5,source);
            clock=5000;timer=new CharacterPlayTime(()=>clock);timer.CaptureCheckpoint(profile).Persist();
            clock=5002.5;timer.CaptureCheckpoint(profile).Persist();timer.Stop();SameTime(profile,124,source);
            Require(File.ReadAllText(Path.Combine(profile,"character.json"))=="Исходный личный профиль", "учёт изменил character.json");
        }
        private static void CheckProfileContext(string root)
        {
            string a=Path.Combine(root,"A"),b=Path.Combine(root,"B");string originalA=Seed(a,100,true),originalB=Seed(b,200,false);
            double clock=10;var timer=new CharacterPlayTime(()=>clock);var oldFirst=timer.CaptureCheckpoint(a);
            clock=15;var oldLast=timer.CaptureCheckpoint(a);
            Rejected(()=>timer.CaptureCheckpoint(b),"смена выбранного профиля до Stop");
            Require(File.ReadAllText(TimePath(b))==originalB,"отказ выбора изменил другой профиль");
            timer.Stop();clock=100;var newFirst=timer.CaptureCheckpoint(b);clock=110;var newLast=timer.CaptureCheckpoint(b);
            oldFirst.Persist();oldLast.Persist();newFirst.Persist();newLast.Persist();timer.Stop();
            SameTime(a,105,originalA);SameTime(b,210,originalB);
            Require(oldLast.ProfileDirectory!=newLast.ProfileDirectory,"captured context потерял собственный путь");
        }
        private static void CheckClock(string profile)
        {
            string source=Seed(profile,7,false);double clock=10;bool forbidClock=false;
            var timer=new CharacterPlayTime(()=>{if(forbidClock) throw new InvalidOperationException("Persist прочёл живые часы.");return clock;});
            timer.CaptureCheckpoint(profile).Persist();
            foreach(double invalid in new[]{9,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            {clock=invalid;Rejected(()=>timer.CaptureCheckpoint(profile),"обратные/неконечные часы");}
            clock=20;timer.CaptureCheckpoint(profile);clock=19;Rejected(()=>timer.CaptureCheckpoint(profile),"откат после более позднего capture");
            clock=25;var final=timer.CaptureCheckpoint(profile);forbidClock=true;final.Persist();timer.Stop();SameTime(profile,22,source);
        }
        private static void CheckWriteFailure(string profile)
        {
            string source=Seed(profile,100,true);double clock=10;var timer=new CharacterPlayTime(()=>clock);
            timer.CaptureCheckpoint(profile).Persist();string before=File.ReadAllText(TimePath(profile));
            string previous=File.ReadAllText(TimePath(profile)+".previous");clock=15;var failed=timer.CaptureCheckpoint(profile);
            string previousPath=TimePath(profile)+".previous",preservedPrevious=previousPath+"-fixture";
            var queue=new OrderedSaveWrites();var errors=new List<Exception>();int callbacks=0;
            clock=17;var skipped=timer.CaptureCheckpoint(profile);
            File.Move(previousPath,preservedPrevious);Directory.CreateDirectory(previousPath);
            try
            {
                queue.Enqueue(failed.Persist,()=>callbacks++,errors.Add);
                queue.Enqueue(skipped.Persist,()=>callbacks++,errors.Add);Drain(queue);
                Require(callbacks==0 && errors.Count==2 && !skipped.IsPersisted,"ошибка atomic write получила callback или не остановила FIFO tail");
            }
            finally {Directory.Delete(previousPath,false);File.Move(preservedPrevious,previousPath);}
            Require(!failed.IsPersisted && failed.WriteMilliseconds>0 && File.ReadAllText(TimePath(profile))==before && File.ReadAllText(previousPath)==previous,
                "ошибка записи подтвердила work или изменила atomic source/previous");
            Rejected(failed.Persist,"повтор неудачного work");
            clock=20;var retry=timer.CaptureCheckpoint(profile);queue.Enqueue(retry.Persist,()=>callbacks++,errors.Add);Drain(queue);
            Require(callbacks==1 && errors.Count==2 && retry.IsPersisted,"новый captured retry не завершился после очистки failed FIFO");
            timer.Stop();SameTime(profile,110,source);
        }
        private static void CheckReadFailure(string profile)
        {
            string source=Seed(profile,40,false);double clock=10;var timer=new CharacterPlayTime(()=>clock);var captured=timer.CaptureCheckpoint(profile);
            using(var locked=new FileStream(TimePath(profile),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Rejected(captured.Persist,"ошибка чтения первого checkpoint");
            Require(!captured.IsPersisted && File.ReadAllText(TimePath(profile))==source,"ошибка чтения заменила исходник");
            clock=15;timer.CaptureCheckpoint(profile).Persist();timer.Stop();SameTime(profile,45,source);
        }
        private static void CheckCorrupt(string profile)
        {
            Directory.CreateDirectory(profile);string corrupt="{\"version\":1,\"seconds\":-1,\"trackedFrom\":\"broken\",\"keep\":7}";
            File.WriteAllText(TimePath(profile),corrupt);double clock=10;var timer=new CharacterPlayTime(()=>clock);
            var captured=timer.CaptureCheckpoint(profile);Rejected(captured.Persist,"повреждённый учёт вместо default");
            Require(!captured.IsPersisted && File.ReadAllText(TimePath(profile))==corrupt,"повреждённый источник перезаписан");
            // Explicit fixture repair of the same file, not an automatic production fallback.
            string source=Seed(profile,30,false);clock=20;timer.CaptureCheckpoint(profile).Persist();timer.Stop();SameTime(profile,40,source);
        }
        private static void CheckDirectoryFile(string root)
        {
            Directory.CreateDirectory(root);string profile=Path.Combine(root,"profile");File.WriteAllText(profile,"Файл вместо папки");
            double clock=10;var timer=new CharacterPlayTime(()=>clock);var first=timer.CaptureCheckpoint(profile);
            Rejected(first.Persist,"профиль является файлом");Require(!first.IsPersisted && File.ReadAllText(profile)=="Файл вместо папки","ошибка папки уничтожила исходный файл");
            File.Delete(profile);Directory.CreateDirectory(profile);clock=20;timer.CaptureCheckpoint(profile).Persist();timer.Stop();
            var saved=CharacterTimeRecord.Read(profile);Require(saved.seconds==10 && !saved.completeHistory,"retry path выдумал историю/потерял elapsed");
        }
        private static void CheckExternalChange(string profile)
        {
            Seed(profile,100,true);double clock=10;var timer=new CharacterPlayTime(()=>clock);timer.CaptureCheckpoint(profile).Persist();
            string outside=Seed(profile,500,true);clock=15;var stale=timer.CaptureCheckpoint(profile);
            Rejected(stale.Persist,"внешнее изменение после успешной публикации");
            Require(!stale.IsPersisted && File.ReadAllText(TimePath(profile))==outside,"stale context заменил более новое содержимое");
            timer.Stop();clock=100;timer.CaptureCheckpoint(profile).Persist();clock=103;timer.CaptureCheckpoint(profile).Persist();timer.Stop();SameTime(profile,503,outside);
        }
        private static string Seed(string profile,double seconds,bool complete)
        {
            Directory.CreateDirectory(profile);
            var value=new JObject {{"version",1},{"seconds",seconds},{"completeHistory",complete},{"trackedFrom","2026-10-07T00:00:00.0000000+00:00"},
                {"mod-time",new JObject {{"nested",new JArray(7,"сохранить",new JObject {{"enabled",true}})},{"precise",1.2345678901234567890123456789m}}}};
            string source=value.ToString(Formatting.Indented);File.WriteAllText(TimePath(profile),source);
            File.WriteAllText(Path.Combine(profile,"character.json"),"Исходный личный профиль");return source;
        }
        private static string TimePath(string profile)=>Path.Combine(profile,"playtime.json");
        private static void SameTime(string profile,double expected,string original)
        {
            var actual=CharacterTimeRecord.Read(profile);Require(actual.seconds==expected,"неверная независимая сумма elapsed: "+profile+", actual="+actual.seconds+", expected="+expected);
            var before=ExactJson(original);var after=ExactJson(File.ReadAllText(TimePath(profile)));
            before.Remove("seconds");after.Remove("seconds");
            Require(JToken.DeepEquals(before,after),"изменились version/unknown fields/trackedFrom/completeHistory: "+profile);
        }
        private static JObject ExactJson(string json)
        {
            using var text=new StringReader(json);using var reader=new JsonTextReader(text) {FloatParseHandling=FloatParseHandling.Decimal,DateParseHandling=DateParseHandling.None};
            return JObject.Load(reader);
        }
        private static void Wait(ManualResetEventSlim value,string name)
        {Require(value.Wait(TimeSpan.FromSeconds(10)),"таймаут gate: "+name);}
        private static void Drain(OrderedSaveWrites queue)
        {
            var timer=Stopwatch.StartNew();while(queue.PendingCount>0 && timer.Elapsed.TotalSeconds<10)
            {queue.Pump();if(queue.PendingCount>0) Thread.Sleep(1);}
            Require(queue.PendingCount==0,"очередь учёта времени не завершилась");
        }
        private static void Rejected(Action action,string name)
        {
            try {action();} catch(Exception error) when(error is IOException || error is InvalidDataException || error is InvalidOperationException || error is JsonException || error is UnauthorizedAccessException) {return;}
            throw new InvalidOperationException("Не отклонено: "+name);
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Учёт времени персонажа: "+reason);}
    }
}
