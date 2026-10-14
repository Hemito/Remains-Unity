using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // SnapshotPartCache: the composed text equals StateJson.ToJson(world); unchanged parts are not parsed again,
    // a changed part is; a part made invalid after it was cached is still rejected (strictness is per text).
    //   batch -executeMethod Remains.Editor.SnapshotPartCacheChecks.RunBatch -recoveryPerfWorld <world.json>
    public static class SnapshotPartCacheChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            string input=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) if(args[i]=="-recoveryPerfWorld") input=args[i+1];
            if(input==null) throw new ArgumentException("Нужен -recoveryPerfWorld <мир.json>.");
            Anatomy.Load();WeaponCatalog.Load();
            var world=StateJson.FromJson<WorldState>(File.ReadAllText(input));
            Require(world.containers.Count>0,"В мире нет контейнеров.");
            SnapshotPartCache.Clear();
            WorldChanges.Track(world);   // the session-world mode: unmarked parts reuse stored text
            var first=SnapshotPartCache.Compose(world);
            Require(first.Json==StateJson.ToJson(world),"Составной текст отличается от обычной сериализации.");
            Require(first.Parsed==first.Parts,$"Первый снимок разобрал {first.Parsed} частей из {first.Parts}.");
            var second=SnapshotPartCache.Compose(world);
            Require(second.Json==first.Json && second.Parsed==0,$"Неизменный мир: разобрано {second.Parsed} частей.");

            var container=world.containers[0];
            container.x+=0.5f;WorldChanges.Mark(world,container);
            var moved=SnapshotPartCache.Compose(world);
            Require(moved.Json==StateJson.ToJson(world) && moved.Parsed==1,$"Сдвиг одного контейнера: разобрано {moved.Parsed}.");

            container.extensions??=new Dictionary<string,JToken>();
            container.extensions["probe"]=new JValue(1e300);   // an unknown number outside Decimal: StateJson rejects it
            WorldChanges.Mark(world,container);
            bool rejected=false;
            try {RecoverySnapshot.FromWorld(world);}
            catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Испорченная часть, бывшая в кэше, не отклонена.");
            container.extensions.Remove("probe");WorldChanges.Mark(world,container);
            var restored=RecoverySnapshot.FromWorld(world);
            Require(restored.Json==StateJson.ToJson(world),"После исправления текст снимка отличается.");
            // Bench: strict parse vs trusted deserialization of the same text (both must give the same state).
            string text=StateJson.ToJson(world);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<3;i++) StateJson.FromJson<WorldState>(text);
            double strict=timer.Elapsed.TotalMilliseconds/3;timer.Restart();
            WorldState trusted=null;
            for(int i=0;i<3;i++) trusted=StateJson.FromTrustedJson<WorldState>(text);
            double fast=timer.Elapsed.TotalMilliseconds/3;
            Debug.Log($"Чтение снимка {text.Length} символов: строгое {strict:F1} мс, доверенное {fast:F1} мс.");
            string again=StateJson.ToJson(trusted);
            if(again!=text)
            {
                int at=0;while(at<Math.Min(again.Length,text.Length) && again[at]==text[at]) at++;
                throw new InvalidOperationException($"Доверенное чтение даёт другое состояние с символа {at}: «{text.Substring(Math.Max(0,at-80),Math.Min(160,text.Length-Math.Max(0,at-80)))}» против «{again.Substring(Math.Max(0,at-80),Math.Min(160,again.Length-Math.Max(0,at-80)))}».");
            }
            // Own snapshot received back (host client): the same object, trusted live copy equal to the text;
            // a different text (another world revision) is a separate strict parse.
            var own=RecoverySnapshot.FromWorld(world);
            var received=RecoverySnapshot.FromReceived(own.Json);   // the same text was also produced as "restored"
            Require(ReferenceEquals(received,own) || ReferenceEquals(received,restored),"Собственный снимок не узнан при приёме.");
            Require(StateJson.ToJson(own.CreateLiveWorld())==own.Json,"Копия мира из собственного снимка отличается от текста.");
            world.revision++;
            string foreign=StateJson.ToJson(world);
            Require(!ReferenceEquals(RecoverySnapshot.FromReceived(foreign),own),"Чужой текст принят как собственный снимок.");
            // An unmarked change: kept out until the next full verification, which reports it and writes the fresh text.
            var silent=world.containers[1];silent.y+=1;
            bool caught=false;
            for(int i=0;i<SnapshotPartCache.VerifyEvery+1 && !caught;i++)
            {
                var composed=SnapshotPartCache.Compose(world);
                caught=SnapshotPartCache.LastUnmarked>0;
                if(caught) Require(composed.Json==StateJson.ToJson(world),"Сверка нашла непомеченное изменение, но текст снимка устарел.");
            }
            Require(caught,"Непомеченное изменение не найдено сверкой.");
            Debug.Log($"Кэш частей снимка: PASS — частей {first.Parts}, повторно разобрано 0, после сдвига 1, испорченная часть отклонена, текст совпадает.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
