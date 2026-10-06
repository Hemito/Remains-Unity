using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Diagnostics;
using Remains.Serialization;

namespace Remains.Content
{
    // Single-use append publication within one synchronous RoomContentInstaller.Apply.
    // Staging is a detached StateJson copy; existing live objects and transient state
    // are never replaced. This plan must not span callbacks, yields or world updates.
    public sealed class RoomContentPublication
    {
        private readonly WorldState live,staged;
        private readonly string baseline;
        private readonly long initialRevision;
        private readonly AppendTail[] lists;
        private long publishedRevision;
        private Stage stage;
        private enum Stage {Captured,Prepared,Consumed}

        public static RoomContentPublication Capture(WorldState live,string context=null)
        {
            if(live==null) throw new ArgumentNullException(nameof(live));
            WorldState staged;
            using(TransitionTiming.Measure("content.stage-copy",context)) staged=StateJson.Copy(live);
            using(TransitionTiming.Measure("content.stage-baseline",context)) return new RoomContentPublication(live,staged);
        }
        public WorldState Staged=>stage==Stage.Captured?staged:
            throw new InvalidOperationException("Подготовленная копия содержимого уже передана для публикации: "+live.worldId);

        private RoomContentPublication(WorldState live,WorldState staged)
        {
            if(live==null || staged==null || ReferenceEquals(live,staged))
                throw new InvalidOperationException("Публикация содержимого требует отдельной подготовленной копии мира.");
            if(live.revision!=staged.revision || live.worldId!=staged.worldId || live.branchId!=staged.branchId)
                throw new InvalidOperationException("Подготовленная копия принадлежит другому состоянию мира: "+live.worldId);
            this.live=live;this.staged=staged;initialRevision=staged.revision;
            lists=new AppendTail[] {
                new AppendTail<ActorState>(live.actors,staged.actors,()=>live.actors,()=>staged.actors,"actors"),
                new AppendTail<DoorState>(live.doors,staged.doors,()=>live.doors,()=>staged.doors,"doors"),
                new AppendTail<PropState>(live.props,staged.props,()=>live.props,()=>staged.props,"props"),
                new AppendTail<ContainerState>(live.containers,staged.containers,()=>live.containers,()=>staged.containers,"containers"),
                new AppendTail<MovingPlatformState>(live.movingPlatforms,staged.movingPlatforms,()=>live.movingPlatforms,()=>staged.movingPlatforms,"movingPlatforms"),
                new AppendTail<MechanismButtonState>(live.mechanismButtons,staged.mechanismButtons,()=>live.mechanismButtons,()=>staged.mechanismButtons,"mechanismButtons"),
                new AppendTail<string>(live.contentPatches,staged.contentPatches,()=>live.contentPatches,()=>staged.contentPatches,"contentPatches")
            };
            // Compare normalized staging JSON, never the pre-copy live JSON.
            baseline=StateJson.ToJson(staged);
        }

        public void Prepare()
        {
            if(stage!=Stage.Captured) throw new InvalidOperationException("План содержимого уже подготовлен или использован: "+live.worldId);
            stage=Stage.Consumed;
            foreach(var list in lists) list.CheckStaging();
            int patches=lists[lists.Length-1].AddedCount;
            if(patches<=0 || initialRevision>long.MaxValue-patches || staged.revision!=initialRevision+patches)
                throw new InvalidOperationException("Установка содержимого изменила ревизию без соответствующих новых версий: "+live.worldId);
            publishedRevision=staged.revision;
            foreach(var list in lists) list.DetachTail();
            staged.revision=initialRevision;
            if(!string.Equals(baseline,StateJson.ToJson(staged),StringComparison.Ordinal))
                throw new InvalidOperationException("Установка содержимого изменила прежние данные мира вместо добавления: "+live.worldId);
            stage=Stage.Prepared;
        }

        public void Publish(WorldState destination)
        {
            if(stage!=Stage.Prepared) throw new InvalidOperationException("План содержимого не подготовлен или уже использован: "+live.worldId);
            stage=Stage.Consumed;
            if(!ReferenceEquals(destination,live) || live.revision!=initialRevision)
                throw new InvalidOperationException("Мир публикации изменился после подготовки содержимого: "+live.worldId);
            // Check all destinations before appending the first object. Reserve all
            // capacity first so ordinary validation/allocation cannot publish a prefix.
            foreach(var list in lists) list.CheckDestination();
            foreach(var list in lists) list.Reserve();
            foreach(var list in lists) list.Append();
            live.revision=publishedRevision;
        }

        private abstract class AppendTail
        {
            public abstract int AddedCount {get;}
            public abstract void CheckStaging();
            public abstract void DetachTail();
            public abstract void CheckDestination();
            public abstract void Reserve();
            public abstract void Append();
        }
        private sealed class AppendTail<T> : AppendTail
        {
            private readonly List<T> live,staged;
            private readonly Func<List<T>> liveList,stagedList;
            private readonly string name;
            private readonly int count;
            private T[] tail;
            public AppendTail(List<T> live,List<T> staged,Func<List<T>> liveList,Func<List<T>> stagedList,string name)
            {
                if(live==null || staged==null || live.Count!=staged.Count || ReferenceEquals(live,staged))
                    throw new InvalidOperationException("Нет отдельного списка подготовленного содержимого: "+name);
                this.live=live;this.staged=staged;this.liveList=liveList;this.stagedList=stagedList;this.name=name;count=staged.Count;
            }
            public override int AddedCount=>staged.Count-count;
            public override void CheckStaging()
            {
                if(!ReferenceEquals(staged,stagedList()) || staged.Count<count)
                    throw new InvalidOperationException("Установка содержимого заменила список или удалила прежние записи: "+name);
            }
            public override void DetachTail()
            {
                int added=AddedCount;tail=new T[added];staged.CopyTo(count,tail,0,added);staged.RemoveRange(count,added);
            }
            public override void CheckDestination()
            {
                if(!ReferenceEquals(live,liveList()) || live.Count!=count)
                    throw new InvalidOperationException("Живой список изменился после подготовки содержимого: "+name);
            }
            public override void Reserve()
            {
                int required=checked(count+tail.Length);if(live.Capacity<required) live.Capacity=required;
            }
            public override void Append()=>live.AddRange(tail);
        }
    }
}
