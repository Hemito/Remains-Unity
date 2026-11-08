using Remains.RoomFormat;

namespace Remains.RoomEditor;

// Editing commands operate on the game's model. There is no web-specific room format.
public sealed class EditorDocument
{
    // Read-only view for UI callers; all edits must use commands so history and cached text stay coherent.
    public RoomFile Room { get; private set; }
    public RoomDefinitionCatalog Definitions { get; }
    public TileManifest Art { get; }
    readonly Stack<string> undo = new(), redo = new();
    string originalText, originalCanonical, currentCanonical;
    RoomFile clipboard;
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public bool Dirty => Canonical() != originalCanonical;
    public EditorDocument(string definitions, string art)
    {
        Definitions = RoomDefinitionCatalog.Read(definitions, "definitions.json");
        Art = TileManifest.Read(art, "tiles-manifest.json");
        New("my-room", 30, 20);
    }
    public void Open(string text)
    {
        var room = RoomSerializer.Read(text.StartsWith("\ufeff",StringComparison.Ordinal)?text.Substring(1):text, "room.json");
        Guard(room.Width, room.Height);
        var errors = RoomValidator.Validate(room, "room.json");
        if (errors.Count != 0) throw new FormatException(string.Join("\n", errors));
        Room = room; originalText = text; currentCanonical=RoomSerializer.Write(room); originalCanonical = currentCanonical; undo.Clear(); redo.Clear();
    }
    public void New(string template, int width, int height)
    {
        Guard(width, height);
        var room = new RoomFile { Template = template, Width = width, Height = height };
        Open(RoomSerializer.Write(room));
    }
    // Preserve an imported file's bytes even if its whitespace is not canonical.
    public string Save() => !Dirty ? originalText : Canonical();
    string Canonical() => currentCanonical;
    static void Guard(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 4_000_000)
            throw new ArgumentException("Размер должен быть положительным; защитный предел браузера — 4 млн клеток.");
    }
    void Change(Action<RoomFile> edit)
    {
        string before = Canonical();
        var candidate = RoomSerializer.Read(before, "edit");
        edit(candidate);
        CheckDecorNumbers(candidate);
        var errors = RoomValidator.Validate(candidate, "edit");
        if (errors.Count != 0) throw new FormatException(string.Join("\n", errors));
        string after = RoomSerializer.Write(candidate);
        if (after == before) return;
        Remember(undo,before); redo.Clear(); Room = candidate; currentCanonical=after;
    }
    static void Remember(Stack<string> stack,string value)
    {
        stack.Push(value);
        var kept=new List<string>();long chars=0;
        foreach(var item in stack){if(kept.Count>=100 || kept.Count>0 && chars+item.Length>16_000_000)break;kept.Add(item);chars+=item.Length;}
        stack.Clear();for(int i=kept.Count-1;i>=0;i--)stack.Push(kept[i]);
    }
    public void Undo() { if (!CanUndo) return; Remember(redo,Canonical()); currentCanonical=undo.Pop(); Room = RoomSerializer.Read(currentCanonical, "undo"); }
    public void Redo() { if (!CanRedo) return; Remember(undo,Canonical()); currentCanonical=redo.Pop(); Room = RoomSerializer.Read(currentCanonical, "redo"); }
    public void Resize(int width, int height, bool allowCrop)
    {
        Guard(width, height);
        Change(r =>
        {
            if (!allowCrop)
            {
                foreach (var rows in r.Layers.Values)
                    for (int y = 0; y < r.Height; y++)
                        for (int x = 0; x < r.Width; x++)
                            if ((x >= width || y >= height) && rows[r.Height - 1 - y][x] != '.')
                                throw new InvalidOperationException("Уменьшение удалит клетки. Разрешите обрезку явно.");
                if (r.Objects.Any(o => o.X >= width || o.Y >= height) || r.Decor.Any(o => o.X >= width || o.Y >= height))
                    throw new InvalidOperationException("Уменьшение удалит объекты. Разрешите обрезку явно.");
            }
            foreach (string layer in r.Layers.Keys.ToArray())
            {
                string[] old = r.Layers[layer];
                var grid = RoomLayers.NewGrid(width, height);
                for (int y = 0; y < Math.Min(height, r.Height); y++)
                    for (int x = 0; x < Math.Min(width, r.Width); x++) grid[height - 1 - y][x] = old[r.Height - 1 - y][x];
                r.Layers[layer] = Array.ConvertAll(grid, row => new string(row));
            }
            r.Width = width; r.Height = height;
            r.Objects.RemoveAll(o => o.X >= width || o.Y >= height);
            r.Decor.RemoveAll(o => o.X >= width || o.Y >= height);
        });
    }
    static bool Inside(RoomFile r, int x, int y) => x >= 0 && x < r.Width && y >= 0 && y < r.Height;
    public string Pick(string layer, int x, int y)
    {
        if (!Inside(Room, x, y) || !Room.Layers.TryGetValue(layer, out var rows)) return ".";
        char c = rows[Room.Height - 1 - y][x];
        return c == '.' || RoomLayers.GroupOf(layer) == null ? c.ToString() : Room.Palette(layer)[c];
    }
    char Key(RoomFile r, string layer, string value)
    {
        if (!RoomLayers.IsKnown(layer)) throw new ArgumentException("Неизвестный слой.");
        if (value == ".") return '.';
        string group = RoomLayers.GroupOf(layer);
        if (group == null)
        {
            if (value.Length != 1 || !RoomLayers.FixedAllowed(layer, value[0])) throw new ArgumentException("Неверное значение слоя.");
            return value[0];
        }
        var d = Definitions.Require(value, layer);
        var kind = Kind(layer);
        if (d.Kind != kind) throw new ArgumentException("Материал не подходит выбранному слою.");
        var palette = r.Palette(layer);
        foreach (var entry in palette) if (entry.Value == value) return entry.Key;
        for (char c = '!'; c <= '~'; c++)
            if (c != '.' && c != '"' && c != '\\' && !palette.ContainsKey(c)) { palette.Add(c, value); return c; }
        throw new InvalidOperationException("Нет свободных символов в палитре слоя.");
    }
    public static RoomDefinitionKind Kind(string layer) => layer switch
    {
        "solid" => RoomDefinitionKind.Solid, "back" => RoomDefinitionKind.Backwall,
        "ladder" => RoomDefinitionKind.Ladder, "platform" => RoomDefinitionKind.Platform,
        "stairs" => RoomDefinitionKind.Stairs, "decor" => RoomDefinitionKind.Decor,
        "objects" => RoomDefinitionKind.Object, _ => throw new ArgumentException("Нет каталога для слоя.")
    };
    public void Paint(string layer, string value, int x0, int y0, int x1, int y1, string tool)
    {
        if (!Inside(Room, x0, y0) || !Inside(Room, x1, y1)) return;
        Change(r =>
        {
            char key = Key(r, layer, value);
            char[][] grid = r.Layers.TryGetValue(layer, out var rows) ? Array.ConvertAll(rows, s => s.ToCharArray()) : RoomLayers.NewGrid(r.Width, r.Height);
            void Set(int x, int y) => grid[r.Height - 1 - y][x] = key;
            if (tool == "fill")
            {
                char old = grid[r.Height - 1 - y0][x0];
                if (old == key) return;
                var queue = new Queue<(int x, int y)>(); queue.Enqueue((x0, y0)); Set(x0, y0);
                while (queue.Count != 0)
                {
                    var p = queue.Dequeue();
                    foreach (var q in new[] { (p.x - 1, p.y), (p.x + 1, p.y), (p.x, p.y - 1), (p.x, p.y + 1) })
                        if (Inside(r, q.Item1, q.Item2) && grid[r.Height - 1 - q.Item2][q.Item1] == old)
                        { Set(q.Item1, q.Item2); queue.Enqueue(q); }
                }
            }
            else if (tool == "rectangle")
                for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                    for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Set(x, y);
            else
            {
                // Integer line joins pointer samples without holes at low frame rates.
                int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true) { Set(x0, y0); if (x0 == x1 && y0 == y1) break; int e = 2 * err; if (e >= dy) { err += dy; x0 += sx; } if (e <= dx) { err += dx; y0 += sy; } }
            }
            r.Layers[layer] = Array.ConvertAll(grid, row => new string(row));
        });
    }
    public void Stroke(string layer, string value, IReadOnlyList<(int x, int y)> points)
    {
        if(points.Count == 0) return;
        Change(r =>
        {
            char key=Key(r,layer,value);
            var grid=r.Layers.TryGetValue(layer,out var rows)?Array.ConvertAll(rows,s=>s.ToCharArray()):RoomLayers.NewGrid(r.Width,r.Height);
            var previous=points[0];
            foreach(var point in points)
            {
                if(!Inside(r,point.x,point.y)){previous=point;continue;}
                if(!Inside(r,previous.x,previous.y))previous=point;
                int x=previous.x,y=previous.y,dx=Math.Abs(point.x-x),sx=x<point.x?1:-1,dy=-Math.Abs(point.y-y),sy=y<point.y?1:-1,err=dx+dy;
                while(true){grid[r.Height-1-y][x]=key;if(x==point.x&&y==point.y)break;int e=2*err;if(e>=dy){err+=dy;x+=sx;}if(e<=dx){err+=dx;y+=sy;}}
                previous=point;
            }
            r.Layers[layer]=Array.ConvertAll(grid,row=>new string(row));
        });
    }
    public void SetConnectors(string text) => Change(r => r.Connectors = string.IsNullOrWhiteSpace(text) ? Array.Empty<int>() : text.Split(',').Select(v => int.Parse(v.Trim())).ToArray());
    public void SetOptions(string text)=>Change(r=>
    {
        var value=JsonText.Parse(text,"options");if(value.Kind!=JsonKind.Object)throw new FormatException("Настройки должны быть JSON-объектом строк.");
        r.Options.Clear();foreach(var field in value.Fields)r.Options.Add(field.Key,field.Value.AsString("options."+field.Key));
    });
    public void SetTemplate(string template) => Change(r => r.Template = template);
    public string AddEntity(string layer, string type, int x, int y)
    {
        var d = Definitions.Require(type, layer);
        if (d.Kind != Kind(layer)) throw new ArgumentException("Неверный тип объекта.");
        string id = FreshId(Room, layer == "decor" ? "decor" : "object");
        Change(r =>
        {
            if (!Inside(r, x, y)) throw new ArgumentException("Объект вне комнаты.");
            if (layer == "decor") r.Decor.Add(new RoomDecor { Id = id, Type = type, X = x, Y = y });
            else r.Objects.Add(new RoomObject { Id = id, Type = type, X = x, Y = y });
        });
        return id;
    }
    static string FreshId(RoomFile r, string prefix)
    {
        var ids = r.Objects.Select(o => o.Id).Concat(r.Decor.Select(o => o.Id)).ToHashSet(StringComparer.Ordinal);
        int n = 1; while (ids.Contains(prefix + "-" + n)) n++; return prefix + "-" + n;
    }
    public void MoveEntity(string layer, int index, int x, int y) => Change(r =>
    {
        if (!Inside(r, x, y)) throw new ArgumentException("Объект вне комнаты.");
        if (layer == "decor") { r.Decor[index].X = x; r.Decor[index].Y = y; }
        else { r.Objects[index].X = x; r.Objects[index].Y = y; }
    });
    public void DeleteEntity(string layer, int index) => Change(r => { if (layer == "decor") r.Decor.RemoveAt(index); else r.Objects.RemoveAt(index); });
    public void SetParameters(string layer, int index, string text) => Change(r =>
    {
        var value = JsonText.Parse(text, "params");
        if (value.Kind != JsonKind.Object) throw new FormatException("Параметры должны быть JSON-объектом строк.");
        var target = layer == "decor" ? r.Decor[index].Params : r.Objects[index].Params;
        target.Clear(); foreach (var field in value.Fields) target.Add(field.Key, field.Value.AsString("params." + field.Key));
    });
    public void SetDecorNumber(int index,string key,string value) => Change(r=>
    {
        if(!new[]{"w","h","a","fr","lon"}.Contains(key))throw new ArgumentException("Неизвестный числовой параметр.");
        if(string.IsNullOrWhiteSpace(value))r.Decor[index].Params.Remove(key);
        else r.Decor[index].Params[key]=value;
    });
    static void CheckDecorNumbers(RoomFile room)
    {
        foreach(var d in room.Decor)foreach(var p in d.Params)
            if(new[]{"w","h","a","fr","lon"}.Contains(p.Key) &&
                (!double.TryParse(p.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number)))
                throw new FormatException($"Декор {d.Id}, параметр {p.Key}: требуется конечное число.");
    }
    public void Link(int index, string target) => Change(r =>
    {
        if (!r.Objects.Any(o => o.Id == target)) throw new ArgumentException("Цель связи не найдена.");
        r.Objects[index].Links.Add(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["targ"] = target });
    });
    public void RemoveLink(int index, int link) => Change(r => r.Objects[index].Links.RemoveAt(link));
    public void Copy(int x0, int y0, int x1, int y1)
    {
        int left = Math.Min(x0, x1), bottom = Math.Min(y0, y1), width = Math.Abs(x1 - x0) + 1, height = Math.Abs(y1 - y0) + 1;
        if (!Inside(Room, left, bottom) || !Inside(Room, left + width - 1, bottom + height - 1)) throw new ArgumentException("Выделение вне комнаты.");
        clipboard = RoomSerializer.Read(Canonical(), "clipboard");
        foreach (var layer in clipboard.Layers.Keys.ToArray())
            clipboard.Layers[layer] = Enumerable.Range(0, height).Select(row => Room.Layers[layer][Room.Height - bottom - height + row].Substring(left, width)).ToArray();
        clipboard.Objects.RemoveAll(o => o.X < left || o.X >= left + width || o.Y < bottom || o.Y >= bottom + height);
        clipboard.Decor.RemoveAll(o => o.X < left || o.X >= left + width || o.Y < bottom || o.Y >= bottom + height);
        foreach (var o in clipboard.Objects) { o.X -= left; o.Y -= bottom; }
        foreach (var o in clipboard.Decor) { o.X -= left; o.Y -= bottom; }
        clipboard.Width = width; clipboard.Height = height;
    }
    public void Paste(int x, int y)
    {
        if (clipboard == null) throw new InvalidOperationException("Буфер пуст.");
        Change(r =>
        {
            if (!Inside(r, x, y) || !Inside(r, x + clipboard.Width - 1, y + clipboard.Height - 1)) throw new ArgumentException("Вставка целиком должна помещаться в комнате.");
            foreach (var layer in clipboard.Layers)
            {
                var grid = r.Layers.TryGetValue(layer.Key, out var rows) ? Array.ConvertAll(rows, s => s.ToCharArray()) : RoomLayers.NewGrid(r.Width, r.Height);
                for (int cy = 0; cy < clipboard.Height; cy++) for (int cx = 0; cx < clipboard.Width; cx++)
                {
                    char c = layer.Value[clipboard.Height - 1 - cy][cx];
                    string value = c == '.' || RoomLayers.GroupOf(layer.Key) == null ? c.ToString() : clipboard.Palette(layer.Key)[c];
                    grid[r.Height - 1 - y - cy][x + cx] = Key(r, layer.Key, value);
                }
                r.Layers[layer.Key] = Array.ConvertAll(grid, row => new string(row));
            }
            var clone = RoomSerializer.Read(RoomSerializer.Write(clipboard), "paste");
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var o in clone.Objects) { string old = o.Id; o.Id = FreshId(r, "object"); if (old != null) remap[old] = o.Id; o.X += x; o.Y += y; r.Objects.Add(o); }
            foreach (var o in clone.Decor) { o.Id = FreshId(r, "decor"); o.X += x; o.Y += y; r.Decor.Add(o); }
            foreach (var o in clone.Objects) foreach (var link in o.Links) if (link.TryGetValue("targ", out var target) && remap.TryGetValue(target, out var replacement)) link["targ"] = replacement;
        });
    }
    public void MirrorLayer(string layer, int x0, int y0, int x1, int y1) => Change(r =>
    {
        int left = Math.Min(x0, x1), right = Math.Max(x0, x1), bottom = Math.Min(y0, y1), top = Math.Max(y0, y1);
        if (!Inside(r, left, bottom) || !Inside(r, right, top)) throw new ArgumentException("Выделение вне комнаты.");
        if (!r.Layers.TryGetValue(layer, out var rows)) return;
        var grid = Array.ConvertAll(rows, row => row.ToCharArray());
        for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
        {
            char c = rows[r.Height - 1 - y][right - (x - left)];
            if (c != '.' && (layer == RoomLayers.Stairs || layer == RoomLayers.Ladder))
            {
                var d = Definitions.Require(r.Palette(layer)[c], "mirror");
                if (d.MirrorForm == null) throw new FormatException("У формы нет зеркальной пары: " + d.Id);
                c = Key(r, layer, d.MirrorForm);
            }
            grid[r.Height - 1 - y][x] = c;
        }
        r.Layers[layer] = Array.ConvertAll(grid, row => new string(row));
    });
    public List<string> Validate()
    {
        var errors = RoomValidator.Validate(Room, "room");
        try{CheckDecorNumbers(Room);}catch(FormatException e){errors.Add(e.Message);}
        foreach (var group in Room.Palettes) foreach (var id in group.Value.Values)
            if (!Definitions.Items.TryGetValue(id,out var definition)) errors.Add("Неизвестный материал: " + id);
            else if(RoomLayers.All.Take(5).Contains(group.Key)&&definition.Kind!=Kind(group.Key))errors.Add("Материал не соответствует слою "+group.Key+": "+id);
        foreach (var o in Room.Objects) if (!Definitions.Items.TryGetValue(o.Type,out var definition)||definition.Kind!=RoomDefinitionKind.Object) errors.Add("Неизвестный тип объекта: " + o.Type);
        foreach (var o in Room.Decor) if (!Definitions.Items.TryGetValue(o.Type,out var definition)||definition.Kind!=RoomDefinitionKind.Decor) errors.Add("Неизвестный тип декора: " + o.Type);
        foreach (string id in RoomValidator.UnresolvedLinks(Room)) errors.Add("Связь вне комнаты или отсутствующая цель: " + id);
        if (errors.Count == 0)
        {
            try
            {
                var geometry = RoomGeometryBuilder.Build(Room, Definitions);
                errors.AddRange(geometry.Issues);
                foreach (var o in Room.Objects)
                    if (geometry.Surfaces.Any(s => !s.OneWay && o.X >= s.CellX && o.X < s.CellX + s.Cells && o.Y == s.CellY)) errors.Add($"Объект {o.Id} находится в твёрдой клетке {o.X},{o.Y}.");
            }
            catch(Exception e){errors.Add("Проверка геометрии: "+e.Message);}
        }
        return errors;
    }
}
