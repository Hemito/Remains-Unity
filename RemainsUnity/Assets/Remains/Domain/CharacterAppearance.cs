using System;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable]
    public sealed class CharacterAppearance : ExtensibleState
    {
        public int version;
        public int hairStyle=1,eyeStyle=1;
        public int fur=0xA3A3A3,hair=0x854609,secondaryHair=0xFFFFFF,eyes=0x16F343,magic=0x00FF00;
        public bool secondaryHairEnabled;

        public void Validate(string context)
        {
            if(version!=1 || hairStyle<1 || eyeStyle<1)
                throw new InvalidOperationException($"Внешность {context}: неизвестные версия/форма, version={version}, hair={hairStyle}, eyes={eyeStyle}.");
            CheckColor(fur,"шерсть",context);CheckColor(hair,"грива",context);
            CheckColor(secondaryHair,"вторая грива",context);CheckColor(eyes,"глаза",context);CheckColor(magic,"магия",context);
        }
        private static void CheckColor(int rgb,string slot,string context)
        {
            if(rgb<0 || rgb>0xFFFFFF) throw new InvalidOperationException($"Внешность {context}: недопустимый RGB24, область={slot}, значение={rgb}.");
        }
    }
}
