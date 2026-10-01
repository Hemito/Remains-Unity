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
        }
        private static void Require(bool valid,string detail)
        {if(!valid) throw new InvalidOperationException(detail);}
    }
}
