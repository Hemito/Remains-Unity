using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;

namespace Remains.Editor
{
    public static class ActorTypeChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,owner="client-a",level="hub"};
            var dummy=new ActorState {id="dummy",actorType="training-dummy",level="hub",health=500,x=2};
            world.actors.Add(player);world.actors.Add(dummy);
            var ownership=new OwnershipService();ownership.Reassign(world,new[]{"client-a"});
            Require(dummy.owner=="client-a" && dummy.target==null,"Мишень должна иметь владельца, но не боевую цель");
            var receiver=new DamageReceiver(new PrototypeDamageRule());int results=0;float total=0;
            receiver.Applied+=result=>{results++;total+=result.amount;};
            var shot=new AttackEvent {id="first",attacker=player.id,level="hub",weapon="pistol"};
            Require(receiver.Apply(dummy,shot,true) && dummy.health==500 && !dummy.down,"Мишень не восстановилась после попадания");
            Require(!receiver.Apply(dummy,shot,true) && results==1 && total==11,"Повторное попадание изменило результат мишени");
            shot.id="second";Require(receiver.Apply(dummy,shot,true) && results==2 && total==22,"Второе независимое попадание потеряно");
            var copy=dummy.Copy();Require(copy.actorType=="training-dummy" && copy.resolvedAttacks.Count==2,"Копия потеряла тип или журнал");
            Require(InventorySource.Resolve(world,InventorySource.BodyId(dummy.id))==null && InventorySource.All(world).All(s=>s.Body?.id!=dummy.id),"У мишени появился доступный инвентарь");
            var definition=ActorTypes.Get(dummy.actorType);
            Require(!definition.combatAi && !definition.hasWeapon && definition.presentation=="static","Неверные возможности мишени");
            bool rejected=false;try {ActorTypes.Get("missing-check");} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неизвестный тип молча подменён другим");
            HubTrainingContent.Install(world);int installed=world.actors.Count;long revision=world.revision;
            HubTrainingContent.Install(world);
            Require(world.actors.Count==installed && world.revision==revision && world.Find("player")==player,"Повтор установки мишеней изменил существующий мир");
            var persisted=world.Copy();HubTrainingContent.Install(persisted);
            Require(persisted.actors.Count==installed && persisted.contentPatches.Contains(HubTrainingContent.Patch),"Копия мира потеряла отметку установки мишеней");
            Require(world.containers.Count==3 && persisted.containers.Count==3 && world.containers.Select(c=>c.mass).SequenceEqual(new[]{25f,50f,120f}),"Неверные массы или повторное добавление тренировочных ящиков");
            var telekinesis=new MassLimitedTelekinesisRule(TelekinesisSettings.Load());
            foreach(var box in world.containers)
            {
                player.Position=box.Position;
                Require((telekinesis.Rejection(player,box,false)==null)==(box.mass<=30),"Доступность ящика не соответствует весу: "+box.mass);
            }
            var visual=Remains.Presentation.ActorVisualFactory.Create(dummy);
            try
            {
                visual.Bind(dummy);
                Require(visual is Remains.Presentation.StaticActorVisual && visual.GetComponent<UnityEngine.SpriteRenderer>().sprite!=null,
                    "Не создана самостоятельная модель мишени");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
            UnityEngine.Debug.Log("Типы актёров: PASS — учебная цель, владелец, восстановление, повтор, сохранение, отсутствие лута.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
