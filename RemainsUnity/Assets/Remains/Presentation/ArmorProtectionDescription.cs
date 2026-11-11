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
            var text=new StringBuilder(Remains.Content.GameLocalization.UiText("source.ArmorProtectionDescription.5fbd94e18ebc1ea7", "\n\nЗащита при надевании · базовая ступень\n", "RemainsUnity/Assets/Remains/Presentation/ArmorProtectionDescription.cs"));
            text.Append(Remains.Content.GameLocalization.Format("source.ArmorProtectionDescription.ca49c8eaff8d6374", $"Поглощение физического урона: {profile.Physical:0.##}\nПоглощение энергетического урона: {profile.Energy:0.##}\n", "RemainsUnity/Assets/Remains/Presentation/ArmorProtectionDescription.cs"));
            text.Append(Remains.Content.GameLocalization.Format("source.ArmorProtectionDescription.d2db9cb19a006123", $"Шанс поглощения: {profile.Quality*100:0.#}%\n", "RemainsUnity/Assets/Remains/Presentation/ArmorProtectionDescription.cs"));
            foreach(var pair in profile.Resistances)
            {
                if(pair.Value==0) continue;
                text.Append(DamageTypes.Name(pair.Key)).Append(": ");
                text.Append(pair.Value>0?Remains.Content.GameLocalization.Format("source.ArmorProtectionDescription.55b739cfa2cd182a", $"сопротивление {pair.Value*100:0.#}%", "RemainsUnity/Assets/Remains/Presentation/ArmorProtectionDescription.cs"):Remains.Content.GameLocalization.Format("source.ArmorProtectionDescription.9bf2fd94c4aacf41", $"уязвимость {-pair.Value*100:0.#}%", "RemainsUnity/Assets/Remains/Presentation/ArmorProtectionDescription.cs")).Append('\n');
            }
            return text.ToString().TrimEnd();
        }
    }
}
