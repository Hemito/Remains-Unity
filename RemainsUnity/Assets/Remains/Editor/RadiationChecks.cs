using System;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Player radiation after the original (PlayerRadiation): the dose of a source by distance (Obj.ggModum), its accrual
    // (rad += drad/30 a frame), the health limit maxhp − rad as a loss with knockdown at 0, healing and revival under it,
    // the radiation cut after a revival, cures (hrad), the Geiger chance and clips; the radioactive barrel is a source.
    //   batch -executeMethod Remains.Editor.RadiationChecks.RunBatch
    public static class RadiationChecks
    {
        private sealed class FixedDamage : IDamageRule {public float Calculate(string weapon,float speed)=>10;}
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();PropCatalog.Load();
            Require(Mathf.Approximately(PlayerRadiation.Dose(3,0),3) && Mathf.Approximately(PlayerRadiation.Dose(3,2.5f),1.5f) && PlayerRadiation.Dose(3,5)==0 &&
                PlayerRadiation.Dose(3,9)==0,"доза источника: rad·(radrad − d)/radrad в 250 px");
            Require(Mathf.Approximately(PropCatalog.Get("radbarrel").radioactivity,3),"радиоактивная бочка — источник rad 3");

            ActorState Player()=>new() {id="p",player=true,connected=true,level="l",health=100,actorType="pony",anatomy="pony"};
            // Ten seconds 2.5 m from a barrel: 1.5 a frame → +15.
            var player=Player();
            Require(PlayerRadiation.Accumulate(player,PlayerRadiation.Dose(3,2.5f),10) && Mathf.Abs(player.radiation-15)<.001f,$"накопление: {player.radiation} вместо 15");
            var npc=new ActorState {id="n",player=false,health=100};
            Require(!PlayerRadiation.Accumulate(npc,3,10) && npc.radiation==0,"радиация копится только у игроков");

            // The limit is maxhp − rad: a loss of health without a recorded attack; at 0 the player is down.
            var receiver=new DamageReceiver(new FixedDamage());DamageResult result=default;receiver.Applied+=r=>result=r;
            player.radiation=30;
            Require(receiver.LimitHealth(player) && Mathf.Approximately(player.health,70) && player.damageSequence==1 && player.resolvedAttacks.Count==0 &&
                !string.IsNullOrEmpty(result.attack) && !player.down && !receiver.LimitHealth(player),"предел здоровья 100 − 30 — потеря без записи атаки, один раз");
            player.radiation=100;
            Require(receiver.LimitHealth(player) && player.health==0 && player.down,"радиация 100 — без сознания");

            // Healing: self-heal stops at the limit, revival gives room of at least 40; the owner cuts the radiation after it.
            var healer=Player();healer.id="h";healer.inventory.Add(new ItemStack {id="kit",definition="heal",count=3});healer.weaponInventoryVersion=1;
            var hurt=Player();hurt.id="h";hurt.radiation=40;hurt.health=60;hurt.inventory=healer.inventory;hurt.weaponInventoryVersion=1;
            Require(HealingRules.Rejection(hurt,hurt,true)!=null,"самолечение при здоровье у предела отклоняется");
            hurt.health=30;
            Require(HealingRules.Apply(hurt,hurt,true) && Mathf.Approximately(hurt.health,60),$"самолечение не выше 100 − rad: {hurt.health}");
            var down=Player();down.id="d";down.radiation=90;down.health=0;down.down=true;
            Require(HealingRules.Apply(healer,down,false) && Mathf.Approximately(down.health,35) && !down.down,$"подъём: +35 в пределах 100 − min(rad, 60): {down.health}");
            Require(PlayerRadiation.LimitAfterRevival(down) && Mathf.Approximately(down.radiation,60) && !PlayerRadiation.LimitAfterRevival(down),"после подъёма радиация не выше 60");

            // Cures (hrad): antiradin 200 and milk 50 are taken; Rad-Cola (hrad −33, with mana) is not offered for radiation.
            Require(Mathf.Approximately(ItemRadiationCures.Amount("item:antiradin"),200) && Mathf.Approximately(ItemRadiationCures.Amount("item:milk"),50) &&
                ItemRadiationCures.Amount("item:radcola")==0 && ItemRadiationCures.Amount("heal")==0,"лечение радиации по hrad AllData");
            var actions=new StandardItemActions();
            Require(actions.Action(new ItemStack {id="a",definition="item:antiradin",count=1})==StandardItemActions.Consume &&
                actions.Action(new ItemStack {id="k",definition="heal",count=1})==StandardItemActions.Heal,"антирадин принимается, аптечка лечит");
            var sick=Player();sick.radiation=80;
            PlayerRadiation.Cure(sick,50);Require(Mathf.Approximately(sick.radiation,30),"молоко снимает 50");
            PlayerRadiation.Cure(sick,200);Require(sick.radiation==0,"радиация не ниже 0");

            // The Geiger counter clicks with min(0.5, drad/10) a frame above drad 0.1; its three clips are there.
            Require(Presentation.GeigerAudio.Chance(.05f,1f/30)==0 && Mathf.Approximately(Presentation.GeigerAudio.Chance(3,1f/30),.3f) &&
                Mathf.Approximately(Presentation.GeigerAudio.Chance(9,1f/30),.5f),"шанс щелчка счётчика");
            foreach(string key in new[]{"exported:sound:53","exported:sound:54","exported:sound:55"})
                Require(AudioClipSource.Load(key) is {samples:>0},"нет звука счётчика Гейгера "+key);
            Debug.Log("Радиация: PASS — доза по расстоянию, накопление, предел здоровья 100 − rad с падением при 0, лечение и подъём под пределом, "+
                "срез после подъёма, антирадин и молоко, счётчик Гейгера.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("RadiationChecks: "+reason);}
    }
}
