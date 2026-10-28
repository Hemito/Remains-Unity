using System;
using Remains.Content;

namespace Remains.Domain
{
    public static class EquippedArmorProtection
    {
        public const string Slot="armor.body";
        // Resolve the same canonical selection as the renderer. No cached copy of equipment statistics on the actor.
        public static ArmorProtectionProfile Profile(ActorState actor,ArmorProtectionCatalog armors=null,CosmeticCatalog cosmetics=null)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            var state=actor.cosmeticEquipment;
            if(state==null) return null;
            CosmeticEquipment.Validate(actor);
            if(state.version!=CosmeticEquipment.Version) return null; // Validate reports the unsupported format.
            foreach(var selection in state.entries)
            {
                if(selection.slot!=Slot || selection.item==null) continue;
                if(!CosmeticEquipment.TryResolve(actor,selection,out var definition,out _,cosmetics)) return null;
                // A visual capability need not declare gameplay protection (for example a modded costume).
                (armors??ArmorProtectionCatalog.Current).TryGet(definition.Id,out var profile);
                return profile;
            }
            return null;
        }
        public static float Resolve(ArmorProtectionProfile armor,string type,float incoming,float intrinsic,string attackId,string targetId)
        {
            if(!float.IsFinite(incoming) || incoming<0 || !float.IsFinite(intrinsic) || intrinsic<0)
                throw new InvalidOperationException($"Некорректный расчёт защиты: actor={targetId}, attack={attackId}, damage={incoming}, intrinsic={intrinsic}.");
            var channel=DamageTypes.Channel(type);
            float damage=incoming*(1-(armor?.Resistance(type)??0));
            if(channel==DamageArmorChannel.None) return damage;
            float protection=intrinsic;
            if(armor!=null && Covered(armor.Quality,attackId,targetId))
                protection+=channel==DamageArmorChannel.Physical?armor.Physical:armor.Energy;
            return Math.Max(0,damage-protection);
        }
        // Source qual is a per-hit probability. A local stable roll survives duplicate delivery, reload and owner change;
        // it neither consumes gameplay RNG nor depends on frame/time, string.GetHashCode or process hash seeds.
        public static bool Covered(float quality,string attackId,string targetId)
        {
            if(!float.IsFinite(quality) || quality<0 || quality>1) throw new ArgumentOutOfRangeException(nameof(quality));
            if(string.IsNullOrWhiteSpace(attackId) || string.IsNullOrWhiteSpace(targetId))
                throw new InvalidOperationException("Для покрытия брони нужны ID атаки и цели.");
            if(quality==0) return false;if(quality==1) return true;
            uint hash=2166136261;
            Add("armor-coverage-v1");Add(attackId);Add(targetId);
            // Avalanche the sequential IDs used by melee contacts and projectile pellets.
            unchecked {hash^=hash>>16;hash*=0x7feb352d;hash^=hash>>15;hash*=0x846ca68b;hash^=hash>>16;}
            return (hash>>8)*(1d/16777216d)<quality;
            void Add(string text)
            {
                unchecked
                {
                    // Length prefix prevents concatenation ambiguities even for mod IDs containing separators.
                    hash=(hash^(uint)text.Length)*16777619;
                    foreach(char value in text) hash=(hash^value)*16777619;
                }
            }
        }
    }
}
