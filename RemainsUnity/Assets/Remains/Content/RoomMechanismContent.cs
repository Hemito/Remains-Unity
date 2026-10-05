using System;
using System.Linq;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class MechanismLayout
    {
        public string patch,level;
        public ContentExpansion expansion;
        public MovingPlatformDefinition[] platformDefinitions=Array.Empty<MovingPlatformDefinition>();
        public MechanismButtonDefinition[] buttonDefinitions=Array.Empty<MechanismButtonDefinition>();
        public MovingPlatformState[] movingPlatforms=Array.Empty<MovingPlatformState>();
        public MechanismButtonState[] mechanismButtons=Array.Empty<MechanismButtonState>();
    }
    public sealed class RoomMechanismContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки механизмов")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление механизмов")] private ContentExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(ContentExpansion value)=>expansion=Remains.Serialization.StateJson.Copy(value);
        public static MechanismLayout Read(RoomAuthoring room,string level)
        {
            var platforms=room.GetComponentsInChildren<RoomMovingPlatformPlacement>(true).Where(room.Included).ToArray();
            var buttons=room.GetComponentsInChildren<RoomMechanismButtonPlacement>(true).Where(room.Included).ToArray();
            var content=room.GetComponent<RoomMechanismContent>();
            if(content==null && platforms.Length==0 && buttons.Length==0) return null;
            if(content==null || string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException("Комната "+room.name+": у механизмов нет версии или уровня.");
            if(!content.enabled) return null;
            var result=new MechanismLayout {patch=level+":"+content.patch,level=level,
                expansion=ContentExpansionRules.InLevel(content.expansion,level),
                movingPlatforms=platforms.Select(p=>p.Read(room,level)).ToArray(),mechanismButtons=buttons.Select(b=>b.Read(room,level)).ToArray(),
                platformDefinitions=platforms.Select(p=>p.Definition).Distinct().Select(p=>p.CreateDefinition()).ToArray(),
                buttonDefinitions=buttons.Select(b=>b.Definition).Distinct().Select(b=>b.CreateDefinition()).ToArray()};
            MechanismContentInstaller.ValidateLayout(result);return result;
        }
        private void OnValidate()
        {
            if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning("Комната "+name+": не задана версия механизмов.",this);
            if(expansion!=null && string.IsNullOrWhiteSpace(expansion.previousPatch) && (expansion.additions?.Length>0 || expansion.earlierVersions?.Length>0))
                Debug.LogWarning("Комната "+name+": добавление механизмов требует предыдущей версии.",this);
        }
    }
}
