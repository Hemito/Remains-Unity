using System;
using System.Collections.Generic;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using UnityEditor;
using UnityEngine;
using Remains.Content;
using Remains.Gameplay;

namespace Remains.Editor
{
    public static class ContractChecks
    {
        public static void Run()
        {
            RoomCatalogLifetimeChecks.Run();
            ProjectStorageChecks.Run();LevelTimeReplicaChecks.Run();MechanismOperationsChecks.Run();
            ItemShortcutChecks.Run();
            InventoryItemActionChecks.Run();InventoryDescriptionChecks.Run();ItemLandingAudioChecks.Run();
            DoorStateChecks.Run();DoorContentChecks.Run();DoorBarrierChecks.Run();DoorDamageRequestChecks.Run();PhysicalContentChecks.Run();ContainerLootChecks.Run();NpcContentChecks.Run();RoomContentChecks.Run();
            FactoryLegacyDoorChecks.Run();ActorDeathAudioChecks.Run();HealingItemAudioChecks.Run();
            ContentEntityIdentityChecks.Run();
            PonyAppearance.Prepare();PonyPaletteChecks.Run();
            var music=Resources.Load<MusicCatalogAsset>("Content/area-music");
            if(music==null) throw new InvalidOperationException("Нет каталога музыки локаций.");
            music.Validate();
            CombatMusicChecks.Run();
            NpcPerceptionChecks.Run();
            ExplosionPrefabChecks.Run();
            Anatomy.Load();
            FootSupportContentChecks.Run();ActorBodySupportChecks.Run();ActorBodyConsumerChecks.Run();BodySupportReplicaChecks.Run();ActorBodySupport.Clear();
            StandingEdgePoseChecks.Run();StandingSupportPoseChecks.Run();
            DynamicSupportChecks.Run();PlatformSimulationChecks.Run();FactoryWorkshopMechanismChecks.Run();PlatformIntegrationChecks.Run();
            NpcDoorChecks.Run();
            ActorStepChecks.Run();
            SlopedSupportChecks.Run();SlopeAnimationAssetChecks.Run();SlopeLocomotionPoseChecks.Run();SlopeAnimationAudioChecks.Run();
            DoorVisualChecks.Run();DoorGeometryChecks.Run();DoorRequestChecks.Run();DoorClosureMotionChecks.Run();InteractionSelectionChecks.Run();TravelPointChecks.Run();
            PassiveActorMotionChecks.Run();
            JumpPoseChecks.Run();
            WalkCycleChecks.Run();
            FallPoseChecks.Run();
            ActorMotionReplicationChecks.Run();
            RemoteActorMotionChecks.Run();
            ReplicationCadenceChecks.Run();
            WeaponItemStateChecks.Run();
            HintPlacementChecks.Run();
            CameraFramingChecks.Run();
            WeaponCatalog.Load();
            WeaponRecoilMotionChecks.Run();WeaponRecoilReplicationChecks.Run();CasingEffectsChecks.Run();RangedShotPoseChecks.Run();
            RangedShotDirectionChecks.Run();
            RaiderWeaponSocketChecks.Run();
            NpcAmmunitionChecks.Run();
            CharacterInterfaceChecks.Run();
            ActorControlChecks.Run();
            DoorCombatChecks.Run();
            FactoryRoomContentChecks.Run();MineChecks.Run();HazardChecks.Run();FactoryEnemyScalingChecks.Run();FirearmChecks.Run();DoorDuplicateChecks.Run();CreatureChecks.Run();
            FactoryBigBoxChecks.Run();FactoryToolStandChecks.Run();PropBodyChecks.Run();ItemTextChecks.Run();
            FactoryWorkshopNpcChecks.Run();
            FactoryCrawlerChecks.Run();
            FactoryStairArtworkAuthoring.RunChecks();
            FactoryLadderLandingChecks.Run();
            FactoryOfficeConnectionChecks.Run();
            FactoryWarehouseChecks.Run();FactoryWarehouseNpcChecks.Run();FactoryWarehouseConnectionChecks.Run();FactoryWarehouseHistoryProjectionChecks.Run();
            FactoryWarehouseMetalDoorChecks.Run();FactoryWarehouseMetalDoorsPlacement.Verify();
            FactoryWarehouseFurnitureChecks.Run();ContainerOpenAudioChecks.Run();
            FactoryContinuationChecks.Run();FactoryContinuationObjectsChecks.Run();FactoryContinuationNpcChecks.Run();FactoryContinuationConnectionChecks.Run();
            FactoryOfficeNpcChecks.Run();
            FactoryFileCabinetChecks.Run();
            FactoryOfficeLootChecks.Run();
            FactoryOfficeFurnitureChecks.Run();
            FactoryOfficeOpeningsChecks.Run();
            FactoryOfficeWindowMaskChecks.Run();
            FactoryBackdropCompositionChecks.Run();
            OfficeRecoveryEvidenceChecks.Run();
            WorldRoomChecks.Run();
            SurfaceSoundChecks.Run();
            WeaponInventoryChecks.Run();
            InventoryStackChecks.Run();
            WeaponInventoryMigrationChecks.Run();
            WeaponInventoryUiChecks.Run();
            CosmeticEquipmentChecks.Run();CosmeticContentModChecks.Run();
            CosmeticInventoryUiChecks.Run();WearablePresentationChecks.Run();PonyMagicActivityChecks.Run();
            InventoryPauseChecks.Run();
            RoomLoadingViewChecks.Run();HudReadyChecks.Run();
            RoomLightingPreparationChecks.Run();
            TransitionTimingChecks.Run();
            RoomContentPublicationChecks.Run();
            WeaponAssetChecks.Run();
            EntityAssetChecks.Run();
            PropPrefabChecks.Run();
            ContainerAppearanceChecks.Run();
            FactoryRouteLootProofChecks.Run();FactoryContinuationRouteChecks.Run();FactoryContinuationStairMotionChecks.Run();
            RoomAuthoringChecks.Run();
            HubRangeAuthoring.CheckPassage();
            RoomTextureChecks.Run();
            RoomLightAuthoringChecks.Run();
            WeaponShotChecks.Run();
            WeaponPrefabChecks.Run();
            WorldLibraryChecks.Run();
            IndependentLibraryChecks.Run();
            WorldCatalogChecks.Run();
            WorldLibraryPerformanceChecks.Run();
            CharacterJoinSelectionChecks.Run();
            EntryPreparationChecks.Run();EntryGeometryChecks.Run();
            RecoveryCaptureChecks.Run();
            OrderedSaveWriteChecks.Run();
            CharacterPlayTimeChecks.Run();
            RecoveryDurabilityChecks.Run();
            LocalRecoveryPairChecks.Run();
            InteractionHotPathChecks.Run();
            CargoHotPathChecks.Run();
            CrouchChecks.Run();
            ProjectilePathChecks.Run();
            ShotArtworkChecks.Run();
            ProjectilePersistenceChecks.Run();
            ProjectileControllerChecks.Run();
            ProjectileTrailJoinChecks.Run();
            LocalProjectileChecks.Run();
            ActorTypeChecks.Run();
            ProjectileAudioChecks.Run();
            MeleeImpactAudioChecks.Run();
            ImpactAudioCoverageChecks.Run();
            RemotePropMotionChecks.Run();
            RaiderChecks.Run();
            AtlasAssetChecks.Run();
            PonyPrefabChecks.Run();
            PonyRenderChecks.Run();
            PonyAppearanceChecks.Run();
            MainMenuPrefabChecks.Run();
            SessionStartupChecks.Run();
            WorldVisualOrderChecks.Run();
            InteractionOutlineChecks.Run();
            var worldLobby=Resources.Load<Remains.Presentation.WorldLobbyView>("Presentation/WorldLobby");
            if(worldLobby==null) throw new InvalidOperationException("Нет префаба сетевого лобби.");
            worldLobby.Validate();
            CombatHudPrefabChecks.Run();
            SelectedItemHudChecks.Run();
            VectorArtworkReserveChecks.Run();
            PonyVectorReserveAuthoring.Run();
            var feedback=Resources.Load<Remains.Presentation.HudFeedbackView>("Presentation/HudFeedback");
            if(feedback==null) throw new InvalidOperationException("Нет префаба обратной связи HUD.");
            feedback.Validate();
            InventoryWindowChecks.Run();
            FloatingContainerChecks.Run();
            ContainerWindowLayoutChecks.Run();
            PonyArtworkBoundsChecks.Run();
            var pauseMenu=Resources.Load<Remains.Presentation.PauseMenuView>("Presentation/PauseMenu");
            if(pauseMenu==null) throw new InvalidOperationException("Нет префаба меню паузы.");
            pauseMenu.Validate();
            var audioSettings=Resources.Load<Remains.Presentation.AudioSettingsView>("Presentation/AudioSettings");
            if(audioSettings==null) throw new InvalidOperationException("Нет префаба настроек звука.");
            audioSettings.Validate();
            var worldManagement=Resources.Load<Remains.Presentation.WorldManagementView>("Presentation/WorldManagement");
            if(worldManagement==null) throw new InvalidOperationException("Нет префаба управления миром.");
            worldManagement.Validate();
            var worldPicker=Resources.Load<Remains.Presentation.WorldPickerView>("Presentation/WorldPicker");
            if(worldPicker==null) throw new InvalidOperationException("Нет префаба списка миров.");
            worldPicker.Validate();
            var comparison=Resources.Load<Remains.Presentation.ContentComparisonLayout>("Presentation/ContentComparison");
            if(comparison==null) throw new InvalidOperationException("Нет префаба сравнения контента.");
            comparison.Validate();
            PersonalInventoryPrefabChecks.Run();
            _=new Remains.Presentation.InventoryArtwork();
            LightingChecks.Run();
            RoomLightingUpdateChecks.Run();
            MotionAudioChecks.Run();ReloadAudioChecks.Run();WeaponReloadChecks.Run();InventoryAudioChecks.Run();
            HeldPropColorChecks.Run();WorldLibraryIdentityChecks.Run();
            DamageFeedbackChecks.Run();
            OwnershipStabilityChecks.Run();
            WeaponPoseChecks.Run();WeaponElevationChecks.Run();HeldItemChecks.Run();
            HoldingAreaChecks.Run();
            MovableContainerChecks.Run();
            PropPlatformChecks.Run();
            PropStackChecks.Run();
            FreePropMotionChecks.Run();
            PropPoseScopeChecks.Run();
            TelekinesisRuleChecks.Run();
            ActorHoldChecks.Run();
            MagicRecoveryChecks.Run();
            AttackContactChecks.Run();
            WeaponModChecks.Run();
            GameplayContentChecks.Run();
            LongSavePathChecks.Run();
            CharacterContentChecks.Run();
            GameplayRuleChecks.Run();
            LevelPauseChecks.Run();
            LevelTimeChecks.Run();
            MovingPlatformChecks.Run();
            ButtonRepairChecks.Run();
            AtomicSaveChecks.Run();
            DepositChecks.Run();
            BodyInventoryChecks.Run();
            PonyAnimationData.Load();
            var idle=Array.Find(PonyAnimationData.Library.clips,clip=>clip.id=="idle");
            Require(idle!=null && idle.frames.Length==1,"Покой содержит только позу стояния, без сидения и переходов прыжка");
            Require(PonyAnimationData.FrameIndex("idle",0,false)==PonyAnimationData.FrameIndex("idle",120,false),"Поза покоя не меняется со временем");
            foreach(var clip in PonyAnimationData.Library.clips)
                foreach(var frame in clip.frames)
                {
                    int devices=0;
                    foreach(var part in frame.parts)
                    {
                        if(part.path.StartsWith("pip") && PonyAppearance.IsVisible(part)) devices++;
                        if(part.path.Contains("/eye/eye/") && (part.shape=="201" || part.shape=="202"))
                            Require(part.texture.Contains("-masked-"),$"Глаз должен учитывать маску: {clip.id}, {part.path}");
                        Require(!(part.shape=="200" && part.path.Contains("/eye/")),"Маска глаза не должна отображаться отдельной фигурой");
                    }
                    Require(devices==1,$"Один пипбак в кадре {clip.id}");
                }
            var actor=new ActorState { id="victim",owner="client-b",player=true,connected=true };
            var damage=new DamageReceiver(new PrototypeDamageRule());
            var attack=new AttackEvent { id="shot-1",weapon="pistol",attacker="client-a",level="factory:0",originX=-2,originY=0.7f,endX=2,endY=0.7f };
            Require(HitGeometry.Contacts(actor,attack),"Сегмент должен пересекать корпус");
            Require(!damage.Apply(actor,attack,false) && actor.health==100,"Пострадавший может отклонить попадание");
            Require(damage.Apply(actor,attack,true) && actor.health==89,"Одно принятое попадание");
            Require(!damage.Apply(actor,attack,true) && actor.health==89,"Повтор события не наносит урон");
            var transferred=actor.Copy();transferred.owner="new-owner";transferred.generation++;
            Require(!new DamageReceiver(new PrototypeDamageRule()).Apply(transferred,attack,true) && transferred.health==89,
                "Передача копии другому владельцу сохраняет журнал урона");
            actor.down=true;
            attack.id="shot-2";
            damage.Apply(actor,attack,true);
            Require(actor.down,"Урон не поднимает отключённое тело");

            var world=new WorldState();world.actors.Add(actor);
            var second=new ActorState {id="second",owner="client-c",player=true,connected=true,x=5};world.actors.Add(second);
            var npc=new ActorState {id="npc",owner="client-b",target=actor.id,x=4,attackBusy=true};world.actors.Add(npc);
            var ownership=new OwnershipService();
            ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-b","Начатая атака сохраняет владельца");
            npc.attackBusy=false;ownership.Reassign(world,new[]{"client-b","client-c"});
            Require(npc.owner=="client-c" && npc.generation==1,"После атаки ближайший активный клиент получает NPC");
            npc.attackCooldown=.01f;npc.attackId="last-known-finished";
            ownership.Reassign(world,new[]{"client-b"});
            Require(!npc.attackBusy && npc.attackId==null && npc.attackCooldown==NpcAttackRules.CooldownSeconds,
                "Потеря владельца сбрасывает атаку и паузу даже при устаревшем idle-снимке");

            var item=SharedInventory.Create("caps",20);world.containers.Add(new ContainerState {x=0,items=new List<ItemStack>{item}});actor.down=false;
            var inventory=new SharedInventory(world);
            Require(inventory.PrepareClaim("claim-1",ContainerState.PrototypeId,item.id,actor),"Первый запрос резервирует предмет");
            Require(actor.inventory.Count==0 && world.pendingGrants.Count==1,"Резерв не выдаёт предмет до записи копий");
            second.x=1;
            Require(!inventory.PrepareClaim("claim-2",ContainerState.PrototypeId,item.id,second),"Другой клиент не получает резерв");
            inventory.CommitGrant(world.pendingGrants[0]);
            Require(actor.inventory.Count==1 && second.inventory.Count==0,"Общий предмет получает один игрок");

            actor.down=true;
            var directory=Path.Combine(Path.GetTempPath(),"Remains-contracts-"+Guid.NewGuid().ToString("N"));
            var saves=new SaveStore(directory);saves.SaveWorld(world);saves.SaveWorld(world);
            var restored=saves.LoadWorld();
            Require(restored.worldId==world.worldId && restored.Find(actor.id).down && restored.Find(actor.id).health==78,
                "Сохранение восстанавливает идентичность мира и состояние тела");
            Require(restored.completedTransactions.Contains("claim-1") && File.Exists(Path.Combine(directory,"world.json.previous")),
                "Сохранение сохраняет транзакцию и предыдущую версию");
            MovementChecks.Run();
            ActorActivityChecks.Run();
            ValidatedStateCopyChecks.Run();
            SessionChecks.Run();
            MeleeSweepChecks.Run();
            MeleeTerrainOcclusionChecks.Run();
            AmmunitionChecks.Run(); RecoveryChecks.Run(); RevivalChecks.Run(); GrenadeChecks.Run(); NpcAttackChecks.Run(); NpcPerceptionIntegrationChecks.Run(); ContainerChecks.Run(); StateExtensionChecks.Run(); BodyTopSurfacesChecks.Run(); BodySleepChecks.Run(); BodySupportDoorChecks.Run(); ActorTextChecks.Run(); ActorStreamChecks.Run();
            actor.connected=true;actor.down=true;second.connected=false;second.down=false;
            actor.injuries.Add(new InjuryState{id="infection",periodicDamage=true});
            actor.injuries.Add(new InjuryState{id="fracture",periodicDamage=false});
            Require(PartyRules.ResolveDefeat(world) && actor.level=="hub" && actor.health==1 &&
                actor.injuries.Count==1 && actor.injuries[0].id=="fracture", "Отключённый игрок не мешает поражению; хаб сохраняет непериодические ранения");
            var stale=actor.Copy();stale.lifecycleSequence--;stale.revision=actor.revision+100;
            Require(!ActorVersion.CanPublishOwnerState(stale,actor) && ActorVersion.IsOlder(stale,actor),
                "Поздний пакет прежнего состояния не отменяет поднятие/поражение группы");
            var spear=new PrototypeDamageRule();
            Require(spear.Calculate("spear",10)>spear.Calculate("spear",1) && spear.Calculate("spear",1000)==76,
                "Урон копья зависит от скорости и имеет определённый данными предел");
            Debug.Log("Проверки контрактов: PASS — урон пострадавшего, дедупликация, тело, передача владельца, резерв/выдача, сохранение.");
        }
        private static void Require(bool condition,string detail)
        {
            if(!condition) throw new InvalidOperationException("Проверка контракта не прошла: "+detail);
        }
    }
}
