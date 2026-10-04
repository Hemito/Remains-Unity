using System;
using Remains.Domain;

namespace Remains.Content
{
    // Content installation is an explicit loading operation, never a per-frame rebuild.
    public sealed class RoomContentBatch
    {
        public DoorLayout doors;
        public PhysicalLayout physical;
        public NpcLayout npcs;
        public static RoomContentBatch Read(RoomAuthoring room,string level)=>new()
        {
            doors=RoomDoorContent.Read(room,level),physical=RoomPhysicalContent.Read(room,level),npcs=RoomNpcContent.Read(room,level)
        };
    }
    public static class RoomContentInstaller
    {
        public static bool NeedsInstall(WorldState world,RoomContentBatch batch)=>batch!=null &&
            (DoorContentInstaller.NeedsInstall(world,batch.doors) || PhysicalContentInstaller.NeedsInstall(world,batch.physical) ||
             NpcContentInstaller.NeedsInstall(world,batch.npcs));
        public static bool Apply(WorldState world,RoomContentBatch batch)
        {
            if(world==null || batch==null) throw new InvalidOperationException("Нет мира или набора содержимого комнаты.");
            var patches=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);string level=null;
            void Check(string patch,string nextLevel)
            {
                if(string.IsNullOrWhiteSpace(patch) || string.IsNullOrWhiteSpace(nextLevel) || !patches.Add(patch) || level!=null && level!=nextLevel)
                    throw new InvalidOperationException("Набор комнаты содержит повтор версии или разные уровни: "+patch);
                level=nextLevel;
            }
            if(batch.doors!=null) Check(batch.doors.patch,batch.doors.level);
            if(batch.physical!=null) Check(batch.physical.patch,batch.physical.level);
            if(batch.npcs!=null) Check(batch.npcs.patch,batch.npcs.level);
            if(!NeedsInstall(world,batch)) return false;
            // Validate the whole batch on a detached world before touching any live objects.
            // Individual installers are deterministic, have no callbacks and perform no I/O.
            var staged=Remains.Serialization.StateJson.Copy(world);
            Install(staged,batch);
            // Preserve references held by movement, travel and ownership consumers.
            Install(world,batch);
            return true;
        }
        private static void Install(WorldState world,RoomContentBatch batch)
        {
            if(batch.doors!=null) DoorContentInstaller.Apply(world,batch.doors);
            if(batch.physical!=null) PhysicalContentInstaller.Apply(world,batch.physical);
            if(batch.npcs!=null) NpcContentInstaller.Apply(world,batch.npcs);
        }
    }
}
