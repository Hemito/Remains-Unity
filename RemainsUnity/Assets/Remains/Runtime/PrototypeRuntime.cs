using System.Collections.Generic;
using System.Collections;
using System.IO;
using kcp2k;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using Remains.Content;
using Remains.Diagnostics;

namespace Remains.Runtime
{
    public sealed class PrototypeRuntime : MonoBehaviour
    {
        public CoopSession Session { get; private set; }
        public CombatController Combat { get; private set; }
        public ItemShortcutActions ItemActions {get;private set;}
        public InteractionFocus InteractionFocus {get;}=new();
        public LooseItemPickup Pickup {get;private set;}
        public MechanismInteraction MechanismInteraction {get;private set;}
        public PlatformSimulation Platforms {get;private set;}
        private readonly PassengerMotionRegistry passengers=new();
        public HeldItemVisuals HeldItems {get;private set;}
        private Vector2? lastGameplayAim;
        public LaunchOptions Options { get; private set; }
        public RevivalController Revival { get; private set; }
        public InteractionAdapter Interactions { get; private set; }
        public GrenadeController Grenades { get; private set; }
        public TelekinesisController Telekinesis { get; private set; }
        public SimulationGeometry Geometry { get; private set; }
        public GameplayAudio Audio {get;private set;}
        public GrenadeImpactAudio GrenadeAudio {get;private set;}
        public ProjectileAudio ImpactAudio {get;private set;}
        public MeleeImpactAudio MeleeAudio {get;private set;}
        public AreaMusic Music {get;private set;}
        public MovementAudio MovementAudio {get;private set;}
        public PropContactAudio PropAudio {get;private set;}
        public ContainerOpenAudio ContainerAudio {get;private set;}
        public HealingItemAudio HealingAudio {get;private set;}
        public ActorDeathAudio DeathAudio {get;private set;}
        public ExplosionEffects Explosions {get;private set;}
        public AudioPreferences AudioPreferences {get;private set;}
        public DoorVisuals DoorVisuals {get;private set;}
        private float musicMixVolume;
        public DamageFeedback DamageFeedback {get;private set;}
        public DamageNumbers DamageNumbers {get;private set;}
        public System.Func<PlayerCommand> CommandSource { get; set; }
        public PrototypeHud Hud => hud;
        private IRoomLighting ActiveLevelLighting {get;set;}
        public RoomLighting ActiveRoomLighting=>ActiveLevelLighting as RoomLighting;
        public FactoryLevelView ActiveFactoryLighting=>ActiveLevelLighting as FactoryLevelView;
        public bool LevelPresentationReady=>roomPresentation?.IsReady==true;
        public bool IsLevelPreparing=>roomPresentation?.IsPreparing==true;
        public Camera WorldCamera=>cameraView;
        public long TerrainPairSnaps=>remoteMotion.TerrainPairSnaps;
        private RoomPresentationGate roomPresentation;
        private EntryRoomPreparation entryRoom;
        public RoomLighting EntryPreparationLighting=>entryRoom?.Lighting;
        public RoomLayout EntryPreparationRoom=>entryRoom?.Room;
        public bool EntryRoomPrepared=>entryRoom?.IsPrepared==true;
        public event System.Action<EntryPreparationContext> EntryPresentationPrepared;
        private bool roomFailurePending;
        private bool automaticStartCancelled;
        private MovementController movement;
        private MovementSettings passiveMovementSettings;
        private NpcController npcs;
        private FreePropSimulation freeProps;
        private readonly Dictionary<string,ActorVisual> visuals = new();
        private readonly List<RoomRadianceField.LightOccluder> muzzleOccluders=new();
        private readonly PonyMagicActivitySource ponyMagic=new();
        private readonly RemoteActorMotion remoteMotion=new();
        internal ActorVisual FindActorVisual(string id)=>visuals.TryGetValue(id,out var visual)?visual:null;
        private ActorVisualLayers visualLayers;
        private readonly WorldVisualOrderTable visualOrders=new();
        private readonly TravelPointVisuals travelPointVisuals=new();
        private PrototypeHud hud;
        private SaveLibraries saveLibraries;
        private Camera cameraView;
        private readonly ReplicationCadence poseCadence=new(.05);
        private readonly WeaponElevation weaponElevation=new();
        private GameObject roomObject;
        private string shownLevel="hub";
        private string shownRoom="base:hub";
        private CameraFollower cameraFollower;
        private bool releaseRequested;
        private System.IDisposable roomCatalogLease;
        private readonly List<GameObject> ownedObjects=new();
        private GameObject networkObject;
        private bool disposed;
        private string bodyWorld;
        public bool ReleasingUnusedAssets {get;private set;}

        private void Awake()
        {
            try { Initialize(); }
            catch(System.Exception failure)
            {
                enabled=false;
                try {DisposeRuntime();}
                catch(System.Exception cleanup) {Debug.LogError("Не удалось завершить частичную загрузку игры: "+cleanup,this);}
                StartupFailure.Show(failure);
            }
        }

