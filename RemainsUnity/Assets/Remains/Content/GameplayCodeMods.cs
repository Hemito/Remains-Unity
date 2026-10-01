using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Remains.Domain;

namespace Remains.Content
{
    // Code mods run trusted managed code. Compose before creating a session/world.
    public interface IGameplayCodeMod
    {
        GameplayRuleSet Configure(GameplayRuleSet previous);
    }
    [Serializable] public sealed class CodeModEntry
    {public int apiVersion;public string assembly,entryType,replacesRulesFrom;}
    public sealed class LoadedCodeMod
    {
        public string id;public int position;public CodeModEntry entry;public byte[] assembly;
    }
    public static class GameplayCodeMods
    {
        public const int ApiVersion=1;
        public static GameplayRuleSet Compose(IEnumerable<LoadedCodeMod> mods)
        {
            var rules=GameplayRuleSet.CreateDefault();string source="base";
            foreach(var mod in mods)
            {
                try
                {
                    if(mod.entry.apiVersion!=ApiVersion) throw new InvalidDataException($"API {mod.entry.apiVersion} не поддерживается, ожидается {ApiVersion}.");
                    if(mod.entry.replacesRulesFrom!=source)
                        throw new InvalidDataException($"Конфликт правил: текущий источник={source}, объявлен={mod.entry.replacesRulesFrom}.");
                    var assembly=Assembly.Load(mod.assembly);
                    var type=assembly.GetType(mod.entry.entryType,true,false);
                    if(!typeof(IGameplayCodeMod).IsAssignableFrom(type) || type.IsAbstract)
                        throw new InvalidDataException($"Тип {mod.entry.entryType} не реализует IGameplayCodeMod.");
                    var instance=(IGameplayCodeMod)Activator.CreateInstance(type);
                    rules=instance.Configure(rules)??throw new InvalidDataException("Кодовый мод вернул пустой набор правил.");
                    source=mod.id;
                }
                catch(Exception failure)
                {throw new InvalidDataException($"Ошибка кодового мода: позиция={mod.position}, id={mod.id}, assembly={mod.entry.assembly}, type={mod.entry.entryType}. {failure.Message}",failure);}
            }
            return rules;
        }
    }
}
