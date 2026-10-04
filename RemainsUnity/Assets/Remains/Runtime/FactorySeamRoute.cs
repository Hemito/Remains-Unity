using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct FactorySeamViewed : NetworkMessage {}
    public struct FactorySeamLoot : NetworkMessage {public string item,definition;public int count;}
    // Only input intents and UI clicks. No position injection or travel call at the room seam.
    internal sealed class FactorySeamRoute
    {
        private readonly PrototypeRuntime runtime;
        private readonly HashSet<int> viewers=new();
        private int phase,generation,lifecycle,inventoryBefore;
        private string actorId,owner,claimedItem,definition,capture;
        private RoomLayout initialRoom;
        private int quantity;
        private bool jumped,opened,claiming,crossed,shared;
        public bool Complete=>phase==10 && crossed && shared;
        public string Progress=>$"seamPhase={phase}, opened={opened}, shared={shared}, claiming={claiming}, item={claimedItem}";
        public string Failure {get;private set;}
        private ActorState Actor=>runtime.Session.LocalActor;
        private bool Host=>runtime.Options.Role=="host";
        private ContainerState Container=>runtime.Session.World.containers.Single(c=>c.id=="factory:0:original:plant:18:sjN3zN7dP8aCZGe6");
        public FactorySeamRoute(PrototypeRuntime value)
        {
            runtime=value;
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<FactorySeamViewed>((connection,_)=>viewers.Add(connection.connectionId));
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<FactorySeamLoot>(message=>
            {claimedItem=message.item;definition=message.definition;quantity=message.count;});
        }
        public void Begin()
        {
            phase=1;generation=Actor.generation;lifecycle=Actor.lifecycleSequence;actorId=Actor.id;owner=Actor.owner;
            initialRoom=runtime.Geometry.Get("factory:0");
        }
        public PlayerCommand Command()
        {
            if(Failure!=null || Complete) return default;
            if(phase==1) return Move(16.22f);
            if(phase==2) return new PlayerCommand {movement=Vector2.up};
            if(phase==3)
            {
                if(!jumped) {jumped=true;return new PlayerCommand {movement=Vector2.right,jump=true};}
                return Move(40.4f);
            }
            if(phase==4 && !opened)
            {
                var target=InteractionSelection.Select(runtime.Session.World,Actor,runtime.Session.Rules,runtime.Session.Travel.Points(Actor.level));
                if(target.kind!=InteractionKind.Container || target.id!=Container.id) {Failure="E в цехе выбирает другой объект: "+target.kind;return default;}
                opened=true;return new PlayerCommand {interact=true};
            }
            if(phase==7) return runtime.Hud.InventoryOpen?new PlayerCommand {inventory=true}:Move(16.22f);
            if(phase==8) return new PlayerCommand {movement=Vector2.down};
            if(phase==9)
            {
                if(!jumped) {jumped=true;return new PlayerCommand {movement=Vector2.left,jump=true,jumpHeld=true,sprint=true};}
                var command=Move(initialRoom.spawnX);command.jumpHeld=Actor.x>11;command.sprint=Actor.x>11;return command;
            }
            return default;
        }
        private PlayerCommand Move(float x)=>new() {movement=Vector2.right*(Mathf.Abs(Actor.x-x)>.06f?Mathf.Sign(x-Actor.x):0)};
        public void Tick()
        {
            if(Failure!=null || Complete) return;
            if(Actor.id!=actorId || Actor.owner!=owner || Actor.generation!=generation || Actor.lifecycleSequence!=lifecycle ||
                Actor.level!="factory:0" || runtime.Session.Travel.Pending || !ReferenceEquals(initialRoom,runtime.Geometry.Get("factory:0")))
            {Failure="Проход между комнатами изменил актёра, управление, поколение, локацию или геометрию.";return;}
            if(Actor.down) {Failure="Персонаж потерял сознание на маршруте через стык.";return;}
            if(phase==1 && Mathf.Abs(Actor.x-16.22f)<.08f) phase=2;
            else if(phase==2 && Actor.y>=10.39f) {phase=3;jumped=false;}
            else if(phase==3 && Mathf.Abs(Actor.x-40.4f)<.08f && ActorTerrainMotion.Supported(initialRoom,Anatomy.Movement(Actor),Actor.Position))
            {phase=4;crossed=true;capture="workshop";}
            else if(phase==4 && runtime.Hud.ContainerOpen)
            {
                if(runtime.Hud.ShownInventorySourceId!=Container.id) {Failure="Окно цеха показывает другой контейнер.";return;}
                shared=true;phase=5;NetworkClient.Send(new FactorySeamViewed());capture="workshop-container";
            }
            else if(phase==5 && Host && viewers.Count==runtime.Options.ProbePeers && !claiming)
            {
                if(Container.items.Count==0) {Failure="В проверочном мире соседний ящик пуст; нужен явный мир для проверки лута.";return;}
                var item=Container.items[0];claimedItem=item.id;definition=item.definition;quantity=item.count;
                inventoryBefore=InventoryJournal.Count(Actor,definition);claiming=true;
                runtime.StartCoroutine(UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0)));phase=6;
            }
            else if(phase==6 && Host && !Container.items.Any(i=>i.id==claimedItem) && !runtime.Session.World.pendingGrants.Any(g=>g.containerId==Container.id))
            {
                if(InventoryJournal.Count(Actor,definition)!=inventoryBefore+quantity) {Failure="Выданный лут цеха не совпал с инвентарём владельца.";return;}
                NetworkServer.SendToAll(new FactorySeamLoot {item=claimedItem,definition=definition,count=quantity});
                runtime.Hud.CloseContainer();phase=7;
            }
            else if(phase==5 && !Host && claimedItem!=null && !Container.items.Any(i=>i.id==claimedItem))
            {runtime.Hud.CloseContainer();phase=7;}
            else if(phase==7 && Mathf.Abs(Actor.x-16.22f)<.08f) phase=8;
            // Jump before the unsupported lower end releases the grip. Below that
            // point the actor is already falling behind the entry platform.
            else if(phase==8 && Actor.y<=initialRoom.spawnY+.7f) {phase=9;jumped=false;}
            else if(phase==9 && Mathf.Abs(Actor.x-initialRoom.spawnX)<.08f && Mathf.Abs(Actor.y-initialRoom.spawnY)<.03f)
                phase=10;
        }
        public void Capture()
        {
            if(capture==null) return;
            if(runtime.Options.Smoke.Contains("-frames-")) NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+capture+".png",runtime.Hud.GetComponent<Canvas>());
            capture=null;
        }
    }
}
