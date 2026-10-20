using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class GameplayContentChecks
    {
        public static void Run()
        {
            CheckComparisonRows();
            var legacy=new ContentManifest {fingerprint="weapons"};
            legacy.packs.Add(new ContentPackRecord {id="base",version="1",fingerprint="weapons"});
            var inputs=new Dictionary<string,string> {{"room","geometry"},{"anatomy","regions"},{"pose","frames"}};
            var original=GameplayContentManifest.Compose(legacy,inputs);
            Require(original.fingerprint==GameplayContentManifest.Compose(legacy,inputs.Reverse()).fingerprint,"Порядок обхода ресурсов меняет отпечаток");
            foreach(string id in inputs.Keys.ToArray())
            {
                var changed=new Dictionary<string,string>(inputs);changed[id]+="changed";
                var other=GameplayContentManifest.Compose(legacy,changed);
                Require(other.fingerprint!=original.fingerprint,"Изменение ресурса не обнаружено: "+id);
                var rows=ContentComparison.Compare(original,other);
                Require(rows.Any(r=>r.id=="Ресурс: "+id),"Нет строки изменённого ресурса: "+id);
            }
            Require(ContentComparison.Compare(legacy,original).Count>=4,"Старый манифест принят без сравнения ресурсов");
            Require(legacy.scope=="weapon-catalog" && legacy.gameplayAssets.Count==0,"Исходный манифест изменён");
            Require(ContentComparison.Compare(original,StateJson.Copy(original)).Count==0,"Копирование потеряло ресурсный манифест");
            Require(WeaponCatalog.ManifestCopy().gameplayAssets.Any(a=>a.id=="Content/actors"),"Рабочий каталог не включает определения актёров");
            Require(WeaponCatalog.ManifestCopy().gameplayAssets.Any(a=>a.id=="Rules/factory-enemy-scaling"),"Формулы уровня противников отсутствуют в игровом fingerprint");
            Require(WeaponCatalog.ManifestCopy().gameplayAssets.Any(a=>a.id=="Original/Pony/motion-frames"),"Рабочий каталог не включает кривые поз, влияющие на области попадания");
        }
        public static void CheckComparisonRows()
        {
            var saved=new ContentManifest {fingerprint="saved"};
            saved.packs.Add(new ContentPackRecord {id="base",version="1",fingerprint="base-hash"});
            saved.packs.Add(new ContentPackRecord {id="early",version="1",fingerprint="early-hash"});
            saved.packs.Add(new ContentPackRecord {id="late",version="1",fingerprint="late-hash"});
            saved.gameplayAssets.Add(new ContentAssetRecord {id="same",fingerprint="same-hash"});
            saved.gameplayAssets.Add(new ContentAssetRecord {id="changed",fingerprint="old-hash"});
            saved.extensions=new Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"mod:unknown","preserved"}};
            var current=saved.Copy();
            string originalSource=StateJson.ToJson(saved),currentSource=StateJson.ToJson(current);
            Require(ContentComparison.Compare(saved,current).Count==0,"Идентичный контент требует согласия");
            Require(ContentComparison.Compare(saved,current,true).Select(row=>row.id).SequenceEqual(new[]{"Ресурс: same","Ресурс: changed","base","early","late"}),
                "Полное сравнение потеряло неизменившиеся строки или их исходный порядок");
            Require(StateJson.ToJson(saved)==originalSource && StateJson.ToJson(current)==currentSource,"Идентичное сравнение изменило исходные данные");

            current.fingerprint="current";
            foreach(bool includeUnchanged in new[]{false,true})
            {
                var rows=ContentComparison.Compare(saved,current,includeUnchanged);
                Require(rows.Count==(includeUnchanged?6:1) && rows[0].id=="Общий отпечаток" &&
                    rows[0].saved=="saved" && rows[0].current=="current",
                    "Различие только общего отпечатка скрыто неизменившимися строками");
            }

            current.gameplayAssets[1].fingerprint="new-hash";
            current.packs[2].version="2";
            currentSource=StateJson.ToJson(current);
            var compact=ContentComparison.Compare(saved,current);
            var complete=ContentComparison.Compare(saved,current,true);
            Require(compact.Select(row=>row.id).SequenceEqual(new[]{"Ресурс: changed","late"}) &&
                complete.Select(row=>row.id).SequenceEqual(new[]{"Ресурс: changed","late","Ресурс: same","base","early"}),
                "Различия позднего ресурса/мода не показаны перед неизменившимися строками");
            Require(complete[0].saved=="old-hash" && complete[0].current=="new-hash" &&
                complete[1].saved=="#3 · версия 1\nlate-hash" && complete[1].current=="#3 · версия 2\nlate-hash",
                "Приоритет строк потерял исходные значения или реальную позицию загрузки мода");
            Require(StateJson.ToJson(saved)==originalSource && StateJson.ToJson(current)==currentSource,
                "Сравнение изменило манифест, порядок загрузки или неизвестные поля");

            current=saved.Copy();current.packs.Reverse();
            currentSource=StateJson.ToJson(current);
            complete=ContentComparison.Compare(saved,current,true);
            Require(complete.Select(row=>row.id).SequenceEqual(new[]{"base","late","Ресурс: same","Ресурс: changed","early"}) &&
                complete[0].saved.StartsWith("#1 ·") && complete[0].current.StartsWith("#3 ·") &&
                complete[1].saved.StartsWith("#3 ·") && complete[1].current.StartsWith("#1 ·"),
                "Изменение порядка модов скрыто либо таблица подменила реальные позиции");
            Require(StateJson.ToJson(saved)==originalSource && StateJson.ToJson(current)==currentSource,
                "Приоритет строк переупорядочил моды в исходном манифесте");

            current=saved.Copy();current.gameplayAssets.RemoveAt(1);
            current.gameplayAssets.Add(new ContentAssetRecord {id="added",fingerprint="added-hash"});
            current.packs.RemoveAt(2);current.packs.Add(new ContentPackRecord {id="added-mod",version="3",fingerprint="added-mod-hash"});
            complete=ContentComparison.Compare(saved,current,true);
            Require(complete.Select(row=>row.id).SequenceEqual(new[]{"Ресурс: changed","Ресурс: added","late","added-mod","Ресурс: same","base","early"}) &&
                complete[0].current=="Отсутствует" && complete[1].saved=="Не записан" &&
                complete[2].current=="Отсутствует" && complete[3].saved=="Отсутствует",
                "Добавленные/удалённые ресурсы и моды потеряны или скрыты за неизменившимися строками");
        }
        private static void Require(bool valid,string detail)
        {if(!valid) throw new InvalidOperationException(detail);}
    }
}
