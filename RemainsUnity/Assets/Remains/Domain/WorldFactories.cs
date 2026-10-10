using System;
using System.Globalization;
using System.IO;
using Remains.RoomFormat;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable] public sealed class WorldFactory : ExtensibleState
    {
        public string level;
        public string definition;
        public string contentFingerprint;
        // Stored JSON is the source of truth. Reading never rewrites it or discards fields.
        public string layout;
    }

    public static class WorldFactories
    {
        public static string Level(int index) => "factory:" + index.ToString(CultureInfo.InvariantCulture);
        public static ulong Seed(WorldState world) => unchecked((ulong)(long)world.seed);

        // Explicit creation only. Loading an old world must never call this as a missing-data fallback.
        public static WorldFactory Add(WorldState world, FactoryLayout selection, string definition, string contentFingerprint)
        {
            if (world == null || selection == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(selection));
            string level = Level(selection.Index);
            if (Find(world, level) != null) throw new InvalidOperationException($"Мир {world.worldId}: завод {level} уже имеет сохранённую раскладку.");
            // The room binding and its stored factory must describe the same level. Never replace
            // a legacy authored factory as an implicit migration.
            WorldRooms.Definition(world,"hub"); // validates all existing bindings
            var binding=world.rooms.Find(room=>room.level==level);
            if(binding!=null && binding.definition!=definition)
                throw new InvalidDataException($"Мир {world.worldId}: {level} уже связан с {binding.definition}; замена сохранённой локации запрещена.");
            var record = new WorldFactory { level = level, definition = definition, contentFingerprint = contentFingerprint, layout = selection.Write() };
            Read(world, record, definition, contentFingerprint);
            if(binding==null) world.rooms.Add(new WorldRoom{level=level,definition=definition});
            world.factories.Add(record);
            return record;
        }

        public static WorldFactory Find(WorldState world, string level)
        {
            if (world?.factories == null) throw new InvalidDataException($"Мир {world?.worldId}: отсутствует список заводов.");
            WorldFactory result = null;
            var levels = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var factory in world.factories)
            {
                if (factory == null || string.IsNullOrWhiteSpace(factory.level) || !levels.Add(factory.level))
                    throw new InvalidDataException($"Мир {world.worldId}: пустой или повторный завод {factory?.level}.");
                if (factory.level == level) result = factory;
            }
            return result;
        }

        public static FactoryLayout Require(WorldState world, string level, string definition, string contentFingerprint)
        {
            var record = Find(world, level) ?? throw new InvalidDataException($"Мир {world.worldId}: нет сохранённой раскладки {level}; автоматическая генерация при загрузке запрещена.");
            if(WorldRooms.Definition(world,level)!=record.definition)
                throw new InvalidDataException($"Мир {world.worldId}: привязка локации {level} не совпадает с определением завода {record.definition}.");
            return Read(world, record, definition, contentFingerprint);
        }

        private static FactoryLayout Read(WorldState world, WorldFactory record, string definition, string contentFingerprint)
        {
            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(contentFingerprint) ||
                record.definition != definition || record.contentFingerprint != contentFingerprint || string.IsNullOrWhiteSpace(record.layout))
                throw new InvalidDataException($"Мир {world.worldId}, завод {record.level}: определение или версия контента не совпадает с сохранением.");
            var selection = FactoryLayoutReader.Read(record.layout, $"Мир {world.worldId}, завод {record.level}");
            if (record.level != Level(selection.Index) || selection.Seed != Seed(world) || selection.GeneratorVersion != FactoryLayoutGenerator.GeneratorVersion)
                throw new InvalidDataException($"Мир {world.worldId}, завод {record.level}: номер, seed или версия генератора раскладки не совпадает (index={selection.Index}, seed={selection.Seed}, generator={selection.GeneratorVersion}).");
            return selection;
        }
    }
}
