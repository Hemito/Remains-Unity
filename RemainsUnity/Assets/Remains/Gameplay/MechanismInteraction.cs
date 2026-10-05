using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    // Asset/type resolution runs on world changes, not while holding E.
    public sealed class MechanismInteraction : IDisposable
    {
        private readonly CoopSession session;
        private readonly ButtonRepairProgress repair;
        private readonly Dictionary<string,MechanismButtonDefinition> types=new(StringComparer.Ordinal);
        private string repairingButton;
        private WorldState boundWorld;
        public IReadOnlyList<MechanismInteractionPoint> Points {get;private set;}=Array.Empty<MechanismInteractionPoint>();
        public double Progress=>session.Mechanisms.Pending?1:repair.Progress;
        public bool Repairing=>repair.Active;
        public bool Pending=>session.Mechanisms.Pending;
        public MechanismInteraction(CoopSession session)
        {this.session=session;repair=new ButtonRepairProgress(session.Rules.ButtonRepair);session.Changed+=Reconcile;Reconcile();}
        private void Reconcile()
        {
            var world=session.World;
            if(ReferenceEquals(world,boundWorld)) return;
            boundWorld=world;
            if(world==null) {Points=Array.Empty<MechanismInteractionPoint>();Cancel("Ремонт отменён: сессия завершена.");return;}
            var points=new List<MechanismInteractionPoint>();
            foreach(var button in world.mechanismButtons)
            {
                if(button.removed) continue;
                if(!types.TryGetValue(button.definition,out var type))
                    types.Add(button.definition,type=MechanismCatalogAsset.Load().Button(button.definition).CreateDefinition());
                points.Add(new MechanismInteractionPoint(button,new Rect(button.x+type.graphicOffsetX,button.y+type.graphicOffsetY,
                    type.graphicWidth,type.graphicHeight),type.interactionRadius));
            }
            Points=points.ToArray();
        }
        public void Step(InteractionTarget target,bool pressed,bool held,bool allowed,double simulationDelta)
        {
            var actor=session.LocalActor;
            if(!allowed || actor==null || target.kind!=InteractionKind.Mechanism ||
                repair.Active && target.id!=repairingButton)
            {Cancel("Ремонт отменён: цель или доступ к взаимодействию изменились.");return;}
            var button=session.World.mechanismButtons.Find(b=>b.id==target.id);
            if(button==null || button.removed) {Cancel("Ремонт отменён: кнопка больше недоступна.");return;}
            bool paused=session.Pause.IsPaused(button.level);
            if(pressed && !repair.Active && !Pending && !paused)
            {
                if(button.repairState==MechanismRepairStates.Ready) session.Mechanisms.RequestActivation(button.id);
                else
                {
                    var type=types[button.definition];
                    var result=repair.Begin("mechanism:"+Guid.NewGuid().ToString("N"),actor,button,type.repairDurationSeconds,type.interactionRadius);
                    if(result.Outcome==ButtonRepairOutcome.Rejected) session.ShowNotice(result.Reason);
                    else repairingButton=button.id;
                }
            }
            if(!repair.Active) return;
            var progress=repair.Step(actor,button,held,paused,simulationDelta);
            if(progress.Outcome==ButtonRepairOutcome.Completed)
            {repairingButton=null;session.Mechanisms.RequestRepair(progress.Candidate,progress.ElapsedSeconds);}
            else if(progress.Outcome==ButtonRepairOutcome.Cancelled) {repairingButton=null;session.ShowNotice(progress.Reason);}
        }
        private void Cancel(string reason)
        {if(repair.Active) {repair.Cancel(reason);session.ShowNotice(reason);}repairingButton=null;}
        public void Dispose() {session.Changed-=Reconcile;}
    }
}
