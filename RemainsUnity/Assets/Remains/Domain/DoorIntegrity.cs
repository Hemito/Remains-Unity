using System;
using System.Collections.Generic;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable]
    public sealed class DoorIntegrity : ExtensibleState
    {
        public int version=1;
        public float maxHealth,health;
        public List<string> resolvedAttacks=new();
        public void Validate(string context)
        {
            if(version!=1 || !float.IsFinite(maxHealth) || maxHealth<=0 || !float.IsFinite(health) || health<0 || health>maxHealth || resolvedAttacks==null)
                throw new InvalidOperationException($"Некорректная прочность проёма {context}: version={version}, health={health}/{maxHealth}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in resolvedAttacks)
                if(string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidOperationException($"Некорректный журнал повреждений проёма {context}: attack={id ?? "<null>"}.");
        }
    }
}
