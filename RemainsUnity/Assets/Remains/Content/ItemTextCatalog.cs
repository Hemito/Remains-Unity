using System;
using System.Collections.Generic;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ItemTextDefinition
    {public string id,name,description;}
    [Serializable] public sealed class ItemTextManifest
    {public int schema;public ItemTextDefinition[] items;}

    // Presentation metadata only. This catalog never changes item actions or saved IDs.
    public sealed class ItemTextCatalog
    {
        public const string Resource="Content/item-text";
        private static ItemTextCatalog loaded;
        private readonly Dictionary<string,ItemTextDefinition> entries=new(StringComparer.Ordinal);
        private readonly HashSet<string> missing=new(StringComparer.Ordinal);
        private readonly Action<string> warning;
        public int Count=>entries.Count;
        public ItemTextCatalog(string json,Action<string> warning=null)
        {
            this.warning=warning??(message=>Debug.LogWarning(message));
            var manifest=StateJson.FromJson<ItemTextManifest>(json);
            if(manifest==null || manifest.schema!=1 || manifest.items==null || manifest.items.Length==0)
                throw new InvalidOperationException("Некорректный каталог русских текстов предметов: schema=1, непустой items обязательны.");
            foreach(var entry in manifest.items) Register(entry);
        }
        // Call explicitly during UI setup, then pass the cached instance to consumers.
        public static ItemTextCatalog Load()
        {
            if(loaded!=null) return loaded;
            var asset=GameAssets.Load<TextAsset>(Resource);
            if(asset==null) throw new InvalidOperationException("Нет каталога русских текстов предметов: "+Resource);
            return loaded=new ItemTextCatalog(asset.text);
        }
        public bool Contains(string id)=>id!=null && entries.ContainsKey(id);
        // Code mods register after Load; replacement is explicit and validated before mutation.
        public void Register(ItemTextDefinition entry,bool replace=false)
        {
            if(entry==null || string.IsNullOrWhiteSpace(entry.id) || entry.id!=entry.id.Trim() ||
                string.IsNullOrWhiteSpace(entry.name) || string.IsNullOrWhiteSpace(entry.description))
                throw new InvalidOperationException("Неполные русские тексты предмета: id="+entry?.id);
            if(!replace && entries.ContainsKey(entry.id)) throw new InvalidOperationException("Повтор русских текстов предмета: id="+entry.id);
            entries[entry.id]=new ItemTextDefinition {id=entry.id,name=entry.name,description=entry.description};
        }
        public string Name(string id,string context)
        {
            if(id!=null && entries.TryGetValue(id,out var entry)) return entry.name;
            if(ExportedContentCatalog.Load().TryItem(id,out var source)&&!string.IsNullOrWhiteSpace(source.displayName))return source.displayName;
            Warn(id,context,"название");return "["+(id??"<null>")+"]";
        }
        public string Description(string id,string context)
        {
            if(id!=null && entries.TryGetValue(id,out var entry)) return entry.description;
            if(ExportedContentCatalog.Load().TryItem(id,out var source))
                return (string.IsNullOrWhiteSpace(source.description)?"":source.description+"\n")+
                    "Подключено как обычный предмет. Специальное действие пока не реализовано.";
            Warn(id,context,"описание");return "Нет описания предмета «"+(id??"<null>")+"».";
        }
        private void Warn(string id,string context,string field)
        {
            if(string.IsNullOrWhiteSpace(context)) throw new ArgumentException("Для неизвестного предмета нужен контекст обращения к текстам.",nameof(context));
            string key=field+"\n"+context+"\n"+id;
            if(missing.Add(key)) warning($"Нет русского текста предмета: id={id??"<null>"}, field={field}, context={context}, catalog={Resource}. Зарегистрируйте явные метаданные.");
        }
    }
}
