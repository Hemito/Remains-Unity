using System;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // Imported *.room.json (docs/pipelines/room-format.md). The text is the source of truth;
    // the parsed model is cached per asset instance and never edited in place.
    public sealed class RoomTemplateAsset : ScriptableObject
    {
        [SerializeField,InspectorName("ID шаблона")] private string template;
        [SerializeField,HideInInspector] private string source;
        [NonSerialized] private RoomFile parsed;

        public string Template=>template;

        public void Configure(string templateId,string text)
        {
            if(string.IsNullOrWhiteSpace(templateId) || string.IsNullOrEmpty(text))
                throw new ArgumentException("Шаблон комнаты: пустой ID или текст.");
            template=templateId;source=text;parsed=null;
        }

        public RoomFile Room
        {
            get
            {
                if(parsed!=null) return parsed;
                if(string.IsNullOrEmpty(source)) throw new InvalidOperationException($"Шаблон комнаты {name}: нет исходного текста (переимпортируйте файл).");
                parsed=RoomSerializer.Read(source,name);
                return parsed;
            }
        }
    }
}
