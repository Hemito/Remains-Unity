using System;
using System.Collections.Generic;

namespace Remains.RoomFormat
{
    public static class RoomSerializer
    {
        // One decor/object record per line keeps diffs and hand edits local.
        private static readonly string[] RecordArrays = { "decor", "objects" };

        public static string Write(RoomFile room) => JsonText.Write(ToJson(room), 3, RecordArrays);

        public static RoomFile Read(string text, string source) => FromJson(JsonText.Parse(text, source), source);

        public static JsonValue ToJson(RoomFile room)
        {
            var root = JsonValue.NewObject();
            root.Set("format", JsonValue.Of(RoomFile.FormatName));
            root.Set("version", JsonValue.Of(RoomFile.CurrentVersion));
            root.Set("template", JsonValue.Of(room.Template));
            root.Set("size", JsonValue.NewObject().Set("w", JsonValue.Of(room.Width)).Set("h", JsonValue.Of(room.Height)));
            root.Set("cell", JsonValue.Of(room.Cell));

            var palettes = JsonValue.NewObject();
            foreach (var group in room.Palettes)
            {
                if (group.Value.Count == 0) continue;
                var palette = JsonValue.NewObject();
                foreach (var entry in group.Value) palette.Set(entry.Key.ToString(), JsonValue.Of(entry.Value));
                palettes.Set(group.Key, palette);
            }
            root.Set("palette", palettes);

            var layers = JsonValue.NewObject();
            foreach (string name in RoomLayers.All)
            {
                if (!room.Layers.TryGetValue(name, out var rows)) continue;
                var array = JsonValue.NewArray();
                foreach (string row in rows) array.Add(JsonValue.Of(row));
                layers.Set(name, array);
            }
            root.Set("layers", layers);

            var decor = JsonValue.NewArray();
            foreach (var item in room.Decor)
            {
                var value = JsonValue.NewObject();
                if (item.Id != null) value.Set("id", JsonValue.Of(item.Id));
                value.Set("type", JsonValue.Of(item.Type)).Set("x", JsonValue.Of(item.X)).Set("y", JsonValue.Of(item.Y));
                if (item.Params.Count > 0) value.Set("params", Map(item.Params));
                decor.Add(value);
            }
            root.Set("decor", decor);

            var objects = JsonValue.NewArray();
            foreach (var item in room.Objects)
            {
                var value = JsonValue.NewObject();
                value.Set("id", JsonValue.Of(item.Id));
                value.Set("type", JsonValue.Of(item.Type));
                value.Set("x", JsonValue.Of(item.X)).Set("y", JsonValue.Of(item.Y));
                if (item.Code != null) value.Set("code", JsonValue.Of(item.Code));
                if (item.Params.Count > 0) value.Set("params", Map(item.Params));
                if (item.Items.Count > 0) value.Set("items", Maps(item.Items));
                if (item.Links.Count > 0) value.Set("links", Maps(item.Links));
                if (item.Extra != null) value.Set("extra", item.Extra);
                objects.Add(value);
            }
            root.Set("objects", objects);

            var connectors = JsonValue.NewArray();
            foreach (int value in room.Connectors) connectors.Add(JsonValue.Of(value));
            root.Set("connectors", connectors);
            root.Set("options", Map(room.Options));
            if (room.Extra != null) root.Set("extra", room.Extra);
            if (room.Source != null)
                root.Set("source", JsonValue.NewObject()
                    .Set("file", JsonValue.Of(room.Source.File)).Set("room", JsonValue.Of(room.Source.Room))
                    .Set("mapX", JsonValue.Of(room.Source.MapX)).Set("mapY", JsonValue.Of(room.Source.MapY))
                    .Set("sha256", JsonValue.Of(room.Source.Sha256)));
            return root;
        }

