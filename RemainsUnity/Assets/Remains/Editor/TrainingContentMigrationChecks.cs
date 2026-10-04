using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class TrainingContentMigrationChecks
    {
        public static void Run()
        {
            ActorTypes.Load();PropCatalog.Load();
            try
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/ConnectedTrainingHub.prefab");
                var room=root.GetComponent<RoomAuthoring>();
                var markers=root.GetComponentsInChildren<RoomContainerPlacement>();
                foreach(var marker in markers) PropCatalog.Register(marker.Definition.CreateDefinition(),true);
                var layout=new TrainingLayout {patch="check:original-training-containers",level="hub",actors=Array.Empty<TrainingPlacement>(),
                    containers=markers.Select(marker=>
                    {
                        var entry=marker.Read(room);
                        // Preserve IDs of the two matching old training crates. The unmatched 50 kg crate remains intact.
                        if(entry.type=="mcrate1") entry.id="training:crate:1";
                        if(entry.type=="mcrate3") entry.id="training:crate:3";
                        entry.relocateExisting=true;return entry;
                    }).ToArray()};
                var fresh=new WorldState();TrainingContentInstaller.Apply(fresh,layout);
                Require(fresh.containers.Count==5 && fresh.containers.Select(c=>c.mass).OrderBy(m=>m).SequenceEqual(new[]{15f,25f,75f,120f,1000f}),"Новый мир не получил исходные пять ящиков.");
                var world=new WorldState();HubTrainingContent.Install(world);
                var legacy=world.containers.Single(c=>c.id=="training:crate:1");var spare=world.containers.Single(c=>c.id=="training:crate:2");
                var item=SharedInventory.Create("heal",7);legacy.items.Add(item);legacy.vx=3;legacy.vy=-2;
                world.pendingGrants.Add(new PendingGrant {transaction="pending-training",containerId=legacy.id,item=item.Copy(),recipient="player"});
                var target=layout.containers.Single(c=>c.id==legacy.id);
                TrainingContentInstaller.Apply(world,layout);
                Require(world.containers.Count==6 && ReferenceEquals(world.containers.Single(c=>c.id==legacy.id),legacy) &&
                    ReferenceEquals(legacy.items.Single(),item) && item.count==7 && legacy.x==target.x && legacy.y==target.y && legacy.vx==0 && legacy.vy==0,
                    "Миграция потеряла старый ящик, предметы или не обновила положение.");
                Require(world.containers.Contains(spare) && spare.mass==50 && world.pendingGrants.Single().containerId==legacy.id,"Потерян старый 50-кг ящик или ссылка незавершённой выдачи.");
                legacy.x+=2;var saved=world.Copy();string before=StateJson.ToJson(saved);
                TrainingContentInstaller.Apply(saved,layout);Require(StateJson.ToJson(saved)==before,"Повтор после сохранения сбросил перемещённый игроком ящик или продублировал содержимое.");
                var conflict=new WorldState();HubTrainingContent.Install(conflict);
                var final=layout.containers.Last();conflict.props.Add(new PropState {id=final.id});before=StateJson.ToJson(conflict);
                bool rejected=false;try {TrainingContentInstaller.Apply(conflict,layout);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected && StateJson.ToJson(conflict)==before,"Поздний конфликт оставил частичную миграцию.");
                conflict.props.Clear();conflict.containers.Single(c=>c.id==legacy.id).held=true;before=StateJson.ToJson(conflict);
                rejected=false;try {TrainingContentInstaller.Apply(conflict,layout);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected && StateJson.ToJson(conflict)==before,"Удерживаемый объект был перенесён посреди симуляции.");
                Debug.Log("Миграция учебных ящиков PASS: пять новых, шесть при сохранении старого 50-кг ящика, сохранены ID/предметы/ссылки выдачи, повтор без изменений, конфликт без частичного применения.");
            }
            finally {ActorTypes.Load();PropCatalog.Load();}
            ContractChecks.Run();
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
    }
}
