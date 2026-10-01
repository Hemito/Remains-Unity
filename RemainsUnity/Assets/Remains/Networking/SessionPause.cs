using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;

namespace Remains.Networking
{
    public struct PauseIntentMessage : NetworkMessage
    {public int generation,lifecycle;public bool menu,inventory;}
    public struct ManualPauseMessage : NetworkMessage
    {public int generation,lifecycle;public bool paused;}
    public struct PauseStateMessage : NetworkMessage
    {public long revision;public bool pauseOnMenu,pauseOnInventory;public string[] levels;}

    // Ephemeral session state: no menu intent is written into recovery/world saves.
    public sealed class SessionPause
    {
        private readonly CoopSession session;
        private LevelPauseCoordinator coordinator;
        private PauseStateMessage published;
        private readonly HashSet<string> pausedLevels=new(StringComparer.Ordinal);
        public long Revision {get;private set;}=-1;
        public bool PauseOnMenu {get;private set;}
        public bool PauseOnInventory {get;private set;}
        public event Action Changed;
        public SessionPause(CoopSession session)=>this.session=session;
        public bool IsPaused(string level)=>level!=null && pausedLevels.Contains(level);
        public bool IsServerPaused(string level)=>coordinator!=null && coordinator.IsPaused(session.ServerWorld,level);
        public void RegisterServer()
        {
            coordinator=new LevelPauseCoordinator(session.Rules.Pause);
            published=new PauseStateMessage {revision=0,levels=Array.Empty<string>()};
            NetworkServer.RegisterHandler<PauseIntentMessage>((connection,message)=>
                Apply(connection,coordinator.SetIntent(session.ConnectedActor(connection),message.generation,message.lifecycle,message.menu,message.inventory)));
            NetworkServer.RegisterHandler<ManualPauseMessage>((connection,message)=>
                Apply(connection,coordinator.SetManual(session.ConnectedActor(connection),message.generation,message.lifecycle,message.paused)));
        }
        public void RegisterClient()
        {
            ResetClient();
            NetworkClient.RegisterHandler<PauseStateMessage>(Receive);
        }
        public void ResetClient()
        {Revision=-1;pausedLevels.Clear();PauseOnMenu=PauseOnInventory=false;Changed?.Invoke();}
        public void ResetServer()
        {coordinator=null;published=default;}
        public void SetSettings(bool menu,bool inventory)
        {
            if(!NetworkServer.active || coordinator==null) return;
            coordinator.PauseOnMenu=menu;coordinator.PauseOnInventory=inventory;Publish();
        }
        public void SetIntent(bool menu,bool inventory)
        {
            var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected) return;
            NetworkClient.Send(new PauseIntentMessage {generation=actor.generation,lifecycle=actor.lifecycleSequence,menu=menu,inventory=inventory});
        }
        public void SetManual(bool paused)
        {
            var actor=session.LocalActor;
            if(actor==null || !NetworkClient.isConnected) return;
            NetworkClient.Send(new ManualPauseMessage {generation=actor.generation,lifecycle=actor.lifecycleSequence,paused=paused});
        }
        private void Apply(NetworkConnectionToClient connection,string reason)
        {
            if(reason!=null) {connection.Send(new NoticeMessage {text=reason});return;}
            Publish();
        }
        public void Publish()
        {
            if(!NetworkServer.active || coordinator==null) return;
            coordinator.Reconcile(session.ServerWorld);
            var levels=session.ServerWorld==null?Array.Empty<string>():session.ServerWorld.actors
                .Where(actor=>actor.player && actor.connected).Select(actor=>actor.level).Distinct(StringComparer.Ordinal)
                .Where(level=>coordinator.IsPaused(session.ServerWorld,level)).OrderBy(level=>level,StringComparer.Ordinal).ToArray();
            if(published.pauseOnMenu!=coordinator.PauseOnMenu || published.pauseOnInventory!=coordinator.PauseOnInventory ||
                !levels.SequenceEqual(published.levels))
                published=new PauseStateMessage {revision=published.revision+1,pauseOnMenu=coordinator.PauseOnMenu,
                    pauseOnInventory=coordinator.PauseOnInventory,levels=levels};
            // Also send an unchanged state after joins/world transitions so late clients see it.
            NetworkServer.SendToAll(published);
        }
        private void Receive(PauseStateMessage message)
        {
            if(message.revision<=Revision) return;
            Revision=message.revision;PauseOnMenu=message.pauseOnMenu;PauseOnInventory=message.pauseOnInventory;
            pausedLevels.Clear();foreach(string level in message.levels) pausedLevels.Add(level);
            Changed?.Invoke();
        }
    }
}
