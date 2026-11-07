using System;
using System.Text.RegularExpressions;

namespace Remains.Domain
{
    public static class CharacterName
    {
        public static bool IsProfileName(string name)=>!string.IsNullOrWhiteSpace(name) &&
            Regex.IsMatch(name,@"\A[\p{L}\p{N}_ -]{1,32}\z");
        public static void ValidateNew(string name)
        {
            if(!IsProfileName(name) || name!=name.Trim())
                throw new ArgumentException("Имя: от 1 до 32 букв, цифр, пробелов, дефисов или подчёркиваний, без пробелов по краям.");
            if(Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
                throw new ArgumentException("Это имя зарезервировано системой. Выберите другое.");
        }
    }
}
