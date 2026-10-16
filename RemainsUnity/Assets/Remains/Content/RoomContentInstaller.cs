using System;
using Remains.Domain;
using Remains.Diagnostics;

namespace Remains.Content
{
    // Content installation is an explicit loading operation, never a per-frame rebuild.
    public sealed class RoomContentBatch
    {
        public DoorLayout doors;
        public PhysicalLayout physical;
        public NpcLayout npcs;
        public MechanismLayout mechanisms;
        public MineLayout mines;
        public HazardLayout hazards;
        public TravelPoint[] travelPoints;
        public static RoomContentBatch Read(RoomAuthoring room,string level)=>new()
        {
            doors=RoomDoorContent.Read(room,level),physical=RoomPhysicalContent.Read(room,level),npcs=RoomNpcContent.Read(room,level),
            travelPoints=RoomTravelPointAuthoring.ReadAll(room,level),mechanisms=RoomMechanismContent.Read(room,level),
            mines=RoomMineContent.Read(room,level),hazards=RoomHazardContent.Read(room,level)
        };
    }
    public static class RoomContentInstaller
    {
        public static bool NeedsInstall(WorldState world,RoomContentBatch batch)=>batch!=null &&
            (DoorContentInstaller.NeedsInstall(world,batch.doors) || PhysicalContentInstaller.NeedsInstall(world,batch.physical) ||
             NpcContentInstaller.NeedsInstall(world,batch.npcs) || MechanismContentInstaller.NeedsInstall(world,batch.mechanisms) ||
             MineContentInstaller.NeedsInstall(world,batch.mines) || HazardContentInstaller.NeedsInstall(world,batch.hazards));
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
            if(batch.mechanisms!=null) Check(batch.mechanisms.patch,batch.mechanisms.level);
            if(batch.mines!=null) Check(batch.mines.patch,batch.mines.level);
            if(batch.hazards!=null) Check(batch.hazards.patch,batch.hazards.level);
            if(!NeedsInstall(world,batch)) return false;
            // Validate the whole batch on a detached world before touching any live objects.
            // Individual installers are the sole rule source, have no callbacks and perform no I/O.
            var publication=RoomContentPublication.Capture(world,level);
            using(TransitionTiming.Measure("content.stage-validation",level)) Install(publication.Staged,batch);
            using(TransitionTiming.Measure("content.stage-append-validation",level)) publication.Prepare();
            // Preserve references held by movement, travel and ownership consumers.
            using(TransitionTiming.Measure("content.publish",level)) publication.Publish(world);
            return true;
        }
        private static void Install(WorldState world,RoomContentBatch batch)
        {
            if(batch.doors!=null) using(TransitionTiming.Measure("content.doors",batch.doors.level)) DoorContentInstaller.Apply(world,batch.doors);
            if(batch.physical!=null) using(TransitionTiming.Measure("content.physical",batch.physical.level)) PhysicalContentInstaller.Apply(world,batch.physical);
            if(batch.npcs!=null) using(TransitionTiming.Measure("content.npcs",batch.npcs.level)) NpcContentInstaller.Apply(world,batch.npcs);
            if(batch.mechanisms!=null) using(TransitionTiming.Measure("content.mechanisms",batch.mechanisms.level)) MechanismContentInstaller.Apply(world,batch.mechanisms);
            if(batch.mines!=null) using(TransitionTiming.Measure("content.mines",batch.mines.level)) MineContentInstaller.Apply(world,batch.mines);
            if(batch.hazards!=null) using(TransitionTiming.Measure("content.hazards",batch.hazards.level)) HazardContentInstaller.Apply(world,batch.hazards);
        }
    }
}
