using System;
using System.Text.RegularExpressions;

namespace Remains.Domain
{
    public static class CharacterName
    {
        public static void ValidateNew(string name)
        {
            if(string.IsNullOrWhiteSpace(name) || name!=name.Trim() || !Regex.IsMatch(name,@"^[\p{L}\p{N}_ -]{1,32}$"))
                throw new ArgumentException("Имя: от 1 до 32 букв, цифр, пробелов, дефисов или подчёркиваний, без пробелов по краям.");
            if(Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
                throw new ArgumentException("Это имя зарезервировано системой. Выберите другое.");
        }
    }
}
