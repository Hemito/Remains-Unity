using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class ArmorProtectionCatalogChecks
    {
        public static void Run()
        {
            var catalog=ArmorProtectionCatalog.Load();
            foreach(var item in CosmeticCatalog.Current.Definitions)
                if(item.Slot=="armor.body" && item.Id.StartsWith("armor.",StringComparison.Ordinal))
                    Require(catalog.TryGet(item.Id,out _),"Нет защиты текущего комплекта: "+item.Id);
            var metal=catalog.Get("armor.metal");var magus=catalog.Get("armor.magus");var sapper=catalog.Get("armor.sapper");
            Require(metal.Physical==12 && metal.Energy==8 && Near(metal.Quality,.6f) &&
                Near(metal.Resistance(DamageTypes.FromOriginal(9)),-.3f),"Металлическая броня потеряла базовую ступень или слабость к электричеству.");
            Require(magus.Physical==8 && magus.Energy==14 && Near(magus.Resistance(DamageTypes.FromOriginal(5)),.3f) &&
                Near(sapper.Resistance(DamageTypes.FromOriginal(4)),.5f),"Защита мага/сапёра не соответствует источнику.");
            foreach(string id in new[]{"armor.pip","armor.socks"})
            {
                var clothing=catalog.Get(id);
                Require(clothing.Physical==0 && clothing.Energy==0 && clothing.Quality==0 &&
                    Near(clothing.Resistance(DamageTypes.FromOriginal(19)),-.5f),"Нейтральная одежда получила выдуманную защиту или потеряла source pink weakness: "+id);
            }
            var mutable=new Dictionary<string,float> {{DamageTypes.FromOriginal(0),.2f}};
            var profile=new ArmorProtectionProfile("check:armor",4,3,.6f,mutable);mutable.Clear();
            Require(Near(profile.Resistance(DamageTypes.FromOriginal(0)),.2f),"Профиль ссылается на изменяемый словарь автора.");
            Require(profile.Resistance(DamageTypes.FromOriginal(10))==0,"Отсутствующее сопротивление должно быть явно нейтральным.");
            Reject(()=>new ArmorProtectionProfile("bad",float.NaN,0,0));
            Reject(()=>new ArmorProtectionProfile("bad",0,0,1.1f));
            Reject(()=>new ArmorProtectionProfile("bad",0,0,0,new Dictionary<string,float>{{"missing-type",0}}));
            Reject(()=>new ArmorProtectionProfile("bad",0,0,0,new Dictionary<string,float>{{DamageTypes.FromOriginal(0),1.1f}}));
            string a="{\"schema\":1,\"sourceHash\":\"check\",\"items\":[{\"id\":\"x\",\"physical\":1,\"energy\":2,\"quality\":1,\"resistances\":[]}]}";
            var first=new ArmorProtectionCatalog(a);var second=new ArmorProtectionCatalog(a);
            Require(first.Fingerprint==second.Fingerprint,"Нестабильный отпечаток защиты.");
            Reject(()=>first.Register(profile));
            second=new ArmorProtectionCatalog(a);second.Register(profile);
            Require(first.Fingerprint!=second.Fingerprint,"Модовая защита не входит в отпечаток.");
            Reject(()=>new ArmorProtectionCatalog(a.Replace("\"resistances\":[]","\"resistances\":[{\"type\":\"not-a-type\",\"value\":0}]")));
            Debug.Log("ArmorProtectionCatalogChecks PASS: все комплекты, базовая ступень, различия физической/энергетической защиты, отрицательные сопротивления, нейтральная одежда, неизменяемость профиля, валидация и отпечаток модов.");
        }
        private static bool Near(float a,float b)=>Math.Abs(a-b)<.00001f;
        private static void Require(bool valid,string message)
        {if(!valid)throw new InvalidOperationException("Проверка каталога брони: "+message);}
        private static void Reject(Action action)
        {
            try {action();} catch(InvalidOperationException) {return;}
            throw new InvalidOperationException("Проверка каталога брони: ошибочные данные приняты.");
        }
    }
}
