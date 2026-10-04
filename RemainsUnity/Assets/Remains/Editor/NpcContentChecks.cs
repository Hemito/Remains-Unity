using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Editor
{
    public static class NpcContentChecks
    {
        public static void Run()
        {
            var npc=new ActorState {id="npc",actorType="raider",anatomy="raider",level="factory:0",health=140};
            var layout=new NpcLayout {patch="npc-test",level=npc.level,actors=new[]{npc}};
            var world=new WorldState();NpcContentInstaller.Apply(world,layout);
            var saved=world.Find(npc.id);saved.down=true;saved.health=0;saved.x=12;saved.owner="remote";
            saved.inventory.Add(new ItemStack {id="loot",definition="caps",count=3});
            world=StateJson.Copy(world);string before=StateJson.ToJson(world);
            if(NpcContentInstaller.Apply(world,layout) || before!=StateJson.ToJson(world)) throw new InvalidOperationException("Загрузка изменила сохранённого NPC.");
            world.contentPatches.Clear();NpcContentInstaller.Apply(world,layout);saved=world.Find(npc.id);
            if(!saved.down || saved.health!=0 || saved.x!=12 || saved.owner!="remote" || saved.inventory.Count!=1)
                throw new InvalidOperationException("Принятие существующего NPC сбросило его состояние.");
            world.actors.Clear();NpcContentInstaller.Apply(world,layout);
            if(world.actors.Count!=0) throw new InvalidOperationException("Удалённый NPC появился повторно.");
            var conflict=new WorldState();conflict.props.Add(new PropState {id="occupied"});var second=npc.Copy();second.id="occupied";
            layout.actors=new[]{npc,second};before=StateJson.ToJson(conflict);bool rejected=false;
            try {NpcContentInstaller.Apply(conflict,layout);} catch(InvalidOperationException) {rejected=true;}
            if(!rejected || StateJson.ToJson(conflict)!=before) throw new InvalidOperationException("Конфликт NPC частично изменил мир.");
            Debug.Log("Расстановка NPC: PASS — здоровье, тело, инвентарь, позиция, владелец и удаление сохраняются; конфликт отклоняется атомарно.");
        }
    }
}
