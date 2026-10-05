using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    public static class PonyAppearance
    {
        private static PonyPaletteAsset palette;
        public static void Prepare() {palette=PonyPaletteAsset.Load();PonyAnimationData.Load();}
        public static PonyAppearanceKey Snapshot(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            var value=actor.appearance;
            if(value==null) return new PonyAppearanceKey(1,1,false,0xA3A3A3,0,0xFFFFFF,0x16F343,0x00FF00,actor.hair);
            ValidateSettings(value,actor.id??actor.character??"предпросмотр");
            return new PonyAppearanceKey(value.hairStyle,value.eyeStyle,value.secondaryHairEnabled,value.fur,value.hair,value.secondaryHair,value.eyes,value.magic);
        }
        // Counts come from the prepared content catalogue, never from a save enum.
        // No Unity asset access; safe for a background profile read after preparation.
        public static void ValidateSettings(CharacterAppearance value,string context)
        {
            if(value==null) return;
            value.Validate(context);
            var library=PonyAnimationData.Library??throw new InvalidOperationException("Каталог внешности не подготовлен.");
            if(value.hairStyle>library.hairStyles || value.eyeStyle>library.eyeStyles)
                throw new InvalidOperationException($"Внешность {context}: формы отсутствуют в каталоге, hair={value.hairStyle}/{library.hairStyles}, eyes={value.eyeStyle}/{library.eyeStyles}.");
        }
        public static Color Tint(string slot,PonyAppearanceKey value)=>value.Legacy?Tint(slot,value.LegacyHair):slot switch
        {
            "fur"=>FromRgb(value.Fur),"hair"=>FromRgb(value.Hair),"hairSecondary"=>FromRgb(value.SecondaryHair),
            "eye"=>FromRgb(value.Eyes),"magic"=>FromRgb(value.Magic),"none"=>Color.white,
            _=>throw new InvalidOperationException($"Неизвестная цветовая область пони: {slot}.")
        };
        public static Color FromRgb(int rgb)=>new(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,1);
        public static int ToRgb(Color color)=>(Mathf.RoundToInt(color.r*255)<<16)|(Mathf.RoundToInt(color.g*255)<<8)|Mathf.RoundToInt(color.b*255);
        public static Color Tint(string slot,int hair)=>slot switch
        {
            "hair"=>(palette!=null?palette:throw new InvalidOperationException("Палитра пони не подготовлена перед отрисовкой.")).Hair(hair),
            "fur"=>new Color(163/255f,163/255f,163/255f),
            "eye"=>new Color(22/255f,243/255f,67/255f),
            "magic"=>Color.green,
            "none" or "hairSecondary"=>Color.white,
            _=>throw new InvalidOperationException($"Неизвестная цветовая область пони: {slot}.")
        };
        // One device on the near foreleg. pip2 is the independent spare view.
        public static bool IsVisible(PonyPart part)=>IsVisible(part,1,1,false);
        public static bool IsVisible(PonyPart part,int hairStyle,int eyeStyle,bool secondary)=>
            !part.path.StartsWith("pip2/") && Matches(part.hairStyle,part.eyeStyle,part.slot,hairStyle,eyeStyle,secondary);
        public static bool Matches(int partHair,int partEye,string slot,int hairStyle,int eyeStyle,bool secondary)=>
            (partHair==0 || partHair==hairStyle) && (partEye==0 || partEye==eyeStyle) && (slot!="hairSecondary" || secondary);
    }
    // Immutable value used for invalidation; actor data is never stored globally.
    public readonly struct PonyAppearanceKey : IEquatable<PonyAppearanceKey>
    {
        public readonly int HairStyle,EyeStyle,Fur,Hair,SecondaryHair,Eyes,Magic;
        public readonly bool SecondaryEnabled;
        public readonly bool Legacy;
        public readonly int LegacyHair;
        public PonyAppearanceKey(int hairStyle,int eyeStyle,bool secondary,int fur,int hair,int secondaryHair,int eyes,int magic,int? legacyHair=null)
        {HairStyle=hairStyle;EyeStyle=eyeStyle;SecondaryEnabled=secondary;Fur=fur;Hair=hair;SecondaryHair=secondaryHair;Eyes=eyes;Magic=magic;Legacy=legacyHair.HasValue;LegacyHair=legacyHair??0;}
        public bool Equals(PonyAppearanceKey other)=>Legacy==other.Legacy && LegacyHair==other.LegacyHair && HairStyle==other.HairStyle && EyeStyle==other.EyeStyle && SecondaryEnabled==other.SecondaryEnabled &&
            Fur==other.Fur && Hair==other.Hair && SecondaryHair==other.SecondaryHair && Eyes==other.Eyes && Magic==other.Magic;
        public override bool Equals(object other)=>other is PonyAppearanceKey value && Equals(value);
        public override int GetHashCode()=>HashCode.Combine(HashCode.Combine(HairStyle,EyeStyle,SecondaryEnabled,Fur,Hair,SecondaryHair,Eyes,Magic),Legacy,LegacyHair);
    }
}
