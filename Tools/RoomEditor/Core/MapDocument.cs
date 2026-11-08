using Remains.RoomFormat;

namespace Remains.RoomEditor;

// A hand-picked stored layout, read by the same reader as generated layouts.
// No generator rules or room format changes are needed to edit the selection.
public sealed class MapDocument
{
    public FactoryLayout Layout { get; private set; }
    public Dictionary<string, RoomFile> Templates { get; } = new(StringComparer.Ordinal);
    readonly Stack<string> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void AddRoom(string text)
    {
        var room = RoomSerializer.Read(text.TrimStart('\ufeff'), "map room");
        if(room.Width<=0||room.Height<=0||(long)room.Width*room.Height>4_000_000)throw new FormatException("Шаблон карты превышает предел 4 млн клеток.");
        var errors = RoomValidator.Validate(room, "map room");
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors));
        if(Templates.TryGetValue(room.Template,out var existing)&&RoomSerializer.Write(existing)==RoomSerializer.Write(room))return;
        if(Layout!=null){Remember(undo,SaveDraft());redo.Clear();}
        Templates[room.Template] = room;
    }
    public void New(int width, int depth, string template)
    {
        if (width < 1 || depth < 1 || (long)width * depth > 10000) throw new ArgumentException("Размер карты: 1–10000 комнат.");
        if (!Templates.ContainsKey(template)) throw new ArgumentException("Неизвестная комната.");
        var layout = new FactoryLayout { Width = width, Depth = depth, Land = "plant", GeneratorVersion = FactoryLayoutGenerator.GeneratorVersion };
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
            layout.Cells.Add(new FactoryCell { X = x, Y = y, Template = template, Start = x == 0 && y == 0 });
        Open(layout.Write());
    }
    public void Open(string text)
    {
        text=text.TrimStart('\ufeff');
        var root = JsonText.Parse(text, "map");
        long width = root.Get("width")?.AsLong("width") ?? 0, depth = root.Get("depth")?.AsLong("depth") ?? 0;
        if (width < 1 || depth < 1 || width > 10000 || depth > 10000 || width * depth > 10000) throw new ArgumentException("Карта слишком большая или размер неверен.");
        var candidate = FactoryLayoutReader.Read(text, "map");
        foreach(var c in candidate.Cells) if(!Templates.ContainsKey(c.Template)) throw new FormatException("Сначала добавьте шаблон комнаты: " + c.Template);
        Layout = candidate; undo.Clear(); redo.Clear();
    }
    public string Save() => Layout.Write();
    // Browser draft envelope only; downloaded maps retain the game's unchanged format.
    public string SaveDraft()
    {
        var rooms=JsonValue.NewArray();
        foreach(var r in Templates.OrderBy(p=>p.Key,StringComparer.Ordinal))rooms.Add(JsonValue.Of(RoomSerializer.Write(r.Value)));
        return JsonText.Write(JsonValue.NewObject().Set("draftVersion",JsonValue.Of(1)).Set("layout",JsonValue.Of(Save())).Set("rooms",rooms));
    }
    public void OpenDraft(string text)=>RestoreDraft(text,true);
    void RestoreDraft(string text,bool resetHistory)
    {
        var root=JsonText.Parse(text,"map draft");
        if(root.Get("draftVersion")?.AsLong("draftVersion")!=1)throw new FormatException("Неизвестная версия черновика карты.");
        var candidate=new MapDocument();
        foreach(var room in root.Get("rooms").Items)candidate.AddRoom(room.AsString("room"));
        candidate.Open(root.Get("layout").AsString("layout"));
        Templates.Clear();foreach(var pair in candidate.Templates)Templates.Add(pair.Key,pair.Value);
        Layout=candidate.Layout;if(resetHistory){undo.Clear();redo.Clear();}
    }
    void Change(Action<FactoryLayout> edit)
    {
        string before = Save();string snapshot=SaveDraft(); var candidate = FactoryLayoutReader.Read(before, "map edit"); edit(candidate);
        string after = candidate.Write(); candidate = FactoryLayoutReader.Read(after, "map edit");
        if (before == after) return; Remember(undo,snapshot); redo.Clear(); Layout = candidate;
    }
    static void Remember(Stack<string> stack,string text)
    {
        stack.Push(text);var kept=new List<string>();long chars=0;
        foreach(var item in stack){if(kept.Count>=100||kept.Count>0&&chars+item.Length>16_000_000)break;kept.Add(item);chars+=item.Length;}
        stack.Clear();for(int i=kept.Count-1;i>=0;i--)stack.Push(kept[i]);
    }
    public void Undo() { if (!CanUndo) return; Remember(redo,SaveDraft());RestoreDraft(undo.Pop(),false); }
    public void Redo() { if (!CanRedo) return; Remember(undo,SaveDraft());RestoreDraft(redo.Pop(),false); }
    public void SetRoom(int x, int y, string template, bool mirror)
    {
        if (!Templates.ContainsKey(template)) throw new ArgumentException("Неизвестная комната.");
        Change(l => { var c = l.Cell(x, y); c.Template = template; c.Mirror = mirror; c.Role = FactoryCellRole.None; c.RolePoint = null; });
    }
    public void SetExit(int x, int y, string point) => Change(l =>
    {
        var c = l.Cell(x, y);
        if (!Templates[c.Template].Objects.Any(o => o.Id == point && o.Type == "remains:obj:player")) throw new ArgumentException("Точка выхода не найдена в комнате.");
        c.Role = FactoryCellRole.Exit; c.RolePoint = point;
    });
    public void AddSeam(int x, int y, bool down, int slot, int width) => Change(l =>
    {
        if (l.Seams.Any(s => s.X == x && s.Y == y && s.Down == down && s.Slot == slot)) throw new ArgumentException("Такой стык уже есть.");
        l.Seams.Add(new FactorySeam { X = x, Y = y, Down = down, Slot = slot, Width = width });
        l.Seams.Sort((a, b) => { int c = a.X.CompareTo(b.X); if (c == 0) c = a.Y.CompareTo(b.Y); if (c == 0) c = a.Down.CompareTo(b.Down); if (c == 0) c = a.Slot.CompareTo(b.Slot); return c; });
    });
    public void RemoveSeam(int index) => Change(l => l.Seams.RemoveAt(index));
    public FactoryGrid Build(RoomDefinitionCatalog definitions, string border)
    {
        var first=Templates[Layout.Cell(0,0).Template];
        if(first.Width<2||first.Height<2)throw new InvalidOperationException("Общая карта с общими стенами требует комнаты не меньше 2×2 клеток.");
        long width=(long)Layout.Width*(first.Width-1)+1,height=(long)Layout.Depth*(first.Height-1)+1;
        if(width<=0||height<=0||width*height>4_000_000)throw new InvalidOperationException("Предпросмотр карты ограничен 4 млн клеток; раскладку можно сохранить без сборки.");
        return FactoryGridBuilder.Build(Layout, Templates, definitions, border, false);
    }
    public List<string> Validate(RoomDefinitionCatalog definitions)
    {
        var issues = new List<string>(); var seenIds = new HashSet<string>(StringComparer.Ordinal);
        if(Layout.Cells.Count(c=>c.Start)!=1||!Layout.Cell(0,0).Start)issues.Add("Игра требует единственный вход Start в комнате (0,0); другая схема входа пока не поддерживается.");
        foreach (var c in Layout.Cells)
        {
            if (!Templates.TryGetValue(c.Template, out var room)) { issues.Add($"Нет шаблона {c.Template} для ({c.X},{c.Y})."); continue; }
            if(c.Start && room.Objects.Count(o=>o.Type=="remains:obj:player")!=1)issues.Add($"Вход ({c.X},{c.Y}): игре нужна одна точка remains:obj:player.");
            if(c.Role!=FactoryCellRole.None && !room.Objects.Any(o=>o.Type=="remains:obj:player"&&o.Id==c.RolePoint))issues.Add($"Неверная точка роли ({c.X},{c.Y}): {c.RolePoint}.");
            foreach (var id in room.Objects.Select(o => o.Id).Concat(room.Decor.Select(o => o.Id)))
                if (id == null) issues.Add($"Комната {c.Template} содержит объект без постоянного ID.");
                else if (!seenIds.Add($"{c.X},{c.Y}:{id}")) issues.Add($"Повтор ID экземпляра {c.X},{c.Y}:{id}.");
        }
        foreach (var seam in Layout.Seams)
        {
            var a = Layout.Cell(seam.X, seam.Y); var b = Layout.Cell(seam.X + (seam.Down ? 0 : 1), seam.Y + (seam.Down ? 1 : 0));
            if (!Templates.TryGetValue(a.Template, out var ar) || !Templates.TryGetValue(b.Template, out var br)) continue;
            int[] Conn(RoomFile r, bool mirror)
            {
                if (r.Connectors.Length != 22) { issues.Add($"{r.Template}: для проверки стыка нужны 22 разъёма."); return new int[22]; }
                return mirror ? FactoryLayoutGenerator.Mirror(r.Connectors) : r.Connectors;
            }
            int allowed = Math.Min(Conn(ar, a.Mirror)[seam.Slot], Conn(br, b.Mirror)[seam.Slot + 11]);
            if (allowed < seam.Width) issues.Add($"Стык ({seam.X},{seam.Y}), слот {seam.Slot}: ширина {seam.Width}, совместимо {allowed}.");
        }
        // This is graph connectivity through seams, not character-level navigation.
        var reached = new HashSet<(int, int)> { (0, 0) }; var queue = new Queue<(int x, int y)>(); queue.Enqueue((0, 0));
        while (queue.Count != 0)
        {
            var p = queue.Dequeue();
            foreach (var s in Layout.Seams)
            {
                var a = (s.X, s.Y); var b = (s.X + (s.Down ? 0 : 1), s.Y + (s.Down ? 1 : 0));
                if (a == p && reached.Add(b)) queue.Enqueue(b); if (b == p && reached.Add(a)) queue.Enqueue(a);
            }
        }
        foreach (var c in Layout.Cells) if (!reached.Contains((c.X, c.Y))) issues.Add($"Нет цепочки проходов от входа к комнате ({c.X},{c.Y}).");
        try
        {
            string border = definitions.Items.Values.First(d => d.Kind == RoomDefinitionKind.Solid).Id;
            issues.AddRange(Build(definitions, border).Issues);
        }
        catch (Exception e) { issues.Add("Сборка общей геометрии: " + e.Message); }
        return issues;
    }
}
