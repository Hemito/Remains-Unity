using System;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ContainerAppearanceState : ExtensibleState
    {
        public int version;
        public bool opened;
        public long revision;
        public void Validate(string id)
        {
            if(version is not (0 or 1) || revision<0 || !opened && revision!=0 || opened && (version==0 || revision==0))
                throw new InvalidOperationException($"Некорректный вид контейнера: id={id}, version={version}, opened={opened}, revision={revision}.");
        }
    }
}
