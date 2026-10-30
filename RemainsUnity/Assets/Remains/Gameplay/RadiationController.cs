using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Gameplay
{
    // The owner's side of PlayerRadiation for the player this peer controls: ten times a second it sums the doses of the
    // radioactive objects of the player's level around it (their list refreshed twice a second — the work is the active
    // level's few sources, not the world), accumulates them, lowers the health to its limit (knocking down at 0) and cuts
    // the radiation after a revival. The player is published when its whole radiation or its health changed.
    // Not modelled yet: the location's background (loc.rad: 0 on the factory) and water (loc.wrad: the port has none),
    // resistance (radX: armour, perks, Rad-X), radiation of creatures (zombies).
    public sealed class RadiationController
    {
        private const float Period=.1f,ListPeriod=.5f;
        private readonly CoopSession session;
        private readonly CombatController combat;
        private readonly List<PropState> sources=new();
        private string sourcesWorld,sourcesLevel;
        private float clock,listAge=ListPeriod;
        private bool wasDown;
        // The local player's dose per frame (drad) — the Geiger counter's input.
        public float LocalDose {get;private set;}
        public RadiationController(CoopSession session,CombatController combat) {this.session=session;this.combat=combat;}

        public void Step(float dt)
        {
            clock+=dt;listAge+=dt;
            if(clock<Period) return;
            float elapsed=clock;clock=0;
            var world=session.World;var actor=session.LocalActor;
            if(world==null || actor==null || !actor.player || !actor.connected || session.Pause.IsPaused(actor.level)) {LocalDose=0;return;}
            bool changed=false;
            if(wasDown && !actor.down) changed|=PlayerRadiation.LimitAfterRevival(actor);
            wasDown=actor.down;
            if(listAge>=ListPeriod || sourcesWorld!=world.worldId || sourcesLevel!=actor.level) Collect(world,actor.level);
            var centre=actor.Position+Vector2.up*(Anatomy.Movement(actor).height*.5f);
            float dose=0;
            foreach(var prop in sources)
                dose+=PlayerRadiation.Dose(PropCatalog.Get(prop.definition).radioactivity,Vector2.Distance(centre,prop.Center));
            LocalDose=actor.down?0:dose;
            int whole=Mathf.FloorToInt(actor.radiation);
            if(PlayerRadiation.Accumulate(actor,LocalDose,elapsed)) changed|=Mathf.FloorToInt(actor.radiation)!=whole;
            changed|=combat.LimitHealth(actor);
            if(changed) session.PublishActor(actor);
        }

        private void Collect(WorldState world,string level)
        {
            sources.Clear();listAge=0;sourcesWorld=world.worldId;sourcesLevel=level;
            foreach(var prop in world.props)
                if(prop.level==level && PropCatalog.Get(prop.definition).radioactivity>0) sources.Add(prop);
        }
    }
}
