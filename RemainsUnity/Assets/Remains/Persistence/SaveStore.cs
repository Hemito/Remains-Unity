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
        // Kept for code mods using the original personal-profile contract.
        public string DirectoryPath => CharacterDirectoryPath;
        public string CharacterDirectoryPath { get; }
        public string WorldDirectoryPath { get; }
        public WorldCatalog Catalog {get;}
        private readonly WorldLibraryMigration migration;
        private sealed class KnownWorldFile {public string path;public RecoverySnapshot snapshot;public long length,ticks;}
        // Access is serialized by the same cross-process library gate as publication.
        private KnownWorldFile knownActive,knownFlat; // At most two immutable payloads; not a growing history cache.
        public int ReusedWorldPayloads {get;private set;}
        public IReadOnlyList<string> LibraryWarnings=>migration?.Warnings??Array.Empty<string>();
        public SaveStore(string directory):this(directory,directory) {}
        public SaveStore(string characterDirectory,string worldDirectory,WorldLibraryMigration migration=null)
        {
            CharacterDirectoryPath=SavePath.Absolute(characterDirectory);
            WorldDirectoryPath=SavePath.Absolute(worldDirectory);
            Directory.CreateDirectory(CharacterDirectoryPath);
            Directory.CreateDirectory(WorldDirectoryPath);
            this.migration=migration;
            Catalog=new WorldCatalog(WorldDirectoryPath,null,EnsureWorldLibrary,()=>LibraryWarnings);
        }
        public void EnsureWorldLibrary(System.Threading.CancellationToken cancellation=default)=>migration?.Ensure(cancellation);

        public WorldState LoadWorld(bool prepareForPlay=true)
        {
            EnsureWorldLibrary();
            using var access=SaveAccess.Enter(WorldDirectoryPath);
            string path = Path.Combine(WorldDirectoryPath, "world.json");
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
            if(InventoryStackMigration.Needed(world))
            {
                string backup=PreserveSource("world.json");
                world=InventoryStackMigration.Prepare(world,out int removed);
                Debug.LogWarning($"Стопки мира объединены: world={world.worldId}, убрано отдельных записей={removed}, старые ID сохранены как ссылки. Исходник: {backup}.");
            }
            foreach(var actor in world.actors) {Remains.Content.PonyAppearance.ValidateSettings(actor.appearance,actor.id);InventoryStacks.Validate(actor.inventory,actor.inventoryAliases);}
            foreach(var actor in world.actors) CosmeticEquipment.ReconcileInventory(actor);
            foreach(var container in world.containers) InventoryStacks.Validate(container.items,container.itemAliases);
            try {PropBodyRules.ValidateWorld(world);}
            catch(InvalidOperationException error)
            {throw new InvalidDataException($"Некорректные физические тела сейва: path={path}, world={world.worldId}. {error.Message}",error);}
            return world;
        }

        // Capture on the simulation owner thread before handing immutable bytes to disk I/O.
        public RecoverySnapshot CaptureWorldSnapshot(WorldState world)
        {
            if(world==null) throw new ArgumentNullException(nameof(world));
            foreach(var actor in world.actors) Remains.Content.PonyAppearance.ValidateSettings(actor.appearance,actor.id);
            ContentProvenance.Record(ref world.weaponContent,ref world.weaponContentHistory);
            if(world.historyVersion==1) world.checkpointSequence++;
            return RecoverySnapshot.FromWorld(world);
        }
        public RecoverySnapshot SaveWorld(WorldState world)
        {
            var snapshot=CaptureWorldSnapshot(world);
            SaveRecovery(snapshot);return snapshot;
        }

        public RecoverySnapshot SelectedSnapshot()
        {
            EnsureWorldLibrary();
            using var access=SaveAccess.Enter(WorldDirectoryPath);
            string path=Path.Combine(WorldDirectoryPath,"world.json");
            return File.Exists(path)?new RecoverySnapshot(File.ReadAllText(path)):null;
        }

        public List<RecoverySnapshot> Continuations()
        {
            var catalog=Catalog.Read();
            foreach(string warning in catalog.warnings) Debug.LogWarning(warning);
            var copies=new List<RecoverySnapshot>();
            foreach(var entry in catalog.entries)
            {
                try {copies.Add(entry.Load(WorldDirectoryPath));}
                catch(Exception error) when(error is InvalidDataException || error is ArgumentException)
                {Debug.LogWarning($"Полный снимок не прошёл проверку: файл={entry.file}, мир={entry.summary.worldId}; {error.Message}");}
            }
            return copies;
        }

        public void SelectContinuation(RecoverySnapshot snapshot) => SaveRecovery(snapshot);
        // Browsing must remain possible when the active file is corrupt. Loading stays strict.
        public RecoverySnapshot SelectedSnapshotForListing()
        {
            try {return SelectedSnapshot();}
            catch(InvalidDataException error)
            {Debug.LogWarning($"Выбранное сохранение не показано в списке: {WorldDirectoryPath}/world.json. {error.Message}");return null;}
        }

        public void SaveRecovery(RecoverySnapshot snapshot)
        {
            EnsureWorldLibrary();
            using var access=SaveAccess.Enter(WorldDirectoryPath);
            // Keep every distinct payload, including branches with identical revision numbers.
            string archive=Path.Combine("Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
            string path=Path.Combine(WorldDirectoryPath,archive);
            if(File.Exists(path))
            {
                if(!snapshot.MatchesFile(path))
                    throw new InvalidDataException($"Повреждён архив снимка: {path}.");
            }
            else {WriteWorld(archive,snapshot);PruneArchive(snapshot.WorldId);}
            PreserveExistingWorld("world.json");
            PreserveExistingWorld(Path.Combine("Worlds",snapshot.WorldId+".json"));
            string flat=Path.Combine("Worlds",snapshot.WorldId+".json");
            WriteWorld(flat,snapshot);RememberWorldFile(flat,snapshot);
            WriteWorld("world.json",snapshot);RememberWorldFile("world.json",snapshot);
        }

        private void PreserveExistingWorld(string filename)
        {
            string path=Path.Combine(WorldDirectoryPath,filename);
            if(!File.Exists(path)) return;
            RecoverySnapshot previous;
            try
            {
                var info=new FileInfo(path);
                var known=filename=="world.json"?knownActive:knownFlat;
                // The file this store last wrote: identified by its bytes' hash, without reading it into a string.
                if(known!=null && string.Equals(known.path,SavePath.Absolute(path),StringComparison.OrdinalIgnoreCase) &&
                    known.length==info.Length && known.ticks==info.LastWriteTimeUtc.Ticks && TextSha256.OfFile(path)==known.snapshot.Id)
                {previous=known.snapshot;ReusedWorldPayloads++;}
                else previous=new RecoverySnapshot(File.ReadAllText(path));
            }
            catch(InvalidDataException error)
            {
                string backup=PreserveSource(filename);
                Debug.LogWarning($"Повреждённое сохранение сохранено перед заменой: source={path}, backup={backup}. {error.Message}");
                return;
            }
            string archive=Path.Combine("Worlds",previous.WorldId,"Snapshots",previous.Id+".json");
            if(!File.Exists(Path.Combine(WorldDirectoryPath,archive))) WriteWorld(archive,previous);
        }
        // Archive rotation (2026-10-14): every checkpoint (3 s and shared actions) is a distinct ~1.25 MB payload, and
        // keeping all of them grew a factory world to 1.2 GB. The newest ArchiveKept copies of the world stay; the
        // newest is the one just written, which a pending recovery pair refers to.
        public const int ArchiveKept=10;
        private void PruneArchive(string worldId)
        {
            var directory=new DirectoryInfo(Path.Combine(WorldDirectoryPath,"Worlds",worldId,"Snapshots"));
            if(!directory.Exists) return;
            var files=directory.GetFiles("*.json");
            if(files.Length<=ArchiveKept) return;
            Array.Sort(files,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for(int i=ArchiveKept;i<files.Length;i++)
            {
                try {files[i].Delete();}
                catch(IOException error) {Debug.LogWarning($"Ротация архива мира {worldId}: не удалён {files[i].FullName}: {error.Message}");}
            }
        }
        private void RememberWorldFile(string filename,RecoverySnapshot snapshot)
        {
            string path=SavePath.Absolute(Path.Combine(WorldDirectoryPath,filename));var info=new FileInfo(path);
            var known=new KnownWorldFile {path=path,snapshot=snapshot,length=info.Length,ticks=info.LastWriteTimeUtc.Ticks};
            if(filename=="world.json") knownActive=known;else knownFlat=known;
        }
        // The caller freezes actor state before passing this operation to background I/O.
        // Host local clients keep newer canonical heads by setting publishWorld=false.
        public void SaveRecoveryWithCharacter(RecoverySnapshot snapshot,ActorState capturedActor,bool publishWorld=true)
            =>LocalRecoveryPair.Save(this,snapshot,CaptureCharacterJson(capturedActor),publishWorld,
                json=>WriteCharacter("character.json",json));
        public bool RecoverPendingPair()=>LocalRecoveryPair.Recover(this,json=>WriteCharacter("character.json",json));
        private static string CaptureCharacterJson(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            Remains.Content.PonyAppearance.ValidateSettings(actor.appearance,actor.id);
            var saved=actor.Copy();
            CosmeticEquipment.Validate(saved);
            ContentProvenance.Record(ref saved.weaponContent,ref saved.weaponContentHistory);
            string json=JsonUtility.ToJson(saved,true);
            // Unknown extension numbers/colliding fields must pass the same strict parser.
            JsonUtility.FromJson<ActorState>(json);
            return json;
        }
        public void SaveCharacter(ActorState actor)
        {
            string json=CaptureCharacterJson(actor);
            using var access=SaveAccess.Enter(CharacterDirectoryPath);
            if(File.Exists(LocalRecoveryPair.PendingPath(this)))
                throw new InvalidDataException($"Не завершена запись пары мира и персонажа: intent={LocalRecoveryPair.PendingPath(this)}. Сначала восстановите пару; профиль не заменён.");
            WriteCharacter("character.json",json);
        }
        public ActorState LoadCharacter()
            =>PrepareCharacter(ReadCharacterSource(),false);

        public string ReadCharacterSource()
        {
            RecoverPendingPair();
            string path=Path.Combine(CharacterDirectoryPath,"character.json");
            return File.Exists(path)?File.ReadAllText(path):null;
        }

        public ActorState PrepareCharacter(string source,bool preserve)
        {
            if(source==null) return null;
            var actor=JsonUtility.FromJson<ActorState>(source);
            Remains.Content.PonyAppearance.ValidateSettings(actor.appearance,actor.id);
            string backup=null;
            if(preserve || actor.loadoutVersion==0 || actor.weaponInventoryVersion==0 || InventoryStackMigration.Needed(actor))
            {
                backup=Path.Combine("Recovery","character-"+Guid.NewGuid().ToString("N")+".json");
                WriteCharacter(backup,source);
                backup=Path.Combine(CharacterDirectoryPath,backup);
            }
            actor.resolvedExplosions ??= new List<string>();actor.resolvedAttacks ??= new List<string>();
            if(actor.loadoutVersion==0) UpgradeLoadout(actor,backup);
            if(actor.weaponInventoryVersion==0) actor=UpgradeWeaponInventory(actor,backup);
            if(InventoryStackMigration.Needed(actor))
            {
                int removed=InventoryStackMigration.Apply(actor);
                Debug.LogWarning($"Стопки персонажа объединены: actor={actor.id}, убрано отдельных записей={removed}, старые ID сохранены как ссылки. Исходник: {backup}.");
            }
            InventoryStacks.Validate(actor.inventory,actor.inventoryAliases);
            CosmeticEquipment.ReconcileInventory(actor);
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
        public string PreserveCharacterSource()
            => PreserveSource("character.json",CharacterDirectoryPath);

        private static void UpgradeLoadout(ActorState actor,string backup)
        {
            WeaponAmmunition.Initialize(actor,true);
            actor.revision++;
            Debug.LogWarning($"Миграция боезапаса v0→v1: actor={actor.id}, добавлено стартовое оснащение прототипа (магазины и резерв); здоровье и существующие предметы сохранены. Исходник: {backup}.");
        }

        private string PreserveSource(string filename,string root=null)
        {
            root??=WorldDirectoryPath;
            string source=Path.Combine(root,filename);
            if(!File.Exists(source)) return null;
            string directory=Path.Combine(root,"Recovery");Directory.CreateDirectory(directory);
            string destination=Path.Combine(directory,Path.GetFileNameWithoutExtension(filename)+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".json");
            File.Copy(source,destination);
            return destination;
        }

        private void WriteWorld(string filename,string content)=>AtomicSaveFile.Write(Path.Combine(WorldDirectoryPath,filename),content);
        // Streamed from the snapshot's parts: no ~2.8 MB whole-text string per periodic save (GC pauses).
        private void WriteWorld(string filename,RecoverySnapshot snapshot)=>AtomicSaveFile.Write(Path.Combine(WorldDirectoryPath,filename),snapshot.WriteTo);
        private void WriteCharacter(string filename,string content)=>AtomicSaveFile.Write(Path.Combine(CharacterDirectoryPath,filename),content);
    }
}
