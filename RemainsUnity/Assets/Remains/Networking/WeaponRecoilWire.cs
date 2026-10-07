using System.IO;
using Mirror;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Networking
{
    // Only recoil is customized; body pose keeps its independent message contract.
    public static class WeaponRecoilWire
    {
        public static void WriteWeaponRecoilPose(this NetworkWriter writer,WeaponRecoilPose value)
        {
            writer.WriteBool(value!=null);
            if(value==null) return;
            writer.WriteInt(value.version);
            writer.WriteFloat(value.offsetX);
            writer.WriteFloat(value.angleDegrees);
            writer.WriteLong(value.pauseRevision);
            // Rare mod data uses the strict DTO serializer, including extension-name
            // and numeric precision validation. Ordinary samples allocate no JSON.
            writer.WriteString(value.extensions?.Count>0?StateJson.ToJson(value):null);
        }
        public static WeaponRecoilPose ReadWeaponRecoilPose(this NetworkReader reader)
        {
            if(!reader.ReadBool()) return null;
            var value=new WeaponRecoilPose
            {
                version=reader.ReadInt(),offsetX=reader.ReadFloat(),angleDegrees=reader.ReadFloat(),pauseRevision=reader.ReadLong()
            };
            string extended=reader.ReadString();
            if(extended!=null)
            {
                var parsed=StateJson.FromJson<WeaponRecoilPose>(extended);
                if(parsed==null || parsed.version!=value.version || !parsed.offsetX.Equals(value.offsetX) ||
                    !parsed.angleDegrees.Equals(value.angleDegrees) || parsed.pauseRevision!=value.pauseRevision)
                    throw new InvalidDataException("Сетевой образец отдачи содержит противоречащие друг другу поля compact и extensions.");
                value.extensions=parsed.extensions;
            }
            // Known invalid samples reach contextual recoil validation, which resets
            // recoil without rejecting otherwise correct body motion.
            return value;
        }
    }
}
