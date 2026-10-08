using System;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Explicit capability migration is separate from append-only room installation.
    [Serializable] public sealed class DoorCapabilityLayout
    {
        public string patch,level;
        public DoorState[] doors;
    }

    public sealed class RoomDoorCapabilityContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия явного обновления прочности")] private string patch;
        [SerializeField,InspectorName("Только существующие ID дверей")] private string[] stableIds;
        public string Patch=>patch;
        public string[] StableIds=>stableIds==null?null:(string[])stableIds.Clone();
        public void Configure(string version,string[] ids)
        {
            Validate(version,ids,name);patch=version;stableIds=(string[])ids.Clone();
        }
        public static DoorCapabilityLayout[] Read(RoomAuthoring room,string level)
        {
            if(room==null || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException("Обновление дверей: нет комнаты или ID уровня.");
            var registrations=room.GetComponentsInChildren<RoomDoorCapabilityContent>(true).Where(room.Included).Where(r=>r.enabled).ToArray();
            if(registrations.Length==0) return Array.Empty<DoorCapabilityLayout>();
            var initial=RoomDoorContent.Read(room,level)??throw new InvalidOperationException($"Обновление дверей: room={room.name}, level={level}, нет исходной расстановки.");
            var result=registrations.Select(registration=>
            {
                Validate(registration.patch,registration.stableIds,registration.name);
                var ids=registration.stableIds.Select(id=>level+":"+id).ToArray();
                var selected=ids.Select(id=>
                {
                    var matches=initial.doors.Where(d=>d.id==id).ToArray();
                    if(matches.Length!=1) throw new InvalidOperationException($"Обновление дверей: room={room.name}, level={level}, patch={registration.patch}, id={id}, расстановок={matches.Length}.");
                    return StateJson.Copy(matches[0]);
                }).ToArray();
                return new DoorCapabilityLayout {patch=level+":"+registration.patch,level=level,doors=selected};
            }).ToArray();
            ContentEntityIds.ValidateCandidates(result.SelectMany(r=>r.doors).Select(d=>new ContentEntityIdentity(ContentEntityKind.Door,d.id)),"обновление дверей / room="+room.name);
            if(result.Select(r=>r.patch).Distinct(StringComparer.Ordinal).Count()!=result.Length)
                throw new InvalidOperationException("Обновление дверей: повтор версии в комнате "+room.name);
            return result;
        }
        private static void Validate(string version,string[] ids,string context)
        {
            if(string.IsNullOrWhiteSpace(version) || ids==null || ids.Length==0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)
                throw new InvalidOperationException($"Обновление дверей: неполная или повторная регистрация; context={context}, patch={version}.");
        }
        private void OnValidate()
        {try {Validate(patch,stableIds,name);} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