        private void Initialize()
        {
            Options=LaunchOptions.Parse();
            if(Options.Smoke=="catalog-lifetime")
            {
                enabled=false;
                gameObject.AddComponent<RoomCatalogLifetimeProbe>().Initialize(Options);
                return;
            }
            TransitionTiming.Enabled=Options.Smoke?.StartsWith("lighting-preparation")==true;
            TransitionTiming.Reset();
            using(TransitionTiming.Measure("runtime.catalog-prepare",GetInstanceID().ToString()))
                roomCatalogLease=RoomCatalogAsset.AcquireRuntime("PrototypeRuntime:"+GetInstanceID());
            Anatomy.Load();
            FootSupportCatalog.Load();
            passiveMovementSettings=MovementSettings.Load();
            WeaponCatalog.Load();
            Application.runInBackground=true;
            Application.targetFrameRate=Options.Smoke==null?60:Options.ProbeFrameRate;
            networkObject=new GameObject("Сетевая сессия");
            networkObject.SetActive(false);
            var transport=TransportSetup.Create(networkObject,Options.Port,Options.NetworkStress);
            Session=networkObject.AddComponent<CoopSession>(); Session.transport=transport;
            Pickup=new LooseItemPickup(Session);
            // Mirror configures headless frame rate during StartClient/StartHost.
            // Apply the requested probe limit after that, including reconnections.
            if(Options.Smoke!=null) Session.ClientReady+=()=>Application.targetFrameRate=Options.ProbeFrameRate;
            Session.ConfigureRules(WeaponCatalog.Rules);
            Session.UseLobby=Options.Smoke==null || Options.Smoke.StartsWith("lobby") || Options.Smoke.StartsWith("duplicate-lobby") || Options.Smoke.StartsWith("protocol") || Options.Smoke.StartsWith("grenade-resume-"); Session.autoCreatePlayer=false; Session.maxConnections=Options.Capacity;
            Session.CharacterId=Options.Character;
            Session.DebugTravelAllowed=Options.Smoke!=null && !Options.Smoke.StartsWith("travel-points") && !Options.Smoke.StartsWith("prop-body") && !Options.Smoke.StartsWith("lighting-preparation");
            Session.networkAddress=Options.Address;
            bool characterLibraryProbe=Options.Smoke?.StartsWith("characters")==true;
            if(Options.SaveDirectory==null || characterLibraryProbe)
                saveLibraries=new SaveLibraries(characterLibraryProbe?Options.SaveDirectory:ProjectStorage.RootDirectory,Options.Character);
            Session.Saves=saveLibraries?.ForCharacter(Options.Character)??new SaveStore(Options.SaveDirectory);
            if(Options.Smoke!=null && Options.Role=="host" && !string.IsNullOrEmpty(Options.Report))
                gameObject.AddComponent<RecoveryTimingProbe>().Initialize(Session,Options.Report);
            // NetworkManager.Awake must see the final transport, including a test wrapper.
            networkObject.SetActive(true);
            DontDestroyOnLoad(gameObject);
            // Publish the shared order before simulation Changed callbacks can
            // create restored projectile, grenade or actor representations.
            Session.Changed+=RefreshVisualOrders;
            Session.GameplayPreparationFailed+=GameplayPreparationFailed;
            RefreshVisualOrders();
            Geometry=new SimulationGeometry(Session);
            Geometry.OcclusionChanged+=RefreshRoomOcclusion;
            Platforms=new PlatformSimulation(Session,Geometry);
            Platforms.TopologyChanged+=ActorBodySupport.InvalidateRoom;
            Session.Changed+=RefreshBodyBindings;
            Geometry.OcclusionChanged+=RefreshBodyGeometry;
            MechanismInteraction=new MechanismInteraction(Session);
            Combat=new CombatController(Session,Geometry);
            DamageFeedback=new DamageFeedback(Session);
            DamageNumbers=CreateOwnedObject("Числа урона").AddComponent<DamageNumbers>();DamageNumbers.Initialize(Session,DamageFeedback);
            if(Options.Diagnostics || Options.Smoke!=null)
                gameObject.AddComponent<SessionTimingDiagnostics>().Initialize(this);
            Combat.DamageApplied+=DamageFeedback.Publish;
            Revival=new RevivalController(Session);
            Interactions=new InteractionAdapter(Session);
            Grenades=new GrenadeController(Session,Interactions,Combat,Geometry);
            Telekinesis=new TelekinesisController(Session,Interactions,Geometry);
            freeProps=new FreePropSimulation(Session,Geometry,Interactions);
            visualLayers=new ActorVisualLayers(transform,visualOrders);
            var interactionVisuals=CreateOwnedObject("Визуальные взаимодействия").AddComponent<InteractionVisuals>();
            interactionVisuals.Initialize(Session,Interactions,Grenades,Geometry,visualLayers,Combat.Reload);
            interactionVisuals.ConfigureMuzzleLighting(emissions=>
            {
                if(!LevelPresentationReady || ActiveFactoryLighting==null) return;
                // Actors and weapons shadow lamps every frame, not only flashes; the field recomputes only on change.
                muzzleOccluders.Clear();
                foreach(var visual in visuals.Values) visual.AppendLightOccluders(muzzleOccluders);
                interactionVisuals.AppendWeaponLightOccluders(muzzleOccluders);
                ActiveFactoryLighting.RenderMuzzleFlashes(emissions,muzzleOccluders);
            });
            CreateOwnedObject("Цвет удерживаемых предметов").AddComponent<HeldPropGlowPresentation>().Initialize(Session,interactionVisuals,ponyMagic);
            HeldItems=CreateOwnedObject("Удерживаемые вещи").AddComponent<HeldItemVisuals>();HeldItems.Initialize(Session,visualLayers);
            var room=RoomLayout.Load(shownLevel);
            movement=CreateMovement(room);
            npcs=new NpcController(Session,Geometry);
            roomObject=AuthoredRoomVisual.Create(shownLevel,room);
            ActiveLevelLighting=roomObject.GetComponent<IRoomLighting>();
            travelPointVisuals.Bind(roomObject);
            cameraView=CameraFollower.RequireCamera(Camera.main);
            roomPresentation=new RoomPresentationGate(cameraView);roomPresentation.Failed+=RoomPreparationFailed;
            roomPresentation.Bind(ActiveLevelLighting);
            cameraFollower=new CameraFollower(cameraView,room);
            Audio=CreateOwnedObject("Звуки игры").AddComponent<GameplayAudio>();Audio.Initialize(Session,Combat.Reload,cameraView);
            HealingAudio=CreateOwnedObject("Звуки лечения").AddComponent<HealingItemAudio>();HealingAudio.Initialize(Session.HealingAudio,Audio);
            DeathAudio=CreateOwnedObject("Звуки смерти NPC").AddComponent<ActorDeathAudio>();DeathAudio.Initialize(Session,DamageFeedback,Audio);
            DoorVisuals=CreateOwnedObject("Двери мира").AddComponent<DoorVisuals>();DoorVisuals.Initialize(Session,Audio,visualOrders);
            var mechanismVisuals=CreateOwnedObject("Механизмы мира").AddComponent<MechanismVisuals>();mechanismVisuals.Initialize(Session,Platforms,visualOrders);
            GrenadeAudio=CreateOwnedObject("Звуки отскока гранаты").AddComponent<GrenadeImpactAudio>();GrenadeAudio.Initialize(Session,Grenades,Audio);
            MovementAudio=CreateOwnedObject("Звуки движения").AddComponent<MovementAudio>();MovementAudio.Initialize(Session,Geometry,Audio);
            PropAudio=CreateOwnedObject("Звуки физических объектов").AddComponent<PropContactAudio>();PropAudio.Initialize(Session,Geometry,Audio);
            ContainerAudio=CreateOwnedObject("Звуки контейнеров").AddComponent<ContainerOpenAudio>();ContainerAudio.Initialize(Audio);
            ImpactAudio=CreateOwnedObject("Звуки попаданий").AddComponent<ProjectileAudio>();ImpactAudio.Initialize(Combat.Projectiles,Audio);
            MeleeAudio=CreateOwnedObject("Звуки контактов ближнего оружия").AddComponent<MeleeImpactAudio>();MeleeAudio.Initialize(Combat,Audio);
            CreateOwnedObject("Изображения снарядов").AddComponent<ProjectileVisuals>().Initialize(Session,Combat.Projectiles,visualOrders);
            CreateOwnedObject("Попадания лазера").AddComponent<LaserImpactEffects>().Initialize(Session,Combat.Projectiles,visualOrders);
            Explosions=CreateOwnedObject("Эффекты взрывов").AddComponent<ExplosionEffects>();Explosions.Initialize(Session,visualOrders);
            Music=CreateOwnedObject("Музыка локации").AddComponent<AreaMusic>();Music.Initialize(Session);
            if(Application.isBatchMode && Options.Smoke!=null && string.IsNullOrEmpty(Options.SaveDirectory))
                throw new System.InvalidOperationException("Фоновая проверка требует отдельного каталога --save-dir для настроек.");
            AudioPreferences=new AudioPreferences(Path.Combine(Options.SaveDirectory??ProjectStorage.RootDirectory,"Settings","audio.json"));
            musicMixVolume=Music.Volume;AudioPreferences.Changed+=ApplyAudioPreferences;ApplyAudioPreferences();
            PonyVisual.Prepare();
            Session.Changed+=RefreshVisuals;
            entryRoom=new EntryRoomPreparation(this,Session,Geometry,BindEntryPresentation);
            entryRoom.Prepared+=context=>EntryPresentationPrepared?.Invoke(context);
            ItemActions=new ItemShortcutActions(Session,EquipWeaponItem);
            if (!Application.isBatchMode || Options.Smoke?.StartsWith("lighting-preparation")==true || Options.Smoke=="performance-audit" || Options.Smoke?.StartsWith("characters")==true || Options.Smoke?.StartsWith("prop-body")==true || Options.Smoke?.StartsWith("travel-points")==true || Options.Smoke?.StartsWith("door")==true || Options.Smoke?.Contains("-frames-")==true || Options.Smoke?.Contains("-mismatch-")==true || (Options.Smoke?.StartsWith("pause")==true || Options.Smoke?.StartsWith("body")==true) || Options.Smoke?.Contains("-ffui-")==true || Options.Smoke?.StartsWith("journey")==true || Options.Smoke?.StartsWith("session")==true || Options.Smoke?.StartsWith("container")==true || Options.Smoke=="history" || Options.Smoke=="world-management" || Options.Smoke=="world-library-volume" || Options.Smoke?.StartsWith("lobby")==true || Options.Smoke?.StartsWith("duplicate")==true)
            {
                hud=CreateOwnedObject("Интерфейс").AddComponent<PrototypeHud>(); hud.Initialize(Session,AudioPreferences,cameraView);
                roomPresentation.SetHud(hud);
                hud.LocalActorVisual=()=>Session.LocalActor==null?null:FindActorVisual(Session.LocalActor.id);
                hud.LocalWeaponArtwork=()=>Session.LocalActor==null?null:interactionVisuals.FindWeapon(Session.LocalActor.id)?.Artwork;
                hud.LocalHeldItemArtwork=()=>Session.LocalActor==null?null:HeldItems.Find(Session.LocalActor.id);
                var outlineSources=new InteractionOutlineSources(visualLayers,interactionVisuals,DoorVisuals,travelPointVisuals,mechanismVisuals);
                hud.ConfigureWorldOutline(cameraView,outlineSources.Resolve);
                hud.SelectedInteraction=()=>InteractionFocus.Target;
                hud.ContainerOpened+=ContainerAudio.Play;
                hud.PickupPending=()=>Pickup.Pending;
                hud.MechanismProgress=()=>MechanismInteraction.Progress;
                hud.MechanismPending=()=>MechanismInteraction.Pending;
                hud.ReloadRemaining=()=>Combat.Reload.Active?Combat.Reload.Remaining:(float?)null;
                hud.RevivalProgress=()=>Revival.Progress;hud.RevivalPending=()=>Revival.Pending;
                hud.EquipWeaponItem=EquipWeaponItem;
                hud.ConfigureItemActions(ItemActions);
                hud.UseItemShortcut=slot=>ItemActions.Activate(slot,lastGameplayAim,Application.isFocused &&
                    (Keyboard.current?.leftShiftKey.isPressed==true || Keyboard.current?.rightShiftKey.isPressed==true));
                hud.EquipWeapon=weapon=>{if(Session.LocalActor is {down:false}) Combat.Weapon=weapon;};
                bool characterProbe=Options.Smoke?.StartsWith("characters")==true;
                var characterLibrary=saveLibraries?.Characters??new CharacterLibrary(Path.Combine(ProjectStorage.RootDirectory,"Profiles"),Options.SaveDirectory);
                hud.SelectCharacter=name=>
                {
                    if(Session.CharacterSelectionLocked && name!=Session.CharacterId)
                    {hud.ShowMessage("Персонаж уже выбран для подключения. Сначала выйдите из сессии.");return false;}
                    if(!System.Text.RegularExpressions.Regex.IsMatch(name??"",@"^[\p{L}\p{N}_ -]{1,32}$"))
                    {if(Options.Smoke==null) hud.ShowCharacterLibrary();else hud.ShowMessage("Имя персонажа: от 1 до 32 букв, цифр, пробелов, дефисов или подчёркиваний.");return false;}
                    if((Options.Smoke==null || characterProbe) && !File.Exists(Path.Combine(characterLibrary.ProfilePath(name),"character.json")))
                    {hud.ShowCharacterLibrary();return false;}
                    if(Session.CharacterId==name) return true;
                    Session.CharacterId=name;
                    Options.SetSelectedCharacter(name);
                    Session.Saves=saveLibraries?.ForCharacter(name)??new SaveStore(characterLibrary.ProfilePath(name));
                    Session.CharacterContent.Reset();
                    return true;
                };
                hud.ConfigureCharacters(characterLibrary,Options.Smoke==null && Options.Role==null);
                hud.HostRequested+=()=>Session.StartHost();
                hud.JoinRequested+=address=> { Session.networkAddress=address; Session.StartClient(); };
            }
            if(Options.Smoke?.Contains("-attacker-")==true) Session.ServerReady+=()=>Session.SetFriendlyFireDecision(FriendlyFireDecision.Attacker);
            if (Options.Smoke != null)
            {
                if(Options.Smoke=="runtime-catalog-release") new GameObject("Проверка завершения runtime").AddComponent<RuntimeCatalogReleaseProbe>().Initialize(this);
                else if(Options.Smoke=="characters-guest") gameObject.AddComponent<SelectedWorldGuestProbe>().Initialize(this);
                else if(Options.Smoke is "characters-selection" or "characters-copies" or "characters-world-content" or "characters-coop") gameObject.AddComponent<CharacterSelectionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("characters")) gameObject.AddComponent<CharacterMenuProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("audio-settings")) gameObject.AddComponent<AudioSettingsProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("cosmetics")) gameObject.AddComponent<NetworkCosmeticEquipmentProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("appearance")) gameObject.AddComponent<NetworkAppearanceProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("surface-audio")) gameObject.AddComponent<NetworkSurfaceAudioProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("contact-audio")) gameObject.AddComponent<NetworkContactAudioProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("prop-body")) gameObject.AddComponent<NetworkPropBodyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("ladder-contact")) gameObject.AddComponent<NetworkLadderContactProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("mechanism")) gameObject.AddComponent<NetworkMechanismProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("warehouse-furniture")) gameObject.AddComponent<FactoryWarehouseFurnitureProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("warehouse-metal-doors")) gameObject.AddComponent<FactoryWarehouseMetalDoorProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("raid-entry-pending-")) gameObject.AddComponent<PendingBodyDamageProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("raid-entry")) gameObject.AddComponent<RaidEntryReadinessProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("warehouse-reload")) gameObject.AddComponent<WarehouseSaveReloadProbe>().Initialize(this);
                else if((Options.Smoke.StartsWith("warehouse-seam") || Options.Smoke.StartsWith("continuation-seam"))) gameObject.AddComponent<FactoryWarehouseSeamProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("office-furniture")) gameObject.AddComponent<FactoryOfficeFurnitureProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("office-opening")) gameObject.AddComponent<FactoryOfficeOpeningProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("lighting-preparation")) gameObject.AddComponent<RoomLightingPreparationProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("lighting-lifecycle")) gameObject.AddComponent<RoomLightingLifecycleProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("office-recovery")) gameObject.AddComponent<FactoryOfficeRecoveryProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("tool-stand") || Options.Smoke.StartsWith("office-cabinet")) gameObject.AddComponent<NetworkToolStandProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("crawler")) gameObject.AddComponent<NetworkCrawlerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("projectile-handoff")) gameObject.AddComponent<NetworkProjectileHandoffProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("factory-room")) gameObject.AddComponent<NetworkFactoryRoomProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("travel-points")) gameObject.AddComponent<NetworkTravelPointProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("raid-reload")) gameObject.AddComponent<NetworkRaidReloadProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("legacy-doors")) gameObject.AddComponent<FactoryLegacyDoorProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("actor-death")) gameObject.AddComponent<NetworkActorDeathAudioProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("door")) gameObject.AddComponent<NetworkDoorProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("shot-spread")) gameObject.AddComponent<NetworkShotSpreadProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("weapon-reload")) gameObject.AddComponent<NetworkWeaponReloadProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("slope-poses")) gameObject.AddComponent<NetworkSlopePoseProbe>().Initialize(this,interactionVisuals);
                else if(Options.Smoke.StartsWith("held-prop-color")) gameObject.AddComponent<NetworkHeldPropColorProbe>().Initialize(this,interactionVisuals);
                else if(Options.Smoke.StartsWith("training-layers")) gameObject.AddComponent<NetworkTrainingLayerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("training")) gameObject.AddComponent<NetworkTrainingProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("ladder-body")) gameObject.AddComponent<NetworkLadderBodyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("obstacle")) gameObject.AddComponent<NetworkObstacleProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("journey")) gameObject.AddComponent<NetworkJourneyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("lobby")) gameObject.AddComponent<NetworkLobbyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pauserestart")) gameObject.AddComponent<NetworkPauseRestartProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pausegroup")) gameObject.AddComponent<NetworkPauseGroupProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pause")) gameObject.AddComponent<NetworkPauseProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("rules") || Options.Smoke.StartsWith("pause")) gameObject.AddComponent<NetworkRulesProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("extensions")) gameObject.AddComponent<NetworkExtensionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("duplicate")) gameObject.AddComponent<NetworkDuplicateNameProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("protocol")) gameObject.AddComponent<NetworkProtocolProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("bodyrevival")) gameObject.AddComponent<NetworkBodyRevivalProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("body")) gameObject.AddComponent<NetworkBodyInventoryProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("receipt")) gameObject.AddComponent<NetworkReceiptLossProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("shortcuts") || Options.Smoke.StartsWith("direct-items")) gameObject.AddComponent<NetworkItemShortcutProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("container-window")) gameObject.AddComponent<NetworkContainerWindowProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("container")) gameObject.AddComponent<NetworkContainerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc") && Options.Smoke.Contains("-door-")) gameObject.AddComponent<NetworkNpcDoorProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc") && (Options.Smoke.Contains("-workshop-loot-") || Options.Smoke.Contains("-office-loot-"))) gameObject.AddComponent<NetworkWorkshopLootProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc") && Options.Smoke.Contains("-ammo-")) gameObject.AddComponent<NetworkNpcAmmunitionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc")) gameObject.AddComponent<NetworkNpcProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("freeze")) gameObject.AddComponent<NetworkFreezeProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("telekinesis")) gameObject.AddComponent<NetworkTelekinesisProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("actor-hold")) gameObject.AddComponent<NetworkActorHoldProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("melee-occlusion")) gameObject.AddComponent<NetworkMeleeOcclusionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("melee")) gameObject.AddComponent<NetworkMeleeProbe>().Initialize(this);
                else if(Options.Smoke=="performance-audit") gameObject.AddComponent<RuntimePerformanceProbe>().Initialize(this);
                else if(Options.Smoke=="world-library-volume") gameObject.AddComponent<WorldLibraryVolumeProbe>().Initialize(this);
                else if(Options.Smoke=="world-management") gameObject.AddComponent<WorldManagementProbe>().Initialize(this);
                else if(Options.Smoke=="history") gameObject.AddComponent<WorldSelectionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("session")) gameObject.AddComponent<NetworkSessionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("grenade")) gameObject.AddComponent<NetworkGrenadeProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("revival")) gameObject.AddComponent<NetworkRevivalProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("weapon-transfer")) gameObject.AddComponent<NetworkWeaponTransferProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("ammo")) gameObject.AddComponent<NetworkAmmunitionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("movement")) gameObject.AddComponent<NetworkMovementProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("contracts")) gameObject.AddComponent<NetworkContracts>().Initialize(this);
                else gameObject.AddComponent<NetworkSmoke>().Initialize(this);
                if(Options.Smoke.StartsWith("shot-spread-frames-",System.StringComparison.Ordinal))
                    gameObject.AddComponent<WeaponShotEffectsFrameProbe>().Initialize(this,interactionVisuals);
                if(Options.Smoke.Contains("-backdrop-",System.StringComparison.Ordinal)) gameObject.AddComponent<FactoryBackdropFrameProbe>().Initialize(this);
            }
        }

        private IEnumerator Start()
        {
            if(automaticStartCancelled) yield break;
            if(Options.Smoke?.StartsWith("characters")==true && Options.Smoke!="characters-guest") yield break;
            LegacyPrototypeFixture.Prepare(this);
            if(saveLibraries!=null && Options.Role!=null)
            {
                var preparation=System.Threading.Tasks.Task.Run(()=>Session.Saves.EnsureWorldLibrary());
                while(!preparation.IsCompleted) {if(automaticStartCancelled) yield break;yield return null;}
                if(preparation.IsFaulted)
                {
                    Debug.LogError("Не удалось подготовить общую библиотеку миров: "+preparation.Exception);
                    Session.ShowNotice("Не удалось подготовить библиотеку миров. Подключение не начато; подробности — в журнале игры.");
                    yield break;
                }
            }
            if(automaticStartCancelled) yield break;
            if (Options.Role=="host") Session.StartHost();
            else if (Options.Role=="client") Session.StartClient();
        }

        private void RefreshVisualOrders()=>visualOrders.Reconcile(Session.World,Session.LocalActor?.level);

        private void RefreshRoomOcclusion(string level,RoomLayout room,long revision)
        {
            if(Session.LocalActor?.level==level && ActiveLevelLighting!=null && ReferenceEquals(ActiveLevelLighting.Room,room))
                ActiveLevelLighting.Synchronize(room,Session.World,revision);
        }

        private void RefreshVisuals()
        {
            passengers.Reconcile(Session.World,Session.LocalOwner);
            if (Session.World==null || Session.LocalActor==null)
            {
                roomPresentation.SetContext(false,null);
                ActiveLevelLighting?.Release();
                foreach(var visual in visuals.Values) {visual.SetVisible(false);Destroy(visual.gameObject);}
                visuals.Clear();visualLayers.Synchronize(Session.World,Session.LocalActor?.level);travelPointVisuals.Clear();return;
            }
            string definition=WorldRooms.Definition(Session.World,Session.LocalActor.level);
            bool adoptEntry=entryRoom?.HasRoom==true;
            if(adoptEntry || shownLevel!=Session.LocalActor.level || shownRoom!=definition)
            {
                using var transition=TransitionTiming.Measure("runtime.room-transition",Session.LocalActor.level);
                shownLevel=Session.LocalActor.level;shownRoom=definition;
                var room=Geometry.Get(shownLevel);
                using(TransitionTiming.Measure("runtime.release-room",shownLevel))
                {roomPresentation.Bind(null);ActiveLevelLighting?.Release();}
                Destroy(roomObject);roomObject=adoptEntry?entryRoom.Take(Session.World,shownLevel,room):AuthoredRoomVisual.Create(shownLevel,room,Session.World);
                ActiveLevelLighting=roomObject.GetComponent<IRoomLighting>();
                roomPresentation.Bind(ActiveLevelLighting);
                if(ActiveLevelLighting is FactoryLevelView factory)
                {
                    factory.Synchronize(room,Session.World,Geometry.OcclusionRevision(shownLevel));
                    factory.Focus(Session.LocalActor.Position);
                    if(!roomObject.TryGetComponent<FactoryLightFocus>(out var focus))
                        throw new System.InvalidOperationException("Нет авторского наблюдателя камеры завода.");
                    focus.Bind(cameraView);
                }
                movement=CreateMovement(room);
                cameraFollower.Room=room;
                releaseRequested=true;
                if(!ReleasingUnusedAssets) StartCoroutine(ReleaseUnusedAssets());
            }
            travelPointVisuals.Bind(roomObject);
            var activeRoom=Geometry.Get(Session.LocalActor.level);
            if(ActiveLevelLighting==null) throw new System.InvalidOperationException("Нет света видимой локации: "+Session.LocalActor.level);
            ActiveLevelLighting.Synchronize(activeRoom,Session.World,Geometry.OcclusionRevision(Session.LocalActor.level));
            roomPresentation.SetEntryPending(false);
            roomPresentation.SetContext(true,activeRoom);
            if(!ReferenceEquals(movement.Room,activeRoom)) movement=CreateMovement(activeRoom);
            cameraFollower.Room=activeRoom;
            foreach (var actor in Session.World.actors)
            {
                if (!ActorPresence.IsPresent(actor) || actor.level!=Session.LocalActor.level) continue;
                if (!visuals.TryGetValue(actor.id,out var visual))
                {
                    visual=ActorVisualFactory.Create(actor); visuals.Add(actor.id,visual);
                    visualLayers.Attach(actor.id,visual.transform);
                }
                if(visual is PonyVisual pony) pony.ConfigureMagicSource(ponyMagic);
                visual.Bind(actor);
                visual.SetVisible(actor.level==Session.LocalActor?.level);
            }
            var removed=new List<string>();
            foreach (var pair in visuals)
            {
                var actor=Session.World.Find(pair.Key);
                if (!ActorPresence.IsPresent(actor) || actor.level!=Session.LocalActor.level)
                {pair.Value.SetVisible(false);Destroy(pair.Value.gameObject);removed.Add(pair.Key);}
            }
            foreach (var id in removed) visuals.Remove(id);
            visualLayers.Synchronize(Session.World,Session.LocalActor?.level);
        }
        private void BindEntryPresentation(IRoomLighting lighting,RoomLayout room,bool pending)
        {
            if(roomPresentation==null) return;
            roomPresentation.SetEntryPending(pending);
            roomPresentation.Bind(pending?lighting:ActiveLevelLighting);
            roomPresentation.SetContext(pending,room);
        }

        private MovementController CreateMovement(RoomLayout room)
        {
            var controller=new MovementController(room);
            controller.PositionRecovered+=recovered=>
            {
                if(recovered.id==Session.LocalActor?.id) cameraFollower?.ResetMotion();
            };
            return controller;
        }

        private void ApplyAudioPreferences()
        {
            var value=AudioPreferences.Current;
            Audio.Volume=Audio.BaseVolume*value.master*value.effects;
            Music.Volume=musicMixVolume*value.master*value.music;
        }
        // Geometry is prepared on session changes, before actor consumers bind.
        // Per-frame body samples then read only this prepared room and scalar state.
        private void RefreshBodyBindings()
        {
            try
            {
                var world=Session.World;
                if(bodyWorld!=world?.worldId) {ActorBodySupport.Clear();bodyWorld=world?.worldId;}
                if(world==null) return;
                var loaded=new HashSet<string>(Geometry.LoadedLevels,System.StringComparer.Ordinal);
                foreach(var actor in world.actors)
                    if(ActorPresence.IsPresent(actor) && loaded.Contains(actor.level)) ActorBodySupport.Bind(actor,Geometry.Get(actor.level));
                    else ActorBodySupport.Unbind(actor);
            }
            catch(System.Exception failure) {Session.ReportGameplayPreparationFailure(failure);}
        }
        private void RefreshBodyGeometry(string level,RoomLayout changed,long revision)
            =>ActorBodySupport.InvalidateRoom(changed);

        private void Update()
        {
            if(roomFailurePending) return;
            Session.PumpSaveWrites();
            Session.Admission.Step();Session.Travel.Step();Session.Lobby.Step();Session.Pause.Step();Session.Deposits.Step();Session.ActorHolds.Step();
            Session.LevelTime.Step(Time.deltaTime);
            Session.Mechanisms.Step();Platforms.Step(Time.deltaTime);
            var actor=Session.LocalActor;
            Pickup.Step();
            if (actor==null || !NetworkClient.isConnected)
            {
                if(Session.EntryPreparation.ClientPending && Application.isFocused && Keyboard.current?.escapeKey.wasPressedThisFrame==true)
                {if(NetworkServer.active) Session.StopHost();else Session.StopClient();}
                remoteMotion.Clear();InteractionFocus.Clear();return;
            }
            remoteMotion.Step(Session.World,Session.LocalOwner,Time.deltaTime,Session.Pause.IsPaused,Time.unscaledTimeAsDouble,true);
            foreach(var body in Session.World.actors)
                if(body.owner==Session.LocalOwner &&
                    LevelActivity.HasConnectedPlayer(Session.World,body.level) && !Session.Pause.IsPaused(body.level))
                {
                    if(ActorHoldRules.Active(body)) ActorHoldMotion.Step(body,Geometry.Get(body.level),Time.deltaTime);
                    else if(body!=actor && ActorSessionRules.IsPassive(body)) PassiveActorMotion.Step(body,Geometry.Get(body.level),Time.deltaTime,passiveMovementSettings,passengers.ForActor(body));
                }
            foreach(var animated in Session.World.actors)
            {
                if(animated.owner==Session.LocalOwner) animated.poseCorrectionRemaining=0;
                if(LevelActivity.HasConnectedPlayer(Session.World,animated.level) && !Session.Pause.IsPaused(animated.level) &&
                    !remoteMotion.ControlsPosePhase(animated)) ActorPose.Advance(animated,Time.deltaTime);
            }
            Session.Recoil.Step(Time.deltaTime);
            Grenades.Step(Time.deltaTime);
            if (Options.Smoke==null || Options.Smoke.StartsWith("actor-death") || Options.Smoke.StartsWith("legacy-doors") || Options.Smoke.StartsWith("cosmetics") || Options.Smoke.StartsWith("lighting-preparation") || Options.Smoke=="performance-audit" || Options.Smoke.StartsWith("warehouse-furniture") || Options.Smoke.StartsWith("warehouse-metal-doors") || Options.Smoke.StartsWith("warehouse-reload") || Options.Smoke.StartsWith("raid-entry") || (Options.Smoke.StartsWith("warehouse-seam") || Options.Smoke.StartsWith("continuation-seam")) || Options.Smoke.StartsWith("office-furniture") || Options.Smoke.StartsWith("office-opening") || Options.Smoke.StartsWith("office-recovery") || Options.Smoke.StartsWith("tool-stand") || Options.Smoke.StartsWith("office-cabinet") || Options.Smoke.StartsWith("ladder-contact") || Options.Smoke.StartsWith("mechanism") || Options.Smoke.StartsWith("crawler") || Options.Smoke.StartsWith("surface-audio") || Options.Smoke.StartsWith("contact-audio") || Options.Smoke.StartsWith("prop-body") || Options.Smoke.StartsWith("factory-room") || Options.Smoke.StartsWith("raid-reload") || Options.Smoke.StartsWith("travel-points") || Options.Smoke.StartsWith("door") || Options.Smoke.StartsWith("ladder-body") || Options.Smoke.StartsWith("weapon-transfer") || Options.Smoke.StartsWith("projectile-handoff") || Options.Smoke.StartsWith("training") || Options.Smoke.StartsWith("shot-spread") || Options.Smoke.StartsWith("weapon-reload") || Options.Smoke.StartsWith("held-prop-color") || Options.Smoke.StartsWith("obstacle") || Options.Smoke.StartsWith("journey") || Options.Smoke.StartsWith("session") || Options.Smoke.StartsWith("ammo") || Options.Smoke.StartsWith("revival") || Options.Smoke.StartsWith("grenade") || Options.Smoke.StartsWith("npc") || Options.Smoke.StartsWith("container") || Options.Smoke.StartsWith("receipt") || Options.Smoke.StartsWith("shortcuts") || Options.Smoke.StartsWith("direct-items") || Options.Smoke.StartsWith("slope-poses") || Options.Smoke.StartsWith("body") || Options.Smoke.StartsWith("extensions") || Options.Smoke.StartsWith("freeze") || Options.Smoke.StartsWith("telekinesis") || Options.Smoke.StartsWith("actor-hold") || Options.Smoke.StartsWith("melee") || Options.Smoke.StartsWith("rules") || Options.Smoke.StartsWith("pause"))
            {
                npcs.Step(Time.deltaTime);
                // Do not consume a probe/input source's one-shot actions until its room is
                // ready. Real Escape still passes through; physics/network continue below.
                var command=IsLevelPreparing?ReadCommand():CommandSource?.Invoke() ?? ReadCommand();
                if(!IsLevelPreparing && CommandSource!=null && Options.Smoke!=null) command=ProbeWeaponSelection.Resolve(actor,command);
                ApplyCommand(command,Time.deltaTime);
            }
            freeProps.Step(Time.deltaTime);
            Combat.Step(Time.deltaTime);
            if (poseCadence.TryAdvance(Time.unscaledTimeAsDouble))
            {
                foreach (var owned in Session.World.actors) if (owned.owner==Session.LocalOwner) Session.PublishPose(owned);
            }
            // Follow the resolved body that the visuals will draw in LateUpdate.
            // Input above used the camera from the last displayed frame.
            if(Session.LocalActor is {} renderedActor && NetworkClient.isConnected)
                cameraFollower.Step(renderedActor,Time.deltaTime);
        }

        private IEnumerator ReleaseUnusedAssets()
        {
            ReleasingUnusedAssets=true;
            do
            {
                releaseRequested=false;
                // Room objects destroy their owned meshes/materials at frame boundaries.
                yield return null;yield return null;
                // Measure the issuing call only: the asynchronous operation's wait must not
                // become a nested main-thread timing scope spanning multiple frames.
                AsyncOperation unloading;
                using(TransitionTiming.Measure("runtime.unload-unused-call",shownLevel))
                    unloading=Resources.UnloadUnusedAssets();
                yield return unloading;
            } while(releaseRequested);
            ReleasingUnusedAssets=false;
        }

        private PlayerCommand ReadCommand()
        {
            var command=new PlayerCommand();
            // Background simulation continues, but desktop devices drive only the focused instance.
            if(!Application.isFocused) return command;
            var keyboard=Keyboard.current; var mouse=Mouse.current;
            if(keyboard!=null)
            {
                command.movement=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),
                    (keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
                command.jump=keyboard.spaceKey.wasPressedThisFrame;
                command.jumpHeld=keyboard.spaceKey.isPressed;
                command.sprint=keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                command.weaponShortcut=ItemShortcutInput.Pressed(keyboard);command.shortcutWholeStack=command.sprint;command.throwWholeStack=command.sprint;
                command.inventory=keyboard.tabKey.wasPressedThisFrame;
                command.menu=keyboard.escapeKey.wasPressedThisFrame;
                command.reload=keyboard.rKey.wasPressedThisFrame;
                command.interact=keyboard.eKey.wasPressedThisFrame;command.revive=keyboard.eKey.isPressed;command.telekinesis=keyboard.qKey.isPressed;
                command.grenade=keyboard.gKey.wasPressedThisFrame;command.heal=keyboard.hKey.wasPressedThisFrame;
            }
            if(mouse!=null)
            {
                command.pointer=mouse.position.ReadValue();command.hasPointer=true;
                command.hasAim=true;command.aim=cameraView.ScreenToWorldPoint(mouse.position.ReadValue());
                command.fire=(Session.LocalActor?.weapon=="grenade" || HeldItemSelection.HasSelection(Session.LocalActor)?mouse.leftButton.wasPressedThisFrame:mouse.leftButton.isPressed) &&
                    !hud.BlocksPointerGameplay(command.pointer) &&
                    (UnityEngine.EventSystems.EventSystem.current==null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
            }
            return command;
        }

        private void EquipWeaponItem(string item)
        {
            var actor=Session.LocalActor;
            if(actor==null || actor.down) return;
            if(!WeaponInventory.Select(actor,item,out var reason)) {Session.ShowNotice(reason);return;}
            Combat.Weapon=actor.weapon;Session.PublishActor(actor);
        }
        private void ApplyCommand(PlayerCommand command,float dt)
        {
            var actor=Session.LocalActor;
            if(IsLevelPreparing) command=new PlayerCommand {menu=command.menu};
            if(command.hasPointer && hud?.BlocksPointerGameplay(command.pointer)==true) command.fire=false;
            if(command.hasAim && hud?.BlocksGameplayInput!=true && (!command.hasPointer ||
                hud?.BlocksPointerGameplay(command.pointer)!=true && (UnityEngine.EventSystems.EventSystem.current==null ||
                !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))) lastGameplayAim=command.aim;
            if(command.inventory) hud?.ToggleInventory();
            if(command.menu) hud?.ToggleMenu();
            InteractionFocus.Step(Session.World,actor,Session.Rules,command.hasAim?command.aim:(Vector2?)null,
                IsLevelPreparing || Session.Travel.Pending || hud?.BlocksGameplayInput==true || command.hasPointer &&
                    (hud?.BlocksPointerGameplay(command.pointer)==true || UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject()==true),
                Session.Travel.Points(actor.level),MechanismInteraction.Points);
            MechanismInteraction.Step(InteractionFocus.Target,command.interact,command.revive,
                !IsLevelPreparing && !Session.Travel.Pending && hud?.BlocksGameplayInput!=true && hud?.ContainerOpen!=true,dt);
            if(command.interact && !Session.Travel.Pending)
            {
                if(hud.WorldMap.IsOpen) hud.WorldMap.Close();
                else if(hud.ContainerOpen) hud.CloseContainer();
                else if(!hud.BlocksGameplayInput)
                {
                    var target=InteractionFocus.Target;
                    if(target.kind==InteractionKind.Container) hud.OpenContainer(target.id);
                    else if(target.kind==InteractionKind.Pickup) Pickup.Request(target);
                    else if(target.kind==InteractionKind.Door)
                    {
                        var door=Session.World.doors.Find(d=>d.id==target.id);
                        Session.Doors.Request(door.id,!door.open);
                    }
                    else if(target.kind==InteractionKind.Map) hud.WorldMap.Open(Session.Travel.Find(target.id,actor.level));
                    else if(target.kind==InteractionKind.Exit) Session.Travel.RequestVia(target.id);
                }
            }
            if(IsLevelPreparing || hud?.BlocksGameplayInput==true) command=default;
            hud?.CloseContainerForSprint(command.sprint,command.movement);
            if(Session.Pause.IsPaused(actor.level)) {Revival.Step(InteractionFocus.Target,false,0);return;}
            if(Session.Travel.Pending) command=default;
            if(!ActorHoldRules.Active(actor)) movement.Step(actor,command.movement,command.jump,dt,command.sprint,command.jumpHeld);
            long selectionBefore=actor.weaponSelectionRevision;
            if(command.weaponItem!=null) EquipWeaponItem(command.weaponItem);
            if(command.weaponShortcut!=null)
                ItemActions.Activate(command.weaponShortcut,command.hasAim?command.aim:lastGameplayAim,command.shortcutWholeStack);
            if(command.weapon!=null)
            {
                if(actor.weaponInventoryVersion==0) Combat.Weapon=command.weapon;
                else Debug.LogWarning($"Отклонён выбор определения вместо экземпляра: actor={actor.id}, weapon={command.weapon}, inventoryVersion={actor.weaponInventoryVersion}. Используйте weaponItem или weaponShortcut.",this);
            }
            if(actor.weaponInventoryVersion==1) Combat.Weapon=WeaponInventory.Selected(actor)?.definition;
            actor.weapon=Combat.Weapon;
            weaponElevation.Step(actor,Geometry.Get(actor.level),command.movement.y>.1f,dt);
            if(command.hasAim)
            {
                if(HeldItemSelection.HasSelection(actor)) HeldItemPose.Aim(actor,command.aim,Geometry.Get(actor.level));
                else if(!Combat.IsMeleeActive) WeaponPose.Aim(actor,command.aim,Geometry.Get(actor.level));
            }
            if(command.reload && !HeldItemSelection.HasSelection(actor) && !Combat.IsMeleeActive) Combat.Reload.Start(actor,Combat.Weapon);
            Revival.Step(InteractionFocus.Target,command.revive,dt);
            Telekinesis.Step(command.telekinesis,command.aim,dt);
            if(command.grenade && !actor.down && command.hasAim) Grenades.TryThrow(command.aim);
            if(command.heal && !actor.down) Session.RequestSelfHeal();
            if(command.fire && command.hasAim && (actor.weaponSelectionRevision==selectionBefore ||
                !HeldItemSelection.HasSelection(actor) && actor.weapon!="grenade"))
            {
                if(HeldItemSelection.HasSelection(actor)) ItemActions.TryThrow(command.aim,command.throwWholeStack);
                else if(actor.weapon=="grenade") Grenades.TryThrow(command.aim);
                else Combat.Fire(actor,command.aim);
            }
        }

        private void OnApplicationQuit()
        {
            Session?.SaveLocalCharacter();
            Session?.FlushSaveWrites();
        }
        private void RoomPreparationFailed(IRoomLighting source,System.Exception error)
        {
            if(roomFailurePending || source!=ActiveLevelLighting && source!=entryRoom?.Light) return;
            automaticStartCancelled=true;roomFailurePending=true;StartCoroutine(EndFailedSession(source,error,"Не удалось загрузить локацию. Сессия остановлена; подробности — в журнале ошибки."));
        }
        private void GameplayPreparationFailed(System.Exception error)
        {
            if(roomFailurePending) return;
            automaticStartCancelled=true;roomFailurePending=true;
            StartCoroutine(EndFailedSession(ActiveLevelLighting,error,"Ошибка игрового правила. Сессия остановлена; подробности — в журнале ошибки."));
        }
        private IEnumerator EndFailedSession(IRoomLighting source,System.Exception error,string notice)
        {
            // Let the world/geometry notification that initiated this binding finish before
            // Mirror releases it. The next frame cannot run a partially prepared simulation.
            yield return null;
            Debug.LogError($"Сессия остановлена: room={source?.Room?.id}, world={Session.World?.worldId}, reason={error}.",this);
            if(NetworkServer.active) Session.StopHost();else if(NetworkClient.active) Session.StopClient();
            Debug.Log($"Завершение после ошибки игрового правила: server={NetworkServer.active}, client={NetworkClient.active}, connected={NetworkClient.isConnected}.",this);
            roomPresentation.SetContext(false,null);source?.Release();
            Session.ShowNotice(notice);
            SessionLoadFailure.Show(error,notice);roomFailurePending=false;
        }
        private GameObject CreateOwnedObject(string name)
        {
            var value=new GameObject(name);ownedObjects.Add(value);return value;
        }
        private void DisposeRuntime()
        {
            if(disposed) return;
            automaticStartCancelled=true;enabled=false;StopAllCoroutines();
            if(Session!=null) {Session.Changed-=RefreshVisualOrders;Session.Changed-=RefreshVisuals;Session.Changed-=RefreshBodyBindings;Session.GameplayPreparationFailed-=GameplayPreparationFailed;}
            if(Geometry!=null) {Geometry.OcclusionChanged-=RefreshRoomOcclusion;Geometry.OcclusionChanged-=RefreshBodyGeometry;}
            if(AudioPreferences!=null) AudioPreferences.Changed-=ApplyAudioPreferences;
            // Keep content and presentation consumers alive until stop callbacks have
            // saved the character/world and published World=null to their views.
            if(Session!=null)
            {
                if(NetworkManager.singleton==Session)
                {
                    if(NetworkServer.active) Session.StopHost();else if(NetworkClient.active) Session.StopClient();
                    if(NetworkServer.active || NetworkClient.active)
                        throw new System.InvalidOperationException("Runtime нельзя уничтожить до полного отключения транспорта. Каталог комнат удерживается до завершения consumers.");
                }
                Session.FlushSaveWrites();
            }
            entryRoom?.Dispose();entryRoom=null;EntryPresentationPrepared=null;
            roomPresentation?.Dispose();ActiveLevelLighting?.Release();
            Pickup?.Dispose();MechanismInteraction?.Dispose();
            if(Platforms!=null) {Platforms.TopologyChanged-=ActorBodySupport.InvalidateRoom;Platforms.Dispose();}
            Geometry?.Dispose();
            ActorBodySupport.Clear();bodyWorld=null;
            if(roomObject!=null) {roomObject.SetActive(false);Destroy(roomObject);roomObject=null;}
            foreach(var value in ownedObjects) if(value!=null) {value.SetActive(false);Destroy(value);}
            ownedObjects.Clear();
            if(networkObject!=null) {networkObject.SetActive(false);Destroy(networkObject);networkObject=null;}
            roomCatalogLease?.Dispose();roomCatalogLease=null;disposed=true;
        }
        private void OnDestroy()
        {
            try {DisposeRuntime();}
            catch(System.Exception failure) {Debug.LogError("Ошибка завершения runtime: "+failure,this);}
        }
    }
}
