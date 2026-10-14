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
            var first=SnapshotPartCache.Compose(world);
            Require(first.Json==StateJson.ToJson(world),"Составной текст отличается от обычной сериализации.");
            Require(first.Parsed==first.Parts,$"Первый снимок разобрал {first.Parsed} частей из {first.Parts}.");
            var second=SnapshotPartCache.Compose(world);
            Require(second.Json==first.Json && second.Parsed==0,$"Неизменный мир: разобрано {second.Parsed} частей.");

            var container=world.containers[0];
            container.x+=0.5f;
            var moved=SnapshotPartCache.Compose(world);
            Require(moved.Json==StateJson.ToJson(world) && moved.Parsed==1,$"Сдвиг одного контейнера: разобрано {moved.Parsed}.");

            container.extensions??=new Dictionary<string,JToken>();
            container.extensions["probe"]=new JValue(1e300);   // an unknown number outside Decimal: StateJson rejects it
            bool rejected=false;
            try {RecoverySnapshot.FromWorld(world);}
            catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Испорченная часть, бывшая в кэше, не отклонена.");
            container.extensions.Remove("probe");
            var restored=RecoverySnapshot.FromWorld(world);
            Require(restored.Json==StateJson.ToJson(world),"После исправления текст снимка отличается.");
            Debug.Log($"Кэш частей снимка: PASS — частей {first.Parts}, повторно разобрано 0, после сдвига 1, испорченная часть отклонена, текст совпадает.");
        }

        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
