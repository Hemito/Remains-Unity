using System;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    // Canonical persistence adapter. No client clock prediction or passenger motion
    // is claimed here: the network motion-segment adapter is a separate next step.
    public sealed class SessionLevelTime
    {
        private readonly CoopSession session;
        private readonly Func<string,bool> maySimulate;
        private LevelTimeAuthority authority;
        public SessionLevelTime(CoopSession session)
        {
            this.session=session??throw new ArgumentNullException(nameof(session));
            maySimulate=level=>LevelActivity.HasConnectedPlayer(session.ServerWorld,level) && !session.Pause.IsServerPaused(level);
        }
        public void BeginServer()=>authority=new LevelTimeAuthority(session.ServerWorld);
        public void EnsureLevel(string level)
        {
            if(!NetworkServer.active || session.ServerWorld==null) throw new InvalidOperationException("Нет координатора часов для добавления уровня "+level);
            if(LevelTimeMigration.Prepare(session.ServerWorld,new[]{level})) BeginServer();
        }
        public double ReadServer(string level)=>authority?.Read(level)??throw new InvalidOperationException("Координатор часов мира не запущен.");
        public void Step(double delta)
        {
            if(NetworkServer.active && session.ServerWorld!=null)
                (authority??throw new InvalidOperationException("Координатор часов мира не запущен.")).Advance(delta,maySimulate);
        }
        public void ResetServer()=>authority=null;
    }
}
