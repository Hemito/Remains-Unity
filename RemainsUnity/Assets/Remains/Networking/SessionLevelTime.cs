using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct LevelTimeMessage : NetworkMessage
    {public string world;public long sequence;public double sentAt;public LevelTimeStamp[] clocks;}

    // Saved clocks remain canonical. Live prediction uses a separately confirmed
    // session epoch, never the wall time at which a recovery copy was received.
    public sealed class SessionLevelTime
    {
        private readonly CoopSession session;
        private readonly Func<string,bool> maySimulate;
        private LevelTimeAuthority authority;
        private readonly LevelTimeReplica replica=new();
        private readonly Dictionary<string,LevelTimePlayback> localClocks=new(StringComparer.Ordinal);
        private long sequence;
        private double nextPublish;
        public event Action EpochChanged;
        public long ClientSequence=>replica.Sequence;
        public SessionLevelTime(CoopSession session)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            maySimulate=level=>LevelActivity.HasConnectedPlayer(session.ServerWorld,level) && !session.Pause.IsServerPaused(level);
        }
        public void BeginServer() {authority=new LevelTimeAuthority(session.ServerWorld);nextPublish=0;}
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<LevelTimeMessage>(message=>
            {
                string previousWorld=replica.WorldId;
                if(!replica.Receive(message.world,message.sequence,message.sentAt,message.clocks)) return;
                if(previousWorld!=message.world) localClocks.Clear();
                var retained=new HashSet<string>(message.clocks.Select(clock=>clock.level),StringComparer.Ordinal);
                foreach(string level in localClocks.Keys.Where(level=>!retained.Contains(level)).ToArray()) localClocks.Remove(level);
                foreach(string level in retained) if(!localClocks.ContainsKey(level)) localClocks.Add(level,new LevelTimePlayback());
                EpochChanged?.Invoke();
            });
        }
        public bool HasLocal(string level)=>NetworkServer.active && session.ServerWorld!=null ||
            replica.Has(session.World?.worldId,level) && localClocks.TryGetValue(level,out var clock) && clock.Initialized;
        public double ReadLocal(string level)=>NetworkServer.active?ReadServer(level):
            HasLocal(level)?localClocks[level].ElapsedSeconds:throw new InvalidOperationException("Нет локального отсчёта уровня "+level);
        public bool RunsLocal(string level)=>NetworkServer.active?maySimulate(level):
            replica.Running(session.World?.worldId,level);
        public void EnsureLevel(string level)
        {
            if(!NetworkServer.active || session.ServerWorld==null) throw new InvalidOperationException("Нет координатора часов для добавления уровня "+level);
            if(LevelTimeMigration.Prepare(session.ServerWorld,new[]{level})) BeginServer();
        }
        public double ReadServer(string level)=>authority?.Read(level)??throw new InvalidOperationException("Координатор часов мира не запущен.");
        public void Step(double delta)
        {
            if(NetworkServer.active && session.ServerWorld!=null)
            {
                (authority??throw new InvalidOperationException("Координатор часов мира не запущен.")).Advance(delta,maySimulate);
                if(NetworkTime.time>=nextPublish) Publish();
            }
            else if(session.World!=null && replica.WorldId==session.World.worldId)
            {
                double now=NetworkTime.time;
                foreach(var pair in localClocks)
                    pair.Value.Advance(replica.Read(session.World.worldId,pair.Key,now),
                        replica.Running(session.World.worldId,pair.Key) && !session.Pause.IsPaused(pair.Key),delta);
            }
        }
        public void Publish()
        {
            if(!NetworkServer.active || session.ServerWorld==null || authority==null) return;
            var message=new LevelTimeMessage {world=session.ServerWorld.worldId,sequence=++sequence,sentAt=NetworkTime.time,
                clocks=session.ServerWorld.levelTimes.Select(clock=>new LevelTimeStamp
                {level=clock.level,elapsedSeconds=clock.elapsedSeconds,running=maySimulate(clock.level)}).ToArray()};
            NetworkServer.SendToAll(message);nextPublish=NetworkTime.time+.2;
        }
        public void ResetClient() {replica.Reset();localClocks.Clear();EpochChanged?.Invoke();}
        public void ResetServer() {authority=null;sequence=0;nextPublish=0;}
    }
}