        public static RoomFile FromJson(JsonValue root, string source)
        {
            string ctx = source ?? "<room>";
            if (root.Kind != JsonKind.Object) throw new FormatException(ctx + ": корень файла комнаты должен быть объектом.");
            string format = Required(root, "format", ctx).AsString(ctx + ".format");
            if (format != RoomFile.FormatName) throw new FormatException($"{ctx}: неизвестный формат \"{format}\".");
            long version = Required(root, "version", ctx).AsLong(ctx + ".version");
            if (version != RoomFile.CurrentVersion) throw new FormatException($"{ctx}: неподдерживаемая версия формата {version}, ожидалась {RoomFile.CurrentVersion}.");

            var room = new RoomFile { Template = Required(root, "template", ctx).AsString(ctx + ".template") };
            var size = Required(root, "size", ctx);
            room.Width = checked((int)Required(size, "w", ctx + ".size").AsLong(ctx + ".size.w"));
            room.Height = checked((int)Required(size, "h", ctx + ".size").AsLong(ctx + ".size.h"));
            room.Cell = Required(root, "cell", ctx).AsDouble(ctx + ".cell");

            foreach (var group in Fields(Required(root, "palette", ctx), ctx + ".palette"))
                foreach (var entry in Fields(group.Value, ctx + ".palette." + group.Key))
                {
                    if (entry.Key.Length != 1) throw new FormatException($"{ctx}: ключ палитры \"{entry.Key}\" в группе {group.Key} должен быть одним символом.");
                    room.Palette(group.Key).Add(entry.Key[0], entry.Value.AsString(ctx + ".palette." + group.Key + "." + entry.Key));
                }

            foreach (var layer in Fields(Required(root, "layers", ctx), ctx + ".layers"))
            {
                if (!RoomLayers.IsKnown(layer.Key)) throw new FormatException($"{ctx}: неизвестный слой \"{layer.Key}\".");
                var rows = Array(layer.Value, ctx + ".layers." + layer.Key);
                var result = new string[rows.Count];
                for (int i = 0; i < rows.Count; i++) result[i] = rows[i].AsString($"{ctx}.layers.{layer.Key}[{i}]");
                room.Layers.Add(layer.Key, result);
            }

            foreach (var item in Array(Required(root, "decor", ctx), ctx + ".decor"))
            {
                var decor = new RoomDecor
                {
                    Id = item.Get("id")?.AsString(ctx + ".decor.id"),
                    Type = Required(item, "type", ctx + ".decor").AsString(ctx + ".decor.type"),
                    X = checked((int)Required(item, "x", ctx + ".decor").AsLong(ctx + ".decor.x")),
                    Y = checked((int)Required(item, "y", ctx + ".decor").AsLong(ctx + ".decor.y")),
                };
                ReadMap(item.Get("params"), decor.Params, ctx + ".decor.params");
                room.Decor.Add(decor);
            }

            foreach (var item in Array(Required(root, "objects", ctx), ctx + ".objects"))
            {
                var id = Required(item, "id", ctx + ".objects");
                var obj = new RoomObject
                {
                    Id = id.Kind == JsonKind.Null ? null : id.AsString(ctx + ".objects.id"),
                    Type = Required(item, "type", ctx + ".objects").AsString(ctx + ".objects.type"),
                    X = checked((int)Required(item, "x", ctx + ".objects").AsLong(ctx + ".objects.x")),
                    Y = checked((int)Required(item, "y", ctx + ".objects").AsLong(ctx + ".objects.y")),
                    Code = item.Get("code")?.AsString(ctx + ".objects.code"),
                    Extra = item.Get("extra"),
                };
                ReadMap(item.Get("params"), obj.Params, ctx + ".objects.params");
                ReadMaps(item.Get("items"), obj.Items, ctx + ".objects.items");
                ReadMaps(item.Get("links"), obj.Links, ctx + ".objects.links");
                room.Objects.Add(obj);
            }

            var connectors = Array(Required(root, "connectors", ctx), ctx + ".connectors");
            room.Connectors = new int[connectors.Count];
            for (int i = 0; i < connectors.Count; i++) room.Connectors[i] = checked((int)connectors[i].AsLong($"{ctx}.connectors[{i}]"));
            ReadMap(Required(root, "options", ctx), room.Options, ctx + ".options");
            room.Extra = root.Get("extra");

            var src = root.Get("source");
            if (src != null)
                room.Source = new RoomSource
                {
                    File = Required(src, "file", ctx + ".source").AsString(ctx + ".source.file"),
                    Room = Required(src, "room", ctx + ".source").AsString(ctx + ".source.room"),
                    MapX = checked((int)Required(src, "mapX", ctx + ".source").AsLong(ctx + ".source.mapX")),
                    MapY = checked((int)Required(src, "mapY", ctx + ".source").AsLong(ctx + ".source.mapY")),
                    Sha256 = Required(src, "sha256", ctx + ".source").AsString(ctx + ".source.sha256"),
                };
            return room;
        }

        private static JsonValue Map(SortedDictionary<string, string> map)
        {
            var value = JsonValue.NewObject();
            foreach (var entry in map) value.Set(entry.Key, JsonValue.Of(entry.Value));
            return value;
        }

        private static JsonValue Maps(List<SortedDictionary<string, string>> maps)
        {
            var array = JsonValue.NewArray();
            foreach (var map in maps) array.Add(Map(map));
            return array;
        }

        private static void ReadMap(JsonValue value, SortedDictionary<string, string> target, string ctx)
        {
            if (value == null) return;
            foreach (var field in Fields(value, ctx)) target.Add(field.Key, field.Value.AsString(ctx + "." + field.Key));
        }

        private static void ReadMaps(JsonValue value, List<SortedDictionary<string, string>> target, string ctx)
        {
            if (value == null) return;
            foreach (var item in Array(value, ctx))
            {
                var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
                ReadMap(item, map, ctx);
                target.Add(map);
            }
        }

        private static JsonValue Required(JsonValue parent, string key, string ctx)
        {
            if (parent.Kind != JsonKind.Object) throw new FormatException(ctx + ": ожидался объект.");
            return parent.Get(key) ?? throw new FormatException($"{ctx}: нет обязательного поля \"{key}\".");
        }

        private static List<KeyValuePair<string, JsonValue>> Fields(JsonValue value, string ctx)
        {
            if (value.Kind != JsonKind.Object) throw new FormatException(ctx + ": ожидался объект.");
            return value.Fields;
        }

        private static List<JsonValue> Array(JsonValue value, string ctx)
        {
            if (value.Kind != JsonKind.Array) throw new FormatException(ctx + ": ожидался массив.");
            return value.Items;
        }
    }
}
