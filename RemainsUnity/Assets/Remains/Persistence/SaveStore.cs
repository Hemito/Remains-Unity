using JsonUtility=Remains.Serialization.StateJson;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    public sealed class SaveStore
    {
        public string DirectoryPath { get; }
        public SaveStore(string directory)
        {
            DirectoryPath = SavePath.Absolute(directory);
            Directory.CreateDirectory(DirectoryPath);
        }

        public WorldState LoadWorld(bool prepareForPlay=true)
        {
            string path = Path.Combine(DirectoryPath, "world.json");
            if (!File.Exists(path)) return null;
            var world = JsonUtility.FromJson<WorldState>(File.ReadAllText(path));
            if (world == null || world.schema != 1) throw new InvalidDataException($"Неподдерживаемый сейв: {path}");
            if(!prepareForPlay) return world;
            world.containers ??= new List<ContainerState>();
            if(world.container?.Count>0 || world.pendingGrants.Any(g=>string.IsNullOrEmpty(g.containerId)))
            {
                if(world.containers.Count>0) throw new InvalidDataException($"Одновременно заданы старые и новые контейнеры: {path}.");
                string backup=PreserveSource("world.json");
                world.containers.Add(new ContainerState {items=world.container??new List<ItemStack>()});world.container=null;
                foreach(var grant in world.pendingGrants) grant.containerId=ContainerState.PrototypeId;
                world.revision++;
                Debug.LogWarning($"Глобальный контейнер перенесён на завод: container={ContainerState.PrototypeId}, backup={backup}.");
            }
            if(world.actors.Any(a=>a.player && a.loadoutVersion==0))
            {
                string backup=PreserveSource("world.json");
                foreach(var actor in world.actors.Where(a=>a.player && a.loadoutVersion==0)) UpgradeLoadout(actor,backup);
                world.revision++;
            }
            if(world.actors.Any(a=>a.player && a.weaponInventoryVersion==0))
            {
                string backup=PreserveSource("world.json");
                for(int i=0;i<world.actors.Count;i++)
                    if(world.actors[i].player && world.actors[i].weaponInventoryVersion==0)
                        world.actors[i]=UpgradeWeaponInventory(world.actors[i],backup);
                world.revision++;
            }
            world.grenades ??= new List<GrenadeState>();world.explosions ??= new List<ExplosionState>();
            world.projectiles ??= new List<ProjectileState>();
            foreach(var flight in world.projectiles) flight.Path();
            foreach(var actor in world.actors) {actor.inventoryUses ??= new List<InventoryUse>();actor.resolvedExplosions ??= new List<string>();actor.resolvedAttacks ??= new List<string>();}
            return world;
        }

        public RecoverySnapshot SaveWorld(WorldState world)
        {
            ContentProvenance.Record(ref world.weaponContent,ref world.weaponContentHistory);
            if(world.historyVersion==1) world.checkpointSequence++;
            var snapshot=new RecoverySnapshot(JsonUtility.ToJson(world));
            SaveRecovery(snapshot);
            return snapshot;
        }

        public RecoverySnapshot SelectedSnapshot()
        {
            string path=Path.Combine(DirectoryPath,"world.json");
            return File.Exists(path)?new RecoverySnapshot(File.ReadAllText(path)):null;
        }

        public List<RecoverySnapshot> Continuations()
        {
            var all=new Dictionary<string,RecoverySnapshot>();
            var selected=SelectedSnapshot();
            if(selected!=null) all[selected.Id]=selected;
            string root=Path.Combine(DirectoryPath,"Worlds");
            if(Directory.Exists(root))
                foreach(string path in Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories))
                {
                    try { var snapshot=new RecoverySnapshot(File.ReadAllText(path));all[snapshot.Id]=snapshot; }
                    catch(Exception exception) when(exception is InvalidDataException || exception is ArgumentException)
                    { Debug.LogWarning($"Пропущен повреждённый снимок {path}: {exception.Message}"); }
                }
            // Only proven ancestors are hidden. Conflicting or legacy copies stay visible.
            var decoded=all.Values.Select(snapshot=>new {snapshot,state=snapshot.CreateLiveWorld()}).ToList();
            var heads=decoded.GroupBy(a=>a.state.historyVersion==1?a.state.worldId+":"+a.state.branchId:a.snapshot.Id)
                .SelectMany(group=>group.Where(a=>a.state.checkpointSequence==group.Max(b=>b.state.checkpointSequence))).ToList();
            return heads.Where(a=>!heads.Any(b=>a.snapshot.Id!=b.snapshot.Id &&
                WorldHistory.Compare(b.state,b.snapshot.Id,a.state,a.snapshot.Id)==WorldRelation.Newer))
                .Select(a=>a.snapshot).OrderBy(a=>a.WorldId).ThenBy(a=>a.Id).ToList();
        }

        public void SelectContinuation(RecoverySnapshot snapshot) => SaveRecovery(snapshot);

        public void SaveRecovery(RecoverySnapshot snapshot)
        {
            // Keep every distinct payload, including branches with identical revision numbers.
            string archive=Path.Combine("Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
            string path=Path.Combine(DirectoryPath,archive);
            if(File.Exists(path))
            {
                if(File.ReadAllText(path)!=snapshot.Json)
                    throw new InvalidDataException($"Повреждён архив снимка: {path}.");
            }
            else AtomicWrite(archive,snapshot.Json);
            PreserveExistingWorld("world.json");
            PreserveExistingWorld(Path.Combine("Worlds",snapshot.WorldId+".json"));
            AtomicWrite(Path.Combine("Worlds",snapshot.WorldId+".json"),snapshot.Json);
            AtomicWrite("world.json",snapshot.Json);
        }

        private void PreserveExistingWorld(string filename)
        {
            string path=Path.Combine(DirectoryPath,filename);
            if(!File.Exists(path)) return;
            var previous=new RecoverySnapshot(File.ReadAllText(path));
            string archive=Path.Combine("Worlds",previous.WorldId,"Snapshots",previous.Id+".json");
            if(!File.Exists(Path.Combine(DirectoryPath,archive))) AtomicWrite(archive,previous.Json);
        }
        public void SaveCharacter(ActorState actor)
        {
            var saved=actor.Copy();
            ContentProvenance.Record(ref saved.weaponContent,ref saved.weaponContentHistory);
            AtomicWrite("character.json",JsonUtility.ToJson(saved,true));
        }
        public ActorState LoadCharacter()
            =>PrepareCharacter(ReadCharacterSource(),false);

        public string ReadCharacterSource()
        {
            string path=Path.Combine(DirectoryPath,"character.json");
            return File.Exists(path)?File.ReadAllText(path):null;
        }

        public ActorState PrepareCharacter(string source,bool preserve)
        {
            if(source==null) return null;
            var actor=JsonUtility.FromJson<ActorState>(source);
            string backup=null;
            if(preserve || actor.loadoutVersion==0 || actor.weaponInventoryVersion==0)
            {
                backup=Path.Combine("Recovery","character-"+Guid.NewGuid().ToString("N")+".json");
                AtomicWrite(backup,source);
                backup=Path.Combine(DirectoryPath,backup);
            }
            actor.resolvedExplosions ??= new List<string>();actor.resolvedAttacks ??= new List<string>();
            if(actor.loadoutVersion==0) UpgradeLoadout(actor,backup);
            if(actor.weaponInventoryVersion==0) actor=UpgradeWeaponInventory(actor,backup);
            return actor;
        }

        private static ActorState UpgradeWeaponInventory(ActorState actor,string backup)
        {
            var prepared=WeaponInventoryMigration.Prepare(actor);
            Debug.LogWarning($"Оружие перенесено в предметы: actor={actor.id}, версия=1. Заряды и данные предметов сохранены. Исходник: {backup}.");
            return prepared;
        }

        public string PreserveRecoverySource()
            => PreserveSource("world.json");

        private static void UpgradeLoadout(ActorState actor,string backup)
        {
            WeaponAmmunition.Initialize(actor,true);
            actor.revision++;
            Debug.LogWarning($"Миграция боезапаса v0→v1: actor={actor.id}, добавлено стартовое оснащение прототипа (магазины и резерв); здоровье и существующие предметы сохранены. Исходник: {backup}.");
        }

        private string PreserveSource(string filename)
        {
            string source=Path.Combine(DirectoryPath,filename);
            if(!File.Exists(source)) return null;
            string directory=Path.Combine(DirectoryPath,"Recovery");Directory.CreateDirectory(directory);
            string destination=Path.Combine(directory,Path.GetFileNameWithoutExtension(filename)+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");
            File.Copy(source,destination);
            return destination;
        }

        private void AtomicWrite(string filename, string content)
        {
            string destination = Path.Combine(DirectoryPath, filename);
            AtomicSaveFile.Write(destination,content);
        }
    }
}
