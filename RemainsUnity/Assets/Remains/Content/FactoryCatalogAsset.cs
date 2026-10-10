using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Каталог завода",fileName="FactoryCatalog")]
    public sealed class FactoryCatalogAsset : ScriptableObject
    {
        public const string DefinitionId="base:factory-generated-v1";
        public const string ResourceKey="Content/FactoryCatalog";
        [Serializable] public sealed class Entry
        {
            public RoomTemplateAsset template;
            public RoomTemplateInstance normal;
            public RoomTemplateInstance mirrored;
        }
        [SerializeField,InspectorName("Шаблоны и готовые префабы")] private Entry[] entries=Array.Empty<Entry>();
        [SerializeField,InspectorName("Определения форм и объектов")] private TextAsset definitions;
        [SerializeField,InspectorName("Настройки земель")] private TextAsset lands;
        [SerializeField,InspectorName("Настройки света")] private TextAsset lighting;
        [SerializeField,InspectorName("Графика")] private RoomTileArtAsset art;
        [NonSerialized] private Dictionary<string,RoomFile> templates;
        [NonSerialized] private Dictionary<string,Entry> byId;
        [NonSerialized] private RoomDefinitionCatalog parsedDefinitions;
        [NonSerialized] private RoomLandCatalog parsedLands;
        [NonSerialized] private string fingerprint;

        public IReadOnlyDictionary<string,RoomFile> Templates { get { Prepare();return templates; } }
        public RoomDefinitionCatalog Definitions { get { Prepare();return parsedDefinitions; } }
        public RoomLandCatalog Lands { get { Prepare();return parsedLands; } }
        public string ContentFingerprint { get { Prepare();return fingerprint; } }
        public RoomTileArtAsset Art=>art;
        public TextAsset Lighting=>lighting;

        public void Configure(Entry[] rooms,TextAsset forms,TextAsset landSettings,TextAsset lightSettings,RoomTileArtAsset graphics)
        {
            entries=rooms;definitions=forms;lands=landSettings;lighting=lightSettings;art=graphics;
            Reset();Prepare();
        }
        private void Reset() {templates=null;byId=null;parsedDefinitions=null;parsedLands=null;fingerprint=null;}
        private void OnEnable()=>Reset();
        private void OnValidate()
        {
            Reset();
            try {Prepare();} catch(Exception error) {Debug.LogWarning(error.Message,this);}
        }
        private void Prepare()
        {
            if(templates!=null) return;
            if(entries==null || entries.Length==0 || definitions==null || lands==null || lighting==null || art==null)
                throw new InvalidOperationException($"Каталог завода {name}: отсутствуют шаблоны или обязательные ссылки на данные.");
            var selected=new Dictionary<string,RoomFile>(StringComparer.Ordinal);
            var prefabs=new Dictionary<string,Entry>(StringComparer.Ordinal);
            var identity=new StringBuilder(definitions.text).Append('\n').Append(lands.text);
            foreach(var entry in entries.OrderBy(e=>e?.template?.Template,StringComparer.Ordinal))
            {
                if(entry?.template==null || entry.normal==null || entry.mirrored==null)
                    throw new InvalidOperationException($"Каталог завода {name}: нет шаблона или одного из префабов.");
                var room=entry.template.Room;
                if(room.Template!=entry.template.Template || RoomLandCatalog.LandOf(room.Template)!="plant" || !selected.TryAdd(room.Template,room))
                    throw new InvalidOperationException($"Каталог завода {name}: неверный или повторный шаблон {room.Template}.");
                Check(entry.normal,entry.template,false);Check(entry.mirrored,entry.template,true);
                prefabs.Add(room.Template,entry);
                identity.Append('\n').Append(RoomSerializer.Write(room));
            }
            var forms=RoomDefinitionCatalog.Read(definitions.text,definitions.name);
            var landOptions=RoomLandCatalog.Read(lands.text,lands.name);
            using var sha=SHA256.Create();
            string hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString()))).Replace("-","").ToLowerInvariant();
            byId=prefabs;parsedDefinitions=forms;parsedLands=landOptions;fingerprint=hash;
            templates=selected; // publish only after every dependency passed
        }
        private static void Check(RoomTemplateInstance prefab,RoomTemplateAsset template,bool mirror)
        {
            if(prefab.Template!=template || prefab.TemplateId!=template.Template || prefab.Mirror!=mirror)
                throw new InvalidOperationException($"Каталог завода: префаб {prefab.name} не соответствует {template.Template}, mirror={mirror}.");
            var points=prefab.GetComponentsInChildren<RoomTemplateObjectPoint>(true);
            if(points.Length!=template.Room.Objects.Count) throw new InvalidOperationException($"Каталог завода: префаб {prefab.name}, точек {points.Length}, объектов {template.Room.Objects.Count}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var point in points)
            {
                if(point.Index<0 || point.Index>=template.Room.Objects.Count)
                    throw new InvalidOperationException($"Каталог завода: {prefab.name}, индекс точки {point.Index} вне шаблона.");
                var obj=template.Room.Objects[point.Index];
                if(string.IsNullOrWhiteSpace(point.Id) || !ids.Add(point.Id) || point.Id!=obj.Id || point.Type!=obj.Type)
                    throw new InvalidOperationException($"Каталог завода: {prefab.name}, точка {point.Index}/{point.Id}/{point.Type} не соответствует объекту {obj.Id}/{obj.Type}.");
            }
        }
        public RoomTemplateInstance Prefab(string template,bool mirror)
        {
            Prepare();
            if(!byId.TryGetValue(template,out var entry)) throw new InvalidOperationException($"Каталог завода {name}: нет префаба {template}, mirror={mirror}.");
            return mirror?entry.mirrored:entry.normal;
        }
        public static FactoryCatalogAsset Load()=>Resources.Load<FactoryCatalogAsset>(ResourceKey)
            ??throw new InvalidOperationException("Не подготовлен каталог завода: "+ResourceKey+". Выполните команду подготовки каталога в Editor.");
    }
}
