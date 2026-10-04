using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Editor
{
    public static class RoomContentChecks
    {
        public static void Run()
        {
            var player=new ActorState {id="player",player=true};var world=new WorldState();world.actors.Add(player);
            var batch=new RoomContentBatch {
                doors=new DoorLayout {patch="doors",level="factory:0",doors=new[]{new DoorState {id="door",definition="door1",level="factory:0",width=1,height=2,surfaceTexture="floor"}}},
                physical=new PhysicalLayout {patch="physical",level="factory:0",containers=new[]{new ContainerState {id="box",level="factory:0"}}},
                npcs=new NpcLayout {patch="npcs",level="factory:0",actors=new[]{new ActorState {id="player"}}}};
            string before=StateJson.ToJson(world);bool rejected=false;
            try {RoomContentInstaller.Apply(world,batch);} catch(InvalidOperationException) {rejected=true;}
            if(!rejected || before!=StateJson.ToJson(world)) throw new InvalidOperationException("Ошибка NPC оставила в мире двери или контейнеры.");
            batch.npcs.actors[0].id="npc";RoomContentInstaller.Apply(world,batch);
            if(!ReferenceEquals(player,world.Find("player")) || world.doors.Count!=1 || world.containers.Count!=1 || world.Find("npc")==null || world.contentPatches.Count!=3)
                throw new InvalidOperationException("Полная установка потеряла данные или ссылки живого мира.");
            world.doors[0].open=true;world.containers[0].x=9;world.Find("npc").health=3;before=StateJson.ToJson(world);
            if(RoomContentInstaller.Apply(world,batch) || before!=StateJson.ToJson(world)) throw new InvalidOperationException("Повторная установка изменила комнату.");
            batch.npcs.patch=batch.physical.patch;rejected=false;
            try {RoomContentInstaller.Apply(world,batch);} catch(InvalidOperationException) {rejected=true;}
            if(!rejected || before!=StateJson.ToJson(world)) throw new InvalidOperationException("Не отклонена общая версия разных наборов.");
            Debug.Log("Содержимое комнаты: PASS — поздняя ошибка без частичной установки, сохранение ссылок и состояний, повтор и конфликт версий.");
        }
    }
}
