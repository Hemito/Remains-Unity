using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

string root=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--"))??".");
if(args.Contains("--generate-items"))
{
    var glossary=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"Tools/LocalizationOwn/item-glossary.json")))!["entries"]!.AsArray().ToDictionary(e=>(string)e!["russian"]!,e=>e!["translations"]!);
    var inventory=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"Tools/LocalizationReuse/own-strings.json")))!["entries"]!.AsArray();
    var generated=new JsonArray();
    foreach(var entry in inventory.Where(e=>(string)e!["table"]! == "Remains.Items"))
    {
        string russian=(string)entry!["russian"]!;
        if(!glossary.TryGetValue(russian,out var translations))throw new Exception("Нет собственного перевода предмета: "+russian);
        generated.Add(new JsonObject{["table"]="Remains.Items",["key"]=(string)entry["key"]!,["russian"]=russian,["translations"]=translations.DeepClone()});
    }
    File.WriteAllText(Path.Combine(root,"RemainsUnity/Assets/Remains/Editor/LocalizationOwnItems.json"),new JsonObject{["schema"]=1,["entries"]=generated}.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
}
string folder=Path.Combine(root,"RemainsUnity/Assets/Remains/Editor");
string[] languages={"en","uk","zh-Hans","zh-Hant","pl","de","es","ja"};
var seen=new HashSet<(string Table,string Key)>();
int entries=0,values=0;
foreach(string file in Directory.GetFiles(folder,"LocalizationOwn*.json").OrderBy(p=>p,StringComparer.Ordinal))
{
    var data=JsonNode.Parse(File.ReadAllText(file))!;
    if((int?)data["schema"]!=1)throw new Exception("Unknown schema: "+file);
    int count=0;
    foreach(var entry in data["entries"]!.AsArray())
    {
        string table=(string)entry!["table"]!,key=(string)entry["key"]!,source=(string)entry["russian"]!;
        if(!seen.Add((table,key)))throw new Exception("Duplicate key across own packs: "+table+"/"+key);
        string expected=Signature(source);
        var translations=entry["translations"]!.AsObject();
        foreach(var translation in translations)
        {
            if(!languages.Contains(translation.Key))throw new Exception("Unknown locale: "+translation.Key+", "+key);
            string value=(string?)translation.Value??"";
            if(string.IsNullOrWhiteSpace(value))throw new Exception("Empty translation: "+key+"/"+translation.Key);
            if(Signature(value)!=expected)throw new Exception("Placeholder mismatch: "+key+"/"+translation.Key+", "+expected+" != "+Signature(value));
            if(translation.Key!="uk" && Regex.IsMatch(value,"[А-Яа-яЁё]"))throw new Exception("Russian fragment: "+key+"/"+translation.Key);
            values++;
        }
        entries++;count++;
    }
    Console.WriteLine($"PASS {Path.GetFileName(file)}: {count} stable keys");
}
Console.WriteLine($"PASS own localization: {entries} stable keys, {values} supplied translations; placeholders and nonempty values validated. Final missing-locale coverage must be checked against Unity tables because authored packs preserve existing translations.");
static string Signature(string value)=>string.Join("|",Regex.Matches(value,@"(?<!\{)\{(\d+)(?:,[-+]?\d+)?(?::([^{}]*))?\}(?!\})").Select(m=>m.Groups[1].Value+":"+m.Groups[2].Value).OrderBy(x=>x,StringComparer.Ordinal));
