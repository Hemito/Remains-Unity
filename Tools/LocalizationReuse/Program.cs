using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var root=Path.GetFullPath(args.FirstOrDefault(x=>!x.StartsWith("--"))??".");
var options=new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
string tool=Path.Combine(root,"Tools/LocalizationReuse"),sources=Path.Combine(root,"RemainsUnity/Assets/Remains/Editor/LocalizationSources");
JsonNode Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!;
if(args.Contains("--self-test"))
{
    if(Normalize(" ИСПОЛЬЗОВАТЬ… ")!=Normalize("Использовать") || Normalize("Не использовать")==Normalize("Использовать"))throw new Exception("Normalization self-test failed");
    if(HasTemplate("Урон {0}")==false || HasTemplate("Урон @1")==false)throw new Exception("Template self-test failed");
    Console.WriteLine("PASS: normalization, negation and placeholder guards.");return;
}
var packs=Directory.GetFiles(sources,"*.json").OrderBy(x=>x,StringComparer.Ordinal).Select(p=>Read(p)["languages"]![0]!).ToArray();
var russian=packs.Single(p=>S(p,"code")=="ru");
var localeEntries=packs.ToDictionary(p=>S(p,"code"),p=>p["entries"]!.AsArray().ToDictionary(e=>S(e!,"key"),e=>S(e!,"text"),StringComparer.Ordinal));
var originals=russian["entries"]!.AsArray().Where(e=>!S(e!,"key").StartsWith("unity/",StringComparison.Ordinal)).Select(e=>new Original(S(e!,"key"),S(e!,"text"))).ToArray();
var byKey=originals.ToDictionary(o=>o.Key,StringComparer.Ordinal);
var exact=originals.GroupBy(o=>o.Russian,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
var normalized=originals.GroupBy(o=>Normalize(o.Russian),StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
var own=Read(Path.Combine(tool,"own-strings.json"))["entries"]!.AsArray().Select(e=>new Own(S(e!,"table"),S(e!,"key"),S(e!,"russian"),S(e!,"kind"))).OrderBy(e=>e.Table,StringComparer.Ordinal).ThenBy(e=>e.Key,StringComparer.Ordinal).ToArray();
var semanticPath=Path.Combine(tool,"semantic-mappings.json");
var semanticEntries=File.Exists(semanticPath)?Read(semanticPath)["mappings"]!.AsArray().Select(e=>e!).ToArray():Array.Empty<JsonNode>();
var semantic=semanticEntries.Where(e=>(bool?)e["approved"]==true).ToDictionary(e=>(S(e,"table") is {Length:>0} t?t:"Remains.UI",S(e,"ownKey") is {Length:>0} k?k:S(e,"key")),e=>e);
var semanticSuggestions=semanticEntries.Where(e=>(bool?)e["approved"]!=true).ToDictionary(e=>(S(e,"table") is {Length:>0} t?t:"Remains.UI",S(e,"ownKey") is {Length:>0} k?k:S(e,"key")),e=>e);
var inverted=new Dictionary<string,List<Original>>(StringComparer.Ordinal);
var originalTokens=originals.ToDictionary(o=>o.Key,o=>Tokens(o.Russian),StringComparer.Ordinal);
foreach(var entry in originals)foreach(string token in originalTokens[entry.Key]){if(!inverted.TryGetValue(token,out var list))inverted[token]=list=new();list.Add(entry);}
var records=new List<Result>();var mappings=new List<object>();
foreach(var entry in own)
{
    Original[] found=Array.Empty<Original>();string type="unmatched",reason="";
    if(exact.TryGetValue(entry.Russian,out var same)){found=same;type="exact";}
    else if(!HasTemplate(entry.Russian) && normalized.TryGetValue(Normalize(entry.Russian),out var near)){found=near.Where(o=>!HasTemplate(o.Russian)).ToArray();if(found.Length>0)type="normalized";}
    if(semantic.TryGetValue((entry.Table,entry.Key),out var manual))
    {
        if(S(manual,"ownRussian") is {Length:>0} expected && expected!=entry.Russian)throw new InvalidDataException("Semantic ownRussian changed: "+entry.Key);
        if(!byKey.TryGetValue(S(manual,"originalKey"),out var original))throw new InvalidDataException("Unknown semantic originalKey: "+S(manual,"originalKey"));
        if(HasTemplate(entry.Russian)||HasTemplate(original.Russian))throw new InvalidDataException("Semantic template requires explicit argument adapter: "+entry.Key);
        found=new[]{original};type="semantic-approved";reason=S(manual,"reason");
    }
    bool reusable=found.Length>0 && !HasTemplate(entry.Russian) && found.All(o=>!HasTemplate(o.Russian));
    if(reusable && found.Select(TranslationSignature).Distinct(StringComparer.Ordinal).Count()>1){reusable=false;type="ambiguous";reason="Одинаковый русский текст имеет разные переводы в исходных контекстах.";}
    if(found.Length>0 && !reusable && type!="ambiguous"){type="template-review";reason="Требуется явное сопоставление аргументов шаблона.";}
    var choices=found.Select(o=>Candidate(o,1)).ToList();
    if(found.Length==0)
    {
        var tokens=Tokens(entry.Russian);var pool=tokens.Where(inverted.ContainsKey).SelectMany(t=>inverted[t]).DistinctBy(o=>o.Key).ToArray();
        choices=pool.Select(o=>(Original:o,Score:Score(entry.Russian,o.Russian,tokens,originalTokens[o.Key]))).Where(x=>x.Score>=.34).OrderByDescending(x=>x.Score).ThenBy(x=>x.Original.Key,StringComparer.Ordinal).Take(3).Select(x=>Candidate(x.Original,Math.Round(x.Score,3))).ToList();
        if(choices.Count>0){type="candidate-review";reason="Лексическое сходство: значение и контекст не подтверждены, автоматически не применяется.";}
    }
    if(!reusable && semanticSuggestions.TryGetValue((entry.Table,entry.Key),out var suggestion))
    {
        if(!byKey.TryGetValue(S(suggestion,"originalKey"),out var proposed))throw new InvalidDataException("Unknown proposed originalKey: "+S(suggestion,"originalKey"));
        choices.RemoveAll(c=>c.Key==proposed.Key);choices.Insert(0,Candidate(proposed,1));
        type="candidate-review";reason="Ручной кандидат, не утверждён: "+S(suggestion,"reason");
    }
    var selected=reusable?found.OrderBy(o=>o.Key,StringComparer.Ordinal).First():null;
    if(selected!=null)mappings.Add(new{table=entry.Table,key=entry.Key,russian=entry.Russian,originalKey=selected.Key,originalRussian=selected.Russian,matchType=type,reason});
    records.Add(new(entry.Table,entry.Key,entry.Russian,entry.Kind,type,reusable,reason,choices));
}
var summary=records.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count());
if(records.Count!=own.Length || mappings.Count!=records.Count(r=>r.Reusable) || records.GroupBy(r=>(r.Table,r.Key)).Any(g=>g.Count()!=1))throw new InvalidDataException("Inventory partition failed");
var byKind=records.GroupBy(r=>r.Kind).ToDictionary(g=>g.Key,g=>new{total=g.Count(),reused=g.Count(r=>r.Reusable),unmatched=g.Count(r=>r.Status=="unmatched"),review=g.Count(r=>!r.Reusable&&r.Status!="unmatched")});
var report=new{schema=1,sourceLanguage="ru",originalEntries=originals.Length,ownEntries=own.Length,reused=records.Count(r=>r.Reusable),summary,byKind,entries=records};
string history=Path.Combine(root,"docs/history");Directory.CreateDirectory(history);
File.WriteAllText(Path.Combine(history,"localization-reuse-20261111.json"),JsonSerializer.Serialize(report,options)+"\n",new UTF8Encoding(false));
File.WriteAllText(Path.Combine(tool,"reuse-map.json"),JsonSerializer.Serialize(new{schema=1,mappings},options)+"\n",new UTF8Encoding(false));
var md=new StringBuilder("# Сопоставление собственных строк с оригиналом — 2026-11-11\n\n");
md.AppendLine($"Собственных записей: **{own.Length}**; исходных текстовых записей оригинала: **{originals.Length}**. Безопасно сопоставлено: **{records.Count(r=>r.Reusable)}**. Единица учёта — table/key, одинаковые русские подписи разных кнопок перечислены отдельно.\n");
md.AppendLine("Источник оригинала — Editor/LocalizationSources/*.json, только исходные ключи: добавленные нами `unity/…` исключены. Собственные строки — фактическая русская таблица UI, описания предметов и названия, отличающиеся от оригинального каталога по стабильному ID. В исходных ключах сохраняются ordinal повторов.\n");
md.AppendLine("Точное совпадение и изменение регистра/оформления допускают reuse только при согласованных переводах всех дубликатов и отсутствии шаблонов. Семантические соответствия проверены отдельно. Лексические кандидаты не являются найденным переводом. Формулы, смысл механики и аргументы не подменяются. Русский исходник нашей строки сохраняется.\n");
md.AppendLine("Обозначения: `exact` — точное совпадение; `normalized` — совпадение без учёта оформления; `semantic-approved` — проверенное смысловое соответствие; `ambiguous` — исходные дубликаты переводятся по-разному; `candidate-review` — требуется проверка; `unmatched` — подходящей записи не найдено. Виды: `ui` — интерфейс, `item-name` — название предмета, `description` — описание.\n");
md.AppendLine("| Результат | Записей |\n|---|---:|");foreach(var group in summary.OrderBy(x=>x.Key,StringComparer.Ordinal))md.AppendLine($"| {group.Key} | {group.Value} |");
md.AppendLine("\n| Вид собственных строк | Всего | Сопоставлены | На проверку | Не найдены |\n|---|---:|---:|---:|---:|");foreach(var group in byKind.OrderBy(x=>x.Key,StringComparer.Ordinal))md.AppendLine($"| {group.Key} | {group.Value.total} | {group.Value.reused} | {group.Value.review} | {group.Value.unmatched} |");
foreach(var section in new[]{("Подтверждённые соответствия",records.Where(r=>r.Reusable)),("Спорные кандидаты и неоднозначные записи",records.Where(r=>!r.Reusable&&r.Status!="unmatched")),("Соответствие не найдено",records.Where(r=>r.Status=="unmatched"))})
{
    md.AppendLine($"\n## {section.Item1}\n");
    foreach(var item in section.Item2)
    {
        md.AppendLine($"- **{Escape(item.Russian)}** — `{item.Table}/{item.Key}`; {item.Status}.");
        if(item.Reason.Length>0)md.AppendLine("  "+item.Reason);
        foreach(var candidate in item.Candidates.Take(item.Reusable?1:3))md.AppendLine($"  - `{candidate.Key}`: «{Escape(candidate.Russian)}»; покрытие {candidate.Translations.Count}/9; сходство {candidate.Score.ToString("0.###",CultureInfo.InvariantCulture)}.");
    }
}
md.AppendLine("\nПолные переводы кандидатов по каждому языку и все альтернативные исходные ключи — в [JSON-отчёте](localization-reuse-20261111.json). Применяемый список — Tools/LocalizationReuse/reuse-map.json. Повторный запуск не использует время, абсолютные пути и недетерминированный порядок.\n");
File.WriteAllText(Path.Combine(history,"localization-reuse-20261111.md"),md.ToString(),new UTF8Encoding(false));
Console.WriteLine(JsonSerializer.Serialize(new{own=own.Length,original=originals.Length,reused=mappings.Count,summary}));
string TranslationSignature(Original entry)=>string.Join("\u001f",localeEntries.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value.TryGetValue(entry.Key,out var text)?text:"<missing>"));
Choice Candidate(Original entry,double score)=>new(entry.Key,entry.Russian,score,localeEntries.Where(p=>p.Value.TryGetValue(entry.Key,out var value)&&!string.IsNullOrWhiteSpace(value)).ToDictionary(p=>p.Key,p=>p.Value[entry.Key]));
static string S(JsonNode node,string key)=>(string?)node[key]??"";
static bool HasTemplate(string text)=>Regex.IsMatch(text,@"[@#]\d+|\{[^{}]*\}");
static string Normalize(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('ё','е'),@"[^\p{L}\p{Nd}]+"," ").Trim();
static HashSet<string> Tokens(string text)=>Normalize(text).Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t.Length>2).Select(t=>t.Length>5?t[..^2]:t).ToHashSet(StringComparer.Ordinal);
static double Score(string a,string b,HashSet<string> at,HashSet<string> bt)
{
    if(at.Count==0||bt.Count==0)return 0;
    double overlap=at.Intersect(bt).Count();double jaccard=overlap/(at.Count+bt.Count-overlap);
    double length=(double)Math.Min(a.Length,b.Length)/Math.Max(1,Math.Max(a.Length,b.Length));
    return jaccard*.85+length*.15;
}
static string Escape(string text)=>text.Replace("\r","").Replace("\n"," ↵ ").Replace("|","\\|").Replace("*","\\*");
record Original(string Key,string Russian);
record Own(string Table,string Key,string Russian,string Kind);
record Choice(string Key,string Russian,double Score,Dictionary<string,string> Translations);
record Result(string Table,string Key,string Russian,string Kind,string Status,bool Reusable,string Reason,List<Choice> Candidates);
