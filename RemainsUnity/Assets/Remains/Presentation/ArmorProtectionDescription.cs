using System.Text;
using Remains.Content;
using Remains.Domain;

namespace Remains.Presentation
{
    public static class ArmorProtectionDescription
    {
        public static string For(string definition)
        {
            if(!ArmorProtectionCatalog.Current.TryGet(definition,out var profile)) return "";
            var text=new StringBuilder("\n\nЗащита при надевании · базовая ступень\n");
            text.Append($"Поглощение физического урона: {profile.Physical:0.##}\nПоглощение энергетического урона: {profile.Energy:0.##}\n");
            text.Append($"Шанс поглощения: {profile.Quality*100:0.#}%\n");
            foreach(var pair in profile.Resistances)
            {
                if(pair.Value==0) continue;
                text.Append(DamageTypes.Name(pair.Key)).Append(": ");
                text.Append(pair.Value>0?$"сопротивление {pair.Value*100:0.#}%":$"уязвимость {-pair.Value*100:0.#}%").Append('\n');
            }
            return text.ToString().TrimEnd();
        }
    }
}
