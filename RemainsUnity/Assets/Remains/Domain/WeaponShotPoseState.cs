using System;
using Remains.Serialization;

namespace Remains.Domain
{
    [Serializable]
    public sealed class WeaponRecoilPose : ExtensibleState
    {
        public int version=1;
        public float offsetX,angleDegrees;
        public long pauseRevision=-1;
        public WeaponRecoilPose Copy()=>StateJson.Copy(this);
        public bool IsValid=>version==1 && float.IsFinite(offsetX) && float.IsFinite(angleDegrees) && pauseRevision>=-1;
        public void Validate(string context)
        {
            if(!IsValid)
                throw new InvalidOperationException($"Некорректная поза отдачи: {context}, version={version}, offset={offsetX}, angle={angleDegrees}, pauseRevision={pauseRevision}.");
        }
        public static WeaponRecoilPose Neutral(long pauseRevision)=>new() {pauseRevision=pauseRevision};
    }

    // Historical owner sample before spread and before the new shot's impulse.
    // The accepted projectile retains this context when its simulation changes owner.
    [Serializable]
    public sealed class RangedShotPose : ExtensibleState
    {
        public int version=1;
        public string worldId,weaponItem;
        public float rootX,rootY,angleDegrees;
        public bool mirrored;
        public long selectionRevision;
        public int actorGeneration,actorLifecycle;
        public bool hasCasing;
        public float casingX,casingY;
        public void Validate(string context,bool requireWorld=true)
        {
            if(version!=1 || requireWorld && string.IsNullOrWhiteSpace(worldId) ||
                !float.IsFinite(rootX) || !float.IsFinite(rootY) || !float.IsFinite(angleDegrees) ||
                selectionRevision<0 || actorGeneration<0 || actorLifecycle<0 ||
                hasCasing && (!float.IsFinite(casingX) || !float.IsFinite(casingY)))
                throw new InvalidOperationException($"Некорректная сохранённая поза выстрела: {context}, version={version}, world={worldId}, item={weaponItem}, selection={selectionRevision}, generation={actorGeneration}, lifecycle={actorLifecycle}.");
        }
    }
}
