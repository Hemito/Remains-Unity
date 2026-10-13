using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class ExportedBinding
    {
        public string role,resourceId,rule,label,metadataXml,sheetJson;
        public int frame;
    }
    [Serializable] public sealed class ExportedItemEntry
    {
        public string key,category,sourceId,rawXml,displayName,description;
        public ExportedBinding[] graphics,sounds;
        public string[] issues;
    }
    [Serializable] public sealed class ExportedFrame
    {
        public int frame;
        public float zoom;
        [NonSerialized] public Sprite sprite;
        public string spriteResource;
        public Vector2 sourceOrigin;
        public string sourceHash;
    }
    [Serializable] public sealed class ExportedResourceEntry
    {
        public string id,kind,socketsJson,labelsJson;
        public ExportedFrame[] frames;
        public AudioClip[] sounds;
        public string[] issues;
    }

    // Immutable source definitions and authored references. Raw original parameters are
    // deliberately separate from WeaponDefinition's adapted simulation units.
    [CreateAssetMenu(menuName="Remains/Импортированный исходный контент")]
    public sealed class ExportedContentCatalog : ScriptableObject
    {
        public const string ResourcePath="Content/ExportedContent";
        public string sourceHash;
        public ExportedItemEntry[] items=Array.Empty<ExportedItemEntry>();
        public ExportedResourceEntry[] resources=Array.Empty<ExportedResourceEntry>();
        [HideInInspector] public string weaponBundlesJson,attachmentsJson;
        private Dictionary<string,ExportedItemEntry> itemIndex;
        private Dictionary<string,ExportedResourceEntry> resourceIndex;
        private readonly HashSet<string> warned=new(StringComparer.Ordinal);
        private static ExportedContentCatalog loaded;

        public static ExportedContentCatalog Load()
        {
            if(loaded!=null)return loaded;
            var asset=Resources.Load<ExportedContentCatalog>(ResourcePath);
            if(asset==null)throw new InvalidOperationException("Не установлен исходный каталог: "+ResourcePath);
            asset.Validate();return loaded=asset;
        }
        public void Configure(string hash,ExportedItemEntry[] entries,ExportedResourceEntry[] art,string weapons,string attachments)
        {
            sourceHash=hash;items=entries;resources=art;weaponBundlesJson=weapons;attachmentsJson=attachments;
            itemIndex=null;resourceIndex=null;warned.Clear();Validate();
        }
        private void OnValidate()
        {
            itemIndex=null;resourceIndex=null;
            try {Validate();}
            catch(InvalidOperationException error){Debug.LogWarning("Исходный каталог "+name+": "+error.Message,this);}
        }
        public void Validate()
        {
            if(itemIndex!=null)return;
            if(string.IsNullOrWhiteSpace(sourceHash)||items==null||resources==null)
                throw new InvalidOperationException("Неполный исходный каталог: "+name);
            var nextItems=new Dictionary<string,ExportedItemEntry>(StringComparer.Ordinal);
            var nextResources=new Dictionary<string,ExportedResourceEntry>(StringComparer.Ordinal);
            foreach(var entry in resources)
                if(entry==null||string.IsNullOrWhiteSpace(entry.id)||!nextResources.TryAdd(entry.id,entry))
                    throw new InvalidOperationException("Повторный/пустой исходный ресурс: "+entry?.id);
            foreach(var entry in items)
            {
                if(entry==null||string.IsNullOrWhiteSpace(entry.key)||string.IsNullOrWhiteSpace(entry.rawXml)||!nextItems.TryAdd(entry.key,entry))
                    throw new InvalidOperationException("Повторное/неполное исходное определение: "+entry?.key);
                foreach(var binding in entry.graphics??Array.Empty<ExportedBinding>())
                    if(!string.IsNullOrEmpty(binding.resourceId)&&!nextResources.ContainsKey(binding.resourceId))
                        throw new InvalidOperationException($"Отсутствует импортированный ресурс: item={entry.key}, role={binding.role}, resource={binding.resourceId}.");
                foreach(var binding in entry.sounds??Array.Empty<ExportedBinding>())
                    if(!string.IsNullOrEmpty(binding.resourceId)&&!nextResources.ContainsKey(binding.resourceId))
                        throw new InvalidOperationException($"Отсутствует импортированный звук: item={entry.key}, role={binding.role}, resource={binding.resourceId}.");
            }
            itemIndex=nextItems;resourceIndex=nextResources;
        }
        public ExportedItemEntry Require(string key)
        {Validate();return itemIndex.TryGetValue(key,out var entry)?entry:throw new InvalidOperationException("Нет исходного определения: "+key);}
        public bool TryItem(string id,out ExportedItemEntry entry)
        {
            Validate();entry=null;if(string.IsNullOrEmpty(id))return false;
            int suffix=id.IndexOf(":grade:",StringComparison.Ordinal);
            if(suffix>=0)
            {
                if(!int.TryParse(id.Substring(suffix+7),out int grade)||grade<0)return false;
                id=id.Substring(0,suffix);
            }
            if(id.Contains(":"))return itemIndex.TryGetValue(id,out entry);
            return itemIndex.TryGetValue("item:"+id,out entry)||itemIndex.TryGetValue("weapon:"+id,out entry);
        }
        public ExportedResourceEntry Resource(string id)
        {Validate();return resourceIndex.TryGetValue(id,out var entry)?entry:throw new InvalidOperationException("Нет исходного ресурса: "+id);}
        public Remains.Domain.ItemStack CreateFormalItem(string key,int grade,int count)
        {
            var entry=Require(key);
            if(count<=0||grade<0)throw new ArgumentOutOfRangeException(nameof(count),"Количество должно быть положительным, грейд — неотрицательным.");
            string id=entry.key;
            if(entry.category=="weapon")
            {
                string role="item-image-grade-"+grade;bool declared=false;
                foreach(var binding in entry.graphics??Array.Empty<ExportedBinding>())if(binding.role==role)declared=true;
                if(!declared)throw new InvalidOperationException($"Нет объявленного грейда: item={key}, grade={grade}.");
                id+=":grade:"+grade;
            }
            else if(grade!=0)throw new InvalidOperationException("Грейд задаётся отдельным исходным ID предмета: "+key);
            // A qualified ID cannot accidentally activate a legacy action (e.g. grenade/heal).
            return Remains.Domain.SharedInventory.Create(id,count);
        }
        public Sprite FrameSprite(ExportedFrame frame)
        {
            if(frame.sprite!=null)return frame.sprite;
            if(string.IsNullOrWhiteSpace(frame.spriteResource))return null;
            frame.sprite=Resources.Load<Sprite>(frame.spriteResource);
            if(frame.sprite==null)throw new InvalidOperationException("Нет импортированного спрайта: "+frame.spriteResource);
            return frame.sprite;
        }
        public bool TryIcon(string id,int grade,out Sprite sprite)
        {
            Validate();sprite=null;
            if(id==null)return false;
            int suffix=id.IndexOf(":grade:",StringComparison.Ordinal);
            if(suffix>=0&&(!int.TryParse(id.Substring(suffix+7),out grade)||grade<0))return false;
            // Prefer explicit category keys. Item and weapon may share a source ID.
            if(!TryItem(id,out var entry))return false;
            string role=entry.category=="weapon"?"item-image-grade-"+grade:"item-image";
            foreach(var binding in entry.graphics??Array.Empty<ExportedBinding>())
            {
                if(binding.role!=role)continue;
                if(string.IsNullOrEmpty(binding.resourceId))break;
                var art=Resource(binding.resourceId);
                int frame=binding.frame>0?binding.frame:1;
                foreach(var picture in art.frames??Array.Empty<ExportedFrame>())
                    if(picture.frame==frame){sprite=FrameSprite(picture);return sprite!=null;}
                break;
            }
            if(warned.Add(entry.key+":"+grade))Debug.LogWarning($"Нет импортированной иконки: item={entry.key}, grade={grade}, role={role}. Общая иконка допускается только по явной исходной привязке.",this);
            return false;
        }
        public void AddInventoryIcons(Dictionary<string,Sprite> target)
        {
            Validate();
            foreach(var entry in items)
            {
                if(entry.category!="item"&&entry.category!="weapon")continue;
                if(TryIcon(entry.key,0,out var sprite))
                {
                    target.TryAdd(entry.key,sprite);
                    target.TryAdd(entry.sourceId,sprite);
                }
            }
        }
    }
}
