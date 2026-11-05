using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class ItemPickupAudioChecks
    {
        private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        public static void Run()
        {
            var catalog=ItemPickupAudioCatalog.Load();
            var settings=JsonUtility.FromJson<ItemPickupAudioCatalog.Settings>(GameAssets.Load<TextAsset>(ItemPickupAudioCatalog.Resource).text);
            Require(catalog.Count>=ExportedContentCatalog.Load().items.Count(e=>e.category is "item" or "weapon"),"Не назначен весь установленный набор предметов.");
            Require(catalog.TryGet("weapon:mont:grade:2",out var metal,out _) && metal==AudioClipSource.Load("exported:sound:74"),"Потерян исходный металл или грейд.");
            Require(catalog.TryGet("heal",out var heal,out _) && heal==GameAssets.Load<AudioClip>("Original/Audio/ItemLandings/heal"),"Потерян legacy potion.");
            Require(catalog.TryGet("item:pot1",out _,out _) && catalog.TryGet("armor.antirad",out _,out _),"Нет полного предмета/одежды.");
            Require(!catalog.TryGet("spear",out _,out var reason) && reason.Contains("falll_item"),"Молчаливо заменён неразрешённый исходный ключ копья.");
            Require(!catalog.TryGet("mod:unknown",out _,out _),"Неизвестному моду подставлен звук.");
            int loads=0;_ = ItemPickupAudioCatalog.Create(settings,key=>{loads++;return AudioClipSource.Load(key);});
            Require(loads==settings.entries.Where(e=>!string.IsNullOrEmpty(e.clip)).Select(e=>e.clip).Distinct().Count(),"Повторная загрузка общего WAV.");

            var root=new GameObject("Проверка подтверждённого подбора");
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var actor=new ActorState{id="pickup-test",player=true,connected=true,owner="owner",level="hub",health=100,actorType="pony"};world.actors.Add(actor);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
                using var pickup=new LooseItemPickup(session);
                var claim=(PendingItemClaim)typeof(LooseItemPickup).GetField("claim",Private).GetValue(pickup);
                var audio=root.AddComponent<GameplayAudio>();audio.Initialize(session,new WeaponReloadController(session),root.AddComponent<Camera>());audio.ObservePickups(pickup);
                int confirmations=0;pickup.Confirmed+=(definition,transaction)=>
                {Require(definition=="heal" && transaction=="confirmed","Потеряно назначение события.");confirmations++;};
                void Start(string transaction)
                {
                    Require(claim.Capture(transaction,actor,"drop:test","item:test",world.worldId),"Не захвачен новый запрос.");
                    typeof(LooseItemPickup).GetField("requestedDefinition",Private).SetValue(pickup,"heal");
                }
                Start("rejected");claim.Prepared("rejected",false);pickup.Step();
                Require(confirmations==0 && audio.PickupSounds==0,"Отказ прозвучал.");
                Start("reserved");claim.Prepared("reserved",true);claim.Prepared("reserved",false);
                Require(!claim.Step(world,actor,true) && claim.Pending,"Резерв звучит до commit либо отказ стирает принятый резерв.");claim.Clear();
                // The actual inventory transaction is the authority; no award from an audio callback.
                var drop=DroppedItemTransfer.Preview(actor,"pickup-audio","heal",actor.Position,Vector2.zero);
                var stack=SharedInventory.Create("heal",1);drop.items.Add(stack);world.containers.Add(drop);
                var inventory=new SharedInventory(world);
                Require(inventory.PrepareClaim("confirmed",drop.id,stack.id,actor),"Не подготовлена реальная выдача.");
                Start("confirmed");
                Require(!claim.Step(world,actor,true) && audio.PickupSounds==0,"Звук до подтверждения выдачи.");
                inventory.CommitGrant(world.pendingGrants.Single());
                // Exercise the same gate/event as Step, with a connected transport supplied explicitly
                // to the diagnostic overload; no fabricated reservation reply or network packet.
                pickup.Step(true);pickup.Step(true);
                Require(confirmations==1 && audio.PickupSounds==1 && actor.inventory.Count==1,"Commit/повтор не дал ровно один звук/предмет.");
                var voices=root.GetComponentsInChildren<AudioSource>();
                Require(voices.Length==1 && voices[0].clip==heal && voices[0].volume>0 && (!Application.isBatchMode || voices[0].mute),"Нет настоящего AudioSource подтверждённого WAV.");
                Start("stale-life");actor.lifecycleSequence++;world.completedTransactions.Add("stale-life");pickup.Step(true);
                Require(audio.PickupSounds==1 && !claim.Pending,"Звук прежней жизни.");
                Start("stale-world");string previous=world.worldId;world.worldId=Guid.NewGuid().ToString("N");world.completedTransactions.Add("stale-world");pickup.Step(true);world.worldId=previous;
                Require(audio.PickupSounds==1 && !claim.Pending,"Звук другого мира.");
                Start("disconnected");world.completedTransactions.Add("disconnected");pickup.Step(false);
                Require(audio.PickupSounds==1 && !claim.Pending,"Звук после отключения.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log($"PT-20261103-18: PASS — {catalog.Count}явных назначений, грейды/legacy, кэш WAV, настоящий commit/AudioSource, отказ/резерв/повтор/смена жизни/мира/отключение без звука.");
        }
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("Звук подбора: "+message);}
    }
}

