using System;
using System.Collections.Generic;
using Remains.Serialization;

namespace Remains.Domain
{
    public static class MechanismRepairStates
    {
        public const string NeedsRepair="needs-repair",Ready="ready",Broken="broken";
        public static bool BuiltIn(string id)=>id==NeedsRepair || id==Ready || id==Broken;
    }
    [Serializable]
    public sealed class MechanismButtonState : ExtensibleState
    {
        public int schema=1;
        public string id,definition,level,roomId,groupId,platformId,repairState;
        public long revision;
        public float x,y;
        public bool pressed,removed;
        // Instead of a platform: a door group it swaps (Location.allAct), or with an area size the group that opens when a
        // player walks in (Area.as). An area has no picture and no use key; pressed stays on, as the original's fires once.
        public List<string> doors=new();
        public float areaWidth,areaHeight;
        public bool DrivesDoors=>doors?.Count>0;
        public bool IsArea=>areaWidth>0;
        public void Validate()=>Validate(MechanismRepairStates.BuiltIn);
        // Mods may supply their registered state predicate. Acceptance here does not
        // implement a repair rule, decide success, choose a duration or consume items.
        public void Validate(Func<string,bool> supportedRepairState)
        {
            if(supportedRepairState==null) throw new ArgumentNullException(nameof(supportedRepairState));
            MechanismStateValidation.Placement(schema,id,definition,level,roomId,groupId,revision);
            if(DrivesDoors)
            {
                MechanismStateValidation.Require(platformId==null,"кнопка дверей не ведёт платформу",id);
                foreach(string door in doors) MechanismStateValidation.Require(MechanismStateValidation.ScopedId(door,level),"дверь группы не принадлежит level",id);
            }
            else MechanismStateValidation.Require(MechanismStateValidation.ScopedId(platformId,level),"platformId не принадлежит level",id);
            MechanismStateValidation.Require(float.IsFinite(areaWidth) && float.IsFinite(areaHeight) && areaWidth>=0 && areaHeight>=0 &&
                (areaWidth>0)==(areaHeight>0) && (areaWidth==0 || DrivesDoors),"area",id);
            MechanismStateValidation.Require(float.IsFinite(x) && float.IsFinite(y),"button position",id);
            if(string.IsNullOrWhiteSpace(repairState) || !supportedRepairState(repairState))
                throw new InvalidOperationException($"Кнопка {id}: неизвестное состояние ремонта '{repairState ?? "<нет>"}', definition={definition}, level={level}, room={roomId}, group={groupId}.");
        }
        public void ValidateLink(MovingPlatformState platform)=>ValidateLink(platform,MechanismRepairStates.BuiltIn);
        public void ValidateLink(MovingPlatformState platform,Func<string,bool> supportedRepairState)
        {
            Validate(supportedRepairState);
            if(DrivesDoors) return;
            if(platform==null) throw new InvalidOperationException($"Кнопка {id}: отсутствует платформа {platformId}.");
            platform.Validate();
            MechanismStateValidation.Require(platformId==platform.id &&
                MechanismStateValidation.SameGroup(level,roomId,groupId,platform.level,platform.roomId,platform.groupId),
                "button/platform ID или room-scoped group mismatch",id);
        }
    }
}
