using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyWearableSourceAuthoring
    {
        public sealed class Source {public string Id,Resource,OutputPath;}
        private const string Root="Assets/Remains/Resources/Original/Pony/Wearables/";
        private const string XmlHash="8859eba9c9eac5589d56634baebe317dce2ea065bb468301dabf35443c92af0f";
        private static readonly int[] Selectors={24,55,78,101,124,159,182,273,297,319};
        private static readonly HashSet<int> Hair=new(){130,135,188,194,226,232,809,849,886,891};
        private static readonly Dictionary<int,string> Tint=new()
        {{4,"fur"},{26,"fur"},{57,"fur"},{80,"fur"},{103,"fur"},{138,"fur"},{161,"fur"},{199,"fur"},{203,"eye"},{235,"fur"},{237,"fur"},{239,"magic"},{270,"fur"},{276,"fur"},{299,"fur"}};
        private static readonly string[] Expected={"pip","kombu","chitin","skin","metal","battle","assault","magus","antirad","antihim","intel","astealth","moon","sapper","socks","power","polic","spec","encl","ali","tre"};

        // Called before runtime catalog registration. Generated sources remain schema1; no defensive/gameplay stats are introduced.
        public static IReadOnlyList<Source> BuildSources()
        {
            string project=Path.GetFullPath("..");string xmlPath=Path.Combine(project,".reference/original/pfeUI.xml");
            if(PonyWearableArtSource.Hash(File.ReadAllBytes(xmlPath))!=XmlHash)throw new InvalidDataException("Одежда: XML не соответствует закреплённой 1.04 Graphic Update.");
            string dataPath=Path.Combine(project,".reference/original/dlc-ui/scripts/fe/AllData.as");
            var definitions=Regex.Matches(File.ReadAllText(dataPath),@"<armor\b[^>]*>").Cast<Match>().Select(m=>XElement.Parse(m.Value+"</armor>"))
                .ToDictionary(e=>(string)e.Attribute("id"),StringComparer.Ordinal);
            string ruPath=Path.Combine(project,".reference/rig-symbols/armor-text-ru.xml");
            if(!File.Exists(ruPath))
            {
                const string installed="Q:/Steam/steamapps/common/Remains/DLC/text_ru.xml";
                if(!File.Exists(installed))throw new InvalidDataException("Одежда: нужен снимок DLC/text_ru.xml по пути "+ruPath);
                Directory.CreateDirectory(Path.GetDirectoryName(ruPath));File.Copy(installed,ruPath);
                Debug.Log("Одежда: русские названия взяты из установленного DLC/text_ru.xml 1.04; снимок="+ruPath);
            }
            var names=XDocument.Load(ruPath).Descendants("armor").ToDictionary(e=>(string)e.Attribute("id"),e=>(string)e.Element("n"),StringComparer.Ordinal);
            var xml=new PonyWearableXmlSource(xmlPath);
            var skeleton=StateJson.FromJson<WearableSourceBank>(File.ReadAllText(Root+"antirad.json"));
            PonyAppearance.Prepare();
            using var art=new PonyWearableArtSource(xml,Path.Combine(project,".reference/research/visual-armor/source-shapes-4x"));
            var result=new List<Source>();var banks=new Dictionary<string,WearableSourceBank>(StringComparer.Ordinal);var skipped=new JArray();
            foreach(string id in Expected)
            {
                if(!definitions.TryGetValue(id,out var definition) || !names.TryGetValue(id,out string name) || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("Одежда: нет подтверждённого определения/русского названия armor="+id);
                string resource="Original/Pony/Wearables/"+id;
                if(id is "pip" or "antirad")
                {
                    result.Add(new Source {Id=id,Resource=resource,OutputPath=Root+id+"-render.asset"});continue;
                }
                try
                {
                    bool hide=(string)definition.Attribute("hide")=="1";
                    var templates=new Dictionary<string,WearableSourceTemplate>(StringComparer.Ordinal);var neutral=new List<int>();
                    foreach(int selector in Selectors)
                    {
                        var timeline=xml.GetTimeline(selector);
                        if(!timeline.Labels.TryGetValue(id,out int chosen))
                        {
                            // Obj.setArmor in this exact 1.04 source explicitly catches an absent label and selects frame1.
                            // The existing complete base bank already represents that neutral branch at each pose.
                            neutral.Add(selector);continue;
                        }
                        var parts=new List<WearableSourcePart>();
                        Flatten(xml,art,selector,Matrix4x4.identity,Color.white,"",Array.Empty<int>(),parts,chosen);
                        templates.Add(selector.ToString(CultureInfo.InvariantCulture),new WearableSourceTemplate {id=selector.ToString(CultureInfo.InvariantCulture),
                            sourceSelectorId=selector,sourceFrameOneBased=chosen+1,parts=parts.ToArray()});
                    }
                    if(templates.Count==0)throw new InvalidDataException("Нет ни одного подтверждённого графического selector label.");
                    var bank=new WearableSourceBank {schema=1,visualId="pony.armor."+id,itemId="armor."+id,anatomy="pony",baseSourceHash=PonyRenderAsset.SourceHash(),
                        templates=templates.Values.OrderBy(t=>t.sourceSelectorId).ToArray(),clips=skeleton.clips.Select(clip=>new WearableSourceClip {id=clip.id,frames=clip.frames.Select(frame=>new WearableSourceFrame {
                            baseDepthPaths=frame.baseDepthPaths,hiddenBasePartIndices=hide?frame.hiddenBasePartIndices:Array.Empty<int>(),
                            instances=frame.instances.Where(instance=>templates.ContainsKey(instance.templateId)).ToArray()}).ToArray()}).ToArray()};
                    bank.textures=bank.templates.SelectMany(t=>t.parts).Select(p=>p.texture).Distinct(StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal).Select(art.Declaration).ToArray();
                    var document=JObject.FromObject(bank);document["generator"]="PonyWearableSourceAuthoring.v1";document["sourceXmlSha256"]=XmlHash;
                    document["sourceArmorId"]=id;document["nameRu"]=name;document["hideMane"]=hide;
                    document["retainedNeutralSelectors"]=JArray.FromObject(neutral);document["retainedNeutralRule"]="1.04 Obj.setArmor catches absent label and selects frame1";
                    document["classification"]="resource/code-derived 1.04; no original runtime parity or gameplay armor claim";
                    PonyWearableArtSource.WriteOwned(Root+id+".json",Encoding.UTF8.GetBytes(document.ToString(Formatting.None)+"\n"),"remains-wearable-source-v1:");
                    if(neutral.Count>0)Debug.LogWarning($"Частичный комплект {id}: selector labels отсутствуют у [{string.Join(",",neutral)}]; сохранены исходные нейтральные ветви по правилу Obj.setArmor 1.04.");
                    if(id=="ali")Debug.LogWarning("Комплект ali: установлено графическое тело/шлем; крылья и способность полёта требуют отдельной игровой реализации.");
                    banks.Add(id,bank);result.Add(new Source {Id=id,Resource=resource,OutputPath=Root+id+"-render.asset"});
                }
                catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException)
                {skipped.Add(new JObject {["id"]=id,["reason"]=error.Message});Debug.LogWarning($"Комплект не установлен: armor={id}, причина={error.Message}. Исходные ресурсы и другие комплекты сохранены.");}
            }
            RegisterItems(result,names);
            foreach(var pair in banks)
            {
                var sprite=PonyWearableIconAuthoring.Build(pair.Key,pair.Value,art);
                PonyWearableIconAuthoring.Register("armor."+pair.Key,sprite);
            }
            AssetDatabase.SaveAssets();
            string report=Path.Combine(project,".reference/rig-symbols/wearable-source-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report,new JObject {["sourceXmlSha256"]=XmlHash,["textRuSha256"]=PonyWearableArtSource.Hash(File.ReadAllBytes(ruPath)),
                ["outfits"]=JArray.FromObject(result.Select(source=>source.Id)),["newOutfits"]=banks.Count,["skipped"]=skipped,
                ["sharedTextures"]=banks.Values.SelectMany(b=>b.textures).Select(t=>t.resource).Distinct().Count(),
                ["limits"]="resource-derived 1.04; specific outfit mini-previews, no source runtime parity, protection, durability, abilities or wings"}.ToString(Formatting.Indented));
            Debug.Log($"Источники визуальной одежды 1.04: комплектов={result.Count}, новых={banks.Count}; используемых текстур={banks.Values.SelectMany(b=>b.textures).Select(t=>t.resource).Distinct().Count()}. Защита/износ/способности не включены.");
            return result;
        }
        private static void RegisterItems(IReadOnlyList<Source> sources,IReadOnlyDictionary<string,string> names)
        {
            const string cosmetic="Assets/Remains/Resources/Content/cosmetic-equipment.json",text="Assets/Remains/Resources/Content/item-text.json";
            var catalog=JObject.Parse(File.ReadAllText(cosmetic));var rows=(JArray)catalog["items"];
            var strings=JObject.Parse(File.ReadAllText(text));var stringRows=(JArray)strings["items"];
            foreach(var source in sources)
            {
                string id="armor."+source.Id;var existing=rows.OfType<JObject>().SingleOrDefault(r=>(string)r["id"]==id);
                if(existing==null)rows.Add(new JObject {["id"]=id,["slot"]="armor.body",["visual"]="pony.armor."+source.Id,["anatomy"]="pony"});
                else if((string)existing["slot"]!="armor.body" || (string)existing["visual"]!="pony.armor."+source.Id || (string)existing["anatomy"]!="pony")
                    throw new InvalidDataException("Одежда: конфликт существующего определения "+id);
                if(!stringRows.OfType<JObject>().Any(r=>(string)r["id"]==id))stringRows.Add(new JObject {["id"]=id,["name"]=names[source.Id],
                    ["description"]="Визуальный комплект. Защита, специальные способности и износ пока не подключены."});
            }
            WriteJson(cosmetic,catalog);WriteJson(text,strings);
        }
        private static void WriteJson(string path,JObject value)
        {string text=value.ToString(Formatting.Indented)+"\n";if(File.ReadAllText(path)==text)return;File.WriteAllText(path,text,new UTF8Encoding(false));AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);}
        private static void Flatten(PonyWearableXmlSource xml,PonyWearableArtSource art,int id,Matrix4x4 transform,Color tint,string path,int[] order,
            List<WearableSourcePart> output,int chosen=-1,int hair=0,int eyes=0,string slot="none",List<PonyWearableArtSource.Mask> masks=null,int nesting=0)
        {
            if(nesting>40)throw new InvalidDataException("Одежда: циклический/слишком глубокий symbol="+id);
            masks??=new List<PonyWearableArtSource.Mask>();if(Tint.TryGetValue(id,out string binding))slot=binding;
            if(xml.IsShape(id))
            {
                string actual=path.Contains("/hair/")?(path.Contains("/h1/")?"hairSecondary":"hair"):slot;
                output.Add(new WearableSourcePart {shape=id.ToString(CultureInfo.InvariantCulture),texture=art.Texture(id,transform,masks),path=path,slot=actual,
                    hairStyle=hair,eyeStyle=eyes,matrix=new[]{transform.m00,transform.m10,transform.m01,transform.m11,transform.m03,transform.m13},
                    color=new[]{tint.r,tint.g,tint.b,tint.a},bounds=(float[])xml.Bounds(id).Clone(),depthPath=order});return;
            }
            var timeline=xml.GetTimeline(id);
            if(chosen<0 && (Hair.Contains(id) || id==215))
            {
                int expected=Hair.Contains(id)?5:6;
                if(timeline.Frames.Count!=expected)throw new InvalidDataException($"Одежда: appearance selector {id}, кадров={timeline.Frames.Count}, ожидалось={expected}.");
                for(int i=0;i<expected;i++)Flatten(xml,art,id,transform,tint,path,order,output,i,Hair.Contains(id)?i+1:hair,id==215?i+1:eyes,slot,masks,nesting+1);
                return;
            }
            if(timeline.Frames.Count==0)return;
            int frame=chosen<0?0:chosen;
            if(frame>=timeline.Frames.Count)throw new InvalidDataException("Одежда: неизвестный кадр sprite="+id+", frame="+frame);
            var active=new List<(int End,PonyWearableArtSource.Mask Value)>();
            foreach(var child in timeline.Frames[frame])
            {
                active.RemoveAll(mask=>mask.End<child.Depth);var matrix=transform*child.Matrix;
                if(child.ClipDepth>0)
                {
                    if(!xml.IsShape(child.Id))throw new InvalidDataException("Одежда: неподдержанная не-shape mask="+child.Id);
                    active.Add((child.ClipDepth,new PonyWearableArtSource.Mask(child.Id,matrix)));continue;
                }
                if(child.Name is "lwing" or "rwing")continue; // Explicit separate gameplay/presentation scope.
                Flatten(xml,art,child.Id,matrix,tint*child.Color,path+"/"+child.Name,order.Concat(new[]{child.Depth}).ToArray(),output,-1,hair,eyes,slot,
                    masks.Concat(active.Select(m=>m.Value)).ToList(),nesting+1);
            }
        }
    }
}
