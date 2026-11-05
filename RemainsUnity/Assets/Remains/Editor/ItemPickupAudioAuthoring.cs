using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ItemPickupAudioAuthoring
    {
        private const string Output="Assets/Remains/Resources/Presentation/item-pickup-audio.json";
        private const string General="Original/Audio/ItemLandings/ammo";
        public static void BuildBatch()
        {
            try {Build();ItemPickupAudioChecks.Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Build()
        {
            var entries=new Dictionary<string,ItemPickupAudioCatalog.Entry>(StringComparer.Ordinal);
            foreach(var source in ExportedContentCatalog.Load().items.Where(e=>e.category is "item" or "weapon"))
            {
                var fall=(source.sounds??Array.Empty<ExportedBinding>()).Where(s=>s.role=="fall").ToArray();
                if(fall.Length>1)throw new InvalidDataException("Несколько исходных fall-звуков: "+source.key);
                string clip=General,reason=null,rule="Авторское назначение pickup: исходный общий fall_item (Loot.sndFall).";
                if(fall.Length==1)
                {
                    clip=string.IsNullOrEmpty(fall[0].resourceId)?null:AudioClipSource.ExportedPrefix+fall[0].resourceId;
                    reason=clip==null?"Не разрешён исходный звук: "+fall[0].rule:null;
                    rule="Авторский reuse исходного fall для pickup: "+fall[0].rule;
                }
                else
                {
                    var xml=XElement.Parse(source.rawXml);
                    if((string)xml.Attribute("tip") is "book" or "scheme")
                    {clip="exported:sound:71";rule="Авторский reuse fall_paper для book/scheme (Loot.as) при pickup.";}
                }
                entries.Add(source.key,new ItemPickupAudioCatalog.Entry {item=source.key,clip=clip,rule=rule,unresolvedReason=reason});
            }
            // Preserve the game's explicit legacy IDs; never infer them from a partial string.
            var legacy=JsonUtility.FromJson<ItemLandingAudioCatalog.Settings>(GameAssets.Load<TextAsset>(ItemLandingAudioCatalog.Resource).text);
            foreach(var entry in legacy.entries)
                entries.Add(entry.item,new ItemPickupAudioCatalog.Entry {item=entry.item,clip=entry.clip,unresolvedReason=entry.unresolvedReason,
                    rule="Авторский reuse назначения legacy landing для pickup: "+entry.sourceItem+"/"+entry.sourceSound});
            var texts=JsonUtility.FromJson<ItemTextManifest>(GameAssets.Load<TextAsset>(ItemTextCatalog.Resource).text);
            foreach(var item in texts.items)
                if(!entries.ContainsKey(item.id))entries.Add(item.id,new ItemPickupAudioCatalog.Entry {item=item.id,clip=General,
                    rule="Явное авторское назначение pickup для штатного предмета Unity: общий исходный fall_item."});
            var settings=new ItemPickupAudioCatalog.Settings {schema=1,entries=entries.Values.OrderBy(e=>e.item,StringComparer.Ordinal).ToArray()};
            var checkedCatalog=ItemPickupAudioCatalog.Create(settings,AudioClipSource.Load);
            File.WriteAllText(Output,JsonUtility.ToJson(settings,true));AssetDatabase.ImportAsset(Output,ImportAssetOptions.ForceUpdate);
            Debug.Log($"Звуки подбора: подготовлено {checkedCatalog.Count}явных назначений, неразрешённых {settings.entries.Count(e=>e.clip==null)}; исходные звуки приземления переиспользованы авторским решением.");
        }
    }
}
