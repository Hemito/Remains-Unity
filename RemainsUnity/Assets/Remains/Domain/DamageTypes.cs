using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.Domain
{
    // Stable content IDs, independent of the original numeric encoding and presentation effects.
    public static class DamageTypes
    {
        public const string Bullet="bullet",Blade="blade",Physical="physical",Fire="fire",Explosion="explosion",
            Laser="laser",Plasma="plasma",Venom="venom",Emp="emp",Electric="electric",Acid="acid",Cryo="cryo",
            Poison="poison",Bleed="bleed",Fang="fang",Balefire="balefire",Necrotic="necrotic",Psychic="psychic",
            Astral="astral",Pink="pink",Internal="internal",Friendly="friendly";
        private static readonly Dictionary<string,(DamageArmorChannel channel,string name)> definitions=new(StringComparer.Ordinal);
        private static bool frozen;
        private static string fingerprint;
        static DamageTypes()
        {
            for(int i=0;i<20;i++) AddBuiltin(FromOriginal(i));
            AddBuiltin(Internal);AddBuiltin(Friendly);
        }
        public static void Register(string id,DamageArmorChannel channel,string russianName)
        {
            if(frozen) throw new InvalidOperationException("Типы урона уже используются; регистрацию выполняйте при подготовке контента.");
            if(string.IsNullOrWhiteSpace(id) || id!=id.Trim() || string.IsNullOrWhiteSpace(russianName) || !Enum.IsDefined(typeof(DamageArmorChannel),channel) ||
                !definitions.TryAdd(id,(channel,russianName))) throw new InvalidOperationException("Некорректный или повторный тип урона: "+id+".");
        }
        public static string Fingerprint
        {
            get
            {
                if(!frozen)
                {
                    var text=new System.Text.StringBuilder("damage-types-v1");
                    foreach(var pair in definitions.OrderBy(pair=>pair.Key,StringComparer.Ordinal))
                        text.Append('|').Append(pair.Key.Length).Append(':').Append(pair.Key).Append(':').Append((int)pair.Value.channel)
                            .Append(':').Append(pair.Value.name.Length).Append(':').Append(pair.Value.name);
                    fingerprint=Remains.Persistence.RecoverySnapshot.Fingerprint(text.ToString());frozen=true;
                }
                return fingerprint;
            }
        }
        public static string FromOriginal(int type)=>type switch
        {
            0=>Bullet,1=>Blade,2=>Physical,3=>Fire,4=>Explosion,5=>Laser,6=>Plasma,7=>Venom,8=>Emp,9=>Electric,
            10=>Acid,11=>Cryo,12=>Poison,13=>Bleed,14=>Fang,15=>Balefire,16=>Necrotic,17=>Psychic,18=>Astral,
            19=>Pink,100=>Internal,101=>Friendly,
            _=>throw new InvalidOperationException("Неизвестный исходный тип урона: tipdam="+type+".")
        };
        public static void Validate(string type)
        {
            if(type==null || !definitions.ContainsKey(type))
                throw new InvalidOperationException("Неизвестный тип урона: type="+(type??"<null>")+".");
        }
        public static DamageArmorChannel Channel(string type)
        {
            Validate(type);
            return definitions[type].channel;
        }
        private static void AddBuiltin(string type)
        {
            DamageArmorChannel channel=type switch
            {
                Bullet or Blade or Physical or Explosion or Fang or Acid=>DamageArmorChannel.Physical,
                Fire or Laser or Plasma or Electric or Cryo or Astral=>DamageArmorChannel.Energy,
                _=>DamageArmorChannel.None
            };
            Register(type,channel,BuiltinName(type));
        }
        public static string Name(string type)
        {
            Validate(type);return definitions[type].name;
        }
        public static string Of(AttackEvent attack)
        {
            if(attack==null) throw new ArgumentNullException(nameof(attack));
            string type=NpcNaturalAttack.IsNatural(attack)?NpcNaturalAttack.Definition(attack.naturalAttackType).damageType:
                Remains.Content.WeaponCatalog.Get(attack.weapon).damageType;
            Validate(type);return type;
        }
        private static string BuiltinName(string type)
        {
            return type switch
            {
                Bullet=>"Пулевой",Blade=>"Режущий",Physical=>"Ударный",Fire=>"Огонь",Explosion=>"Взрыв",
                Laser=>"Лазер",Plasma=>"Плазма",Venom=>"Токсин",Emp=>"ЭМИ",Electric=>"Электричество",
                Acid=>"Кислота",Cryo=>"Холод",Poison=>"Отравление",Bleed=>"Кровотечение",Fang=>"Укус",
                Balefire=>"Балефайр",Necrotic=>"Некротический",Psychic=>"Психический",Astral=>"Астральный",
                Pink=>"Розовый",Internal=>"Внутренний",Friendly=>"Дружественный",
                _=>throw new InvalidOperationException("Нет названия типа урона: "+type)
            };
        }
    }
    public enum DamageArmorChannel {None,Physical,Energy}
}
