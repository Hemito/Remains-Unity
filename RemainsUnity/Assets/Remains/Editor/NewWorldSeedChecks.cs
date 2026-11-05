using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.RoomFormat;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class NewWorldSeedChecks
    {
        [Serializable] private sealed class Report
        {
            public bool success,explicitSeed104,savedLayoutUnchanged,legacySeedUnchanged;
            public int[] seeds;
            public int distinctSeeds,distinctRoomArrangements;
        }
        public static void RunBatch()
        {
            try
            {
                var report=new Report();var seeds=new List<int>();var arrangements=new HashSet<string>(StringComparer.Ordinal);
                var catalog=FactoryCatalogAsset.Load();
                var fixedWorld=NewGameWorld.Create(104);string saved=fixedWorld.factories.Single().layout;
                Require(saved==NewGameWorld.Create(104).factories.Single().layout,"Явный seed104 перестал давать прежнюю детерминированную раскладку.");
                var fixture=new WorldState{seed=104};FactoryPreparation.Create(fixture,catalog,0);
                Require(saved==fixture.factories.Single().layout,"Новый явный seed104 отличается от существующего способа создания fixture.");report.explicitSeed104=true;
                for(int i=0;i<8;i++)
                {
                    var world=NewGameWorld.Create();seeds.Add(world.seed);
                    var layout=WorldFactories.Require(world,"factory:0",FactoryCatalogAsset.DefinitionId,catalog.ContentFingerprint);
                    Require(layout.Seed==WorldFactories.Seed(world),"Раскладка не хранит выбранный seed нового мира.");
                    arrangements.Add(string.Join("|",layout.Cells.Select(cell=>$"{cell.Template}:{cell.Mirror}:{cell.Back}")));
                    string text=world.factories.Single().layout;
                    var restored=StateJson.FromJson<WorldState>(StateJson.ToJson(world));
                    FactoryPreparation.Prepare(restored,catalog,"factory:0");
                    // Changed content does not lock a saved world out (user 2026-11-05): the layout stays, the version moves on.
                    var older=StateJson.FromJson<WorldState>(StateJson.ToJson(world));older.factories.Single().contentFingerprint="прежний контент";
                    FactoryPreparation.Prepare(older,catalog,"factory:0");
                    Require(older.factories.Single().layout==text && older.factories.Single().contentFingerprint==catalog.ContentFingerprint,"Мир с прежней версией контента не загрузился со своей раскладкой.");
                    Require(restored.seed==world.seed && restored.factories.Single().layout==text && restored.worldId==world.worldId,
                        "Восстановление мира/сетки заменило seed, ID или сохранённую раскладку.");
                }
                report.seeds=seeds.ToArray();report.distinctSeeds=seeds.Distinct().Count();report.distinctRoomArrangements=arrangements.Count;
                // Random seeds may collide; the check rejects a fixed default, not a legitimate individual collision.
                Require(report.distinctSeeds>1 && arrangements.Count>1,"Новые миры по-прежнему используют один seed или одну раскладку комнат.");
                var old=StateJson.FromJson<WorldState>(StateJson.ToJson(fixedWorld));
                FactoryPreparation.Prepare(old,catalog,"factory:0");
                Require(old.seed==104 && old.factories.Single().layout==saved,"Создание других миров изменило сохранённый seed104.");report.savedLayoutUnchanged=true;
                var legacy=StateJson.FromJson<WorldState>("{\"schema\":1,\"worldId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");
                Require(legacy.seed==104 && legacy.factories.Count==0 && WorldRooms.Definition(legacy,"factory:0")=="base:factory-prototype",
                    "Старое сохранение без seed/раскладки случайно мигрировало на новый завод.");report.legacySeedUnchanged=true;
                report.success=true;string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/small-fixes-20261105/new-world-seeds.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
                Debug.Log($"PT-20261103-10 PASS: {report.distinctSeeds}/8 seed, {arrangements.Count}/8 раскладок; сохранения, явный seed104 и прежняя legacy-комната не меняются.");
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
