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

namespace Remains.Runtime
{
    public sealed class PrototypeRuntime : MonoBehaviour
    {
        public CoopSession Session { get; private set; }
        public CombatController Combat { get; private set; }
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
        public ExplosionEffects Explosions {get;private set;}
        public AudioPreferences AudioPreferences {get;private set;}
        public DoorVisuals DoorVisuals {get;private set;}
        private float musicMixVolume;
        public DamageFeedback DamageFeedback {get;private set;}
        public DamageNumbers DamageNumbers {get;private set;}
        public System.Func<PlayerCommand> CommandSource { get; set; }
        public PrototypeHud Hud => hud;
        private MovementController movement;
        private MovementSettings passiveMovementSettings;
        private NpcController npcs;
        private readonly Dictionary<string,ActorVisual> visuals = new();
        private readonly RemoteActorMotion remoteMotion=new();
        internal ActorVisual FindActorVisual(string id)=>visuals.TryGetValue(id,out var visual)?visual:null;
        private ActorVisualLayers visualLayers;
        private PrototypeHud hud;
        private Camera cameraView;
        private readonly ReplicationCadence poseCadence=new(.05);
        private readonly WeaponElevation weaponElevation=new();
        private GameObject roomObject;
        private string shownLevel="hub";
        private string shownRoom="base:hub";
        private CameraFollower cameraFollower;
        private bool releaseRequested;
        public bool ReleasingUnusedAssets {get;private set;}

        private void Awake()
        {
            try { Initialize(); }
            catch(System.Exception failure) { enabled=false;StartupFailure.Show(failure); }
        }

        private void Initialize()
        {
            Options=LaunchOptions.Parse();
            Anatomy.Load();
            passiveMovementSettings=MovementSettings.Load();
            WeaponCatalog.Load();
            Application.runInBackground=true;
            Application.targetFrameRate=Options.Smoke==null?60:Options.ProbeFrameRate;
            var networkObject=new GameObject("Сетевая сессия");
            networkObject.SetActive(false);
            var transport=TransportSetup.Create(networkObject,Options.Port,Options.NetworkStress);
            Session=networkObject.AddComponent<CoopSession>(); Session.transport=transport;
            // Mirror configures headless frame rate during StartClient/StartHost.
            // Apply the requested probe limit after that, including reconnections.
            if(Options.Smoke!=null) Session.ClientReady+=()=>Application.targetFrameRate=Options.ProbeFrameRate;
            Session.ConfigureRules(WeaponCatalog.Rules);
            Session.UseLobby=Options.Smoke==null || Options.Smoke.StartsWith("lobby") || Options.Smoke.StartsWith("duplicate-lobby") || Options.Smoke.StartsWith("protocol") || Options.Smoke.StartsWith("grenade-resume-"); Session.autoCreatePlayer=false; Session.maxConnections=Options.Capacity;
            Session.CharacterId=Options.Character;
            Session.DebugTravelAllowed=Options.Smoke!=null && !Options.Smoke.StartsWith("travel-points");
            Session.networkAddress=Options.Address;
            Session.Saves=new SaveStore(Options.SaveDirectory ?? Path.Combine(Application.persistentDataPath,"Profiles",Options.Character));
            if(Options.Smoke!=null && Options.Role=="host" && !string.IsNullOrEmpty(Options.Report))
                gameObject.AddComponent<RecoveryTimingProbe>().Initialize(Session,Options.Report);
            // NetworkManager.Awake must see the final transport, including a test wrapper.
            networkObject.SetActive(true);
            DontDestroyOnLoad(gameObject);
            Geometry=new SimulationGeometry(Session);
            Combat=new CombatController(Session,Geometry);
            DamageFeedback=new DamageFeedback(Session);
            DamageNumbers=new GameObject("Числа урона").AddComponent<DamageNumbers>();DamageNumbers.Initialize(Session,DamageFeedback);
            if(Options.Diagnostics || Options.Smoke!=null)
                gameObject.AddComponent<SessionTimingDiagnostics>().Initialize(this);
            Combat.DamageApplied+=DamageFeedback.Publish;
            Revival=new RevivalController(Session);
            Interactions=new InteractionAdapter(Session);
            Grenades=new GrenadeController(Session,Interactions,Combat,Geometry);
            Telekinesis=new TelekinesisController(Session,Interactions,Geometry);
            visualLayers=new ActorVisualLayers(transform);
            new GameObject("Визуальные взаимодействия").AddComponent<InteractionVisuals>().Initialize(Session,Interactions,Grenades,Geometry,visualLayers);
            var room=RoomLayout.Load(shownLevel);
            movement=new MovementController(room);
            npcs=new NpcController(Session,Geometry);
            roomObject=AuthoredRoomVisual.Create(shownLevel,room);
            cameraView=CameraFollower.RequireCamera(Camera.main);
            cameraFollower=new CameraFollower(cameraView,room);
            Audio=new GameObject("Звуки игры").AddComponent<GameplayAudio>();Audio.Initialize(Session,Combat.Reload,cameraView);
            DoorVisuals=new GameObject("Двери мира").AddComponent<DoorVisuals>();DoorVisuals.Initialize(Session,Audio);
            GrenadeAudio=new GameObject("Звуки отскока гранаты").AddComponent<GrenadeImpactAudio>();GrenadeAudio.Initialize(Session,Grenades,Audio);
            MovementAudio=new GameObject("Звуки движения").AddComponent<MovementAudio>();MovementAudio.Initialize(Session,Geometry,Audio);
            PropAudio=new GameObject("Звуки физических объектов").AddComponent<PropContactAudio>();PropAudio.Initialize(Session,Geometry,Audio);
            ImpactAudio=new GameObject("Звуки попаданий").AddComponent<ProjectileAudio>();ImpactAudio.Initialize(Combat.Projectiles,Audio);
            MeleeAudio=new GameObject("Звуки контактов ближнего оружия").AddComponent<MeleeImpactAudio>();MeleeAudio.Initialize(Combat,Audio);
            new GameObject("Изображения снарядов").AddComponent<ProjectileVisuals>().Initialize(Session,Combat.Projectiles);
            new GameObject("Попадания лазера").AddComponent<LaserImpactEffects>().Initialize(Session,Combat.Projectiles);
            Explosions=new GameObject("Эффекты взрывов").AddComponent<ExplosionEffects>();Explosions.Initialize(Session);
            Music=new GameObject("Музыка локации").AddComponent<AreaMusic>();Music.Initialize(Session);
            if(Application.isBatchMode && Options.Smoke!=null && string.IsNullOrEmpty(Options.SaveDirectory))
                throw new System.InvalidOperationException("Фоновая проверка требует отдельного каталога --save-dir для настроек.");
            AudioPreferences=new AudioPreferences(Path.Combine(Options.SaveDirectory??Application.persistentDataPath,"Settings","audio.json"));
            musicMixVolume=Music.Volume;AudioPreferences.Changed+=ApplyAudioPreferences;ApplyAudioPreferences();
            PonyVisual.Prepare();
            Session.Changed+=RefreshVisuals;
            if (!Application.isBatchMode || Options.Smoke?.StartsWith("travel-points")==true || Options.Smoke?.StartsWith("door")==true || Options.Smoke?.Contains("-frames-")==true || Options.Smoke?.Contains("-mismatch-")==true || (Options.Smoke?.StartsWith("pause")==true || Options.Smoke?.StartsWith("body")==true) || Options.Smoke?.Contains("-ffui-")==true || Options.Smoke?.StartsWith("journey")==true || Options.Smoke?.StartsWith("session")==true || Options.Smoke?.StartsWith("container")==true || Options.Smoke=="history" || Options.Smoke=="world-management" || Options.Smoke?.StartsWith("lobby")==true || Options.Smoke?.StartsWith("duplicate")==true)
            {
                hud=new GameObject("Интерфейс").AddComponent<PrototypeHud>(); hud.Initialize(Session,AudioPreferences);
                hud.ReloadRemaining=()=>Combat.Reload.Active?Combat.Reload.Remaining:(float?)null;
                hud.RevivalProgress=()=>Revival.Progress;hud.RevivalPending=()=>Revival.Pending;
                hud.EquipWeaponItem=EquipWeaponItem;
                hud.EquipWeapon=weapon=>{if(Session.LocalActor is {down:false}) Combat.Weapon=weapon;};
                hud.SelectCharacter=name=>
                {
                    if(!System.Text.RegularExpressions.Regex.IsMatch(name??"",@"^[\p{L}\p{N}_ -]{1,32}$"))
                    {hud.ShowMessage("Имя персонажа: от 1 до 32 букв, цифр, пробелов, дефисов или подчёркиваний.");return false;}
                    Session.CharacterId=name;
                    Session.Saves=new SaveStore(Options.SaveDirectory??Path.Combine(Application.persistentDataPath,"Profiles",name));
                    return true;
                };
                hud.HostRequested+=()=>Session.StartHost();
                hud.JoinRequested+=address=> { Session.networkAddress=address; Session.StartClient(); };
            }
            if(Options.Smoke?.Contains("-attacker-")==true) Session.ServerReady+=()=>Session.SetFriendlyFireDecision(FriendlyFireDecision.Attacker);
            if (Options.Smoke != null)
            {
                if(Options.Smoke.StartsWith("audio-settings")) gameObject.AddComponent<AudioSettingsProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("appearance")) gameObject.AddComponent<NetworkAppearanceProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("projectile-handoff")) gameObject.AddComponent<NetworkProjectileHandoffProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("factory-room")) gameObject.AddComponent<NetworkFactoryRoomProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("travel-points")) gameObject.AddComponent<NetworkTravelPointProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("door")) gameObject.AddComponent<NetworkDoorProbe>().Initialize(this);
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
                else if(Options.Smoke.StartsWith("container")) gameObject.AddComponent<NetworkContainerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc")) gameObject.AddComponent<NetworkNpcProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("freeze")) gameObject.AddComponent<NetworkFreezeProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("telekinesis")) gameObject.AddComponent<NetworkTelekinesisProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("actor-hold")) gameObject.AddComponent<NetworkActorHoldProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("melee")) gameObject.AddComponent<NetworkMeleeProbe>().Initialize(this);
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
            }
        }

        private void Start()
        {
            LegacyPrototypeFixture.Prepare(this);
            if (Options.Role=="host") Session.StartHost();
            else if (Options.Role=="client") Session.StartClient();
        }

        private void RefreshVisuals()
        {
            if (Session.World==null || Session.LocalActor==null)
            {
                foreach(var visual in visuals.Values) {visual.SetVisible(false);Destroy(visual.gameObject);}
                visuals.Clear();visualLayers.Synchronize(System.Array.Empty<string>());return;
            }
            string definition=WorldRooms.Definition(Session.World,Session.LocalActor.level);
            if(shownLevel!=Session.LocalActor.level || shownRoom!=definition)
            {
                shownLevel=Session.LocalActor.level;shownRoom=definition;
                var room=Geometry.Get(shownLevel);
                Destroy(roomObject);roomObject=AuthoredRoomVisual.Create(shownLevel,room,Session.World);
                movement=new MovementController(room);
                cameraFollower.Room=room;
                releaseRequested=true;
                if(!ReleasingUnusedAssets) StartCoroutine(ReleaseUnusedAssets());
            }
            var activeRoom=Geometry.Get(Session.LocalActor.level);
            if(!ReferenceEquals(movement.Room,activeRoom)) movement=new MovementController(activeRoom);
            cameraFollower.Room=activeRoom;
            foreach (var actor in Session.World.actors)
            {
                if (!ActorPresence.IsPresent(actor) || actor.level!=Session.LocalActor.level) continue;
                if (!visuals.TryGetValue(actor.id,out var visual))
                {
                    visual=ActorVisualFactory.Create(actor); visuals.Add(actor.id,visual);
                    visualLayers.Attach(actor.id,visual.transform);
                }
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
            visualLayers.Synchronize(visuals.Keys);
        }

        private void ApplyAudioPreferences()
        {
            var value=AudioPreferences.Current;
            Audio.Volume=Audio.BaseVolume*value.master*value.effects;
            Music.Volume=musicMixVolume*value.master*value.music;
        }
        private void Update()
        {
            Session.Admission.Step();Session.Travel.Step();Session.Lobby.Step();Session.Pause.Step();Session.Deposits.Step();Session.ActorHolds.Step();
            var actor=Session.LocalActor;
            if (actor==null || !NetworkClient.isConnected) {remoteMotion.Clear();return;}
            remoteMotion.Step(Session.World,Session.LocalOwner,Time.deltaTime,Session.Pause.IsPaused,Time.unscaledTimeAsDouble);
            foreach(var body in Session.World.actors)
                if(body.owner==Session.LocalOwner &&
                    LevelActivity.HasConnectedPlayer(Session.World,body.level) && !Session.Pause.IsPaused(body.level))
                {
                    if(ActorHoldRules.Active(body)) ActorHoldMotion.Step(body,Geometry.Get(body.level),Time.deltaTime);
                    else if(body!=actor && body.down) PassiveActorMotion.Step(body,Geometry.Get(body.level),Time.deltaTime,passiveMovementSettings);
                }
            cameraFollower.Step(actor,Time.deltaTime);
            foreach(var animated in Session.World.actors)
            {
                if(animated.owner==Session.LocalOwner) animated.poseCorrectionRemaining=0;
                if(LevelActivity.HasConnectedPlayer(Session.World,animated.level) && !Session.Pause.IsPaused(animated.level)) ActorPose.Advance(animated,Time.deltaTime);
            }
            Grenades.Step(Time.deltaTime);
            if (Options.Smoke==null || Options.Smoke.StartsWith("factory-room") || Options.Smoke.StartsWith("travel-points") || Options.Smoke.StartsWith("door") || Options.Smoke.StartsWith("ladder-body") || Options.Smoke.StartsWith("weapon-transfer") || Options.Smoke.StartsWith("projectile-handoff") || Options.Smoke.StartsWith("training") || Options.Smoke.StartsWith("obstacle") || Options.Smoke.StartsWith("journey") || Options.Smoke.StartsWith("session") || Options.Smoke.StartsWith("ammo") || Options.Smoke.StartsWith("revival") || Options.Smoke.StartsWith("grenade") || Options.Smoke.StartsWith("npc") || Options.Smoke.StartsWith("container") || Options.Smoke.StartsWith("receipt") || Options.Smoke.StartsWith("body") || Options.Smoke.StartsWith("extensions") || Options.Smoke.StartsWith("freeze") || Options.Smoke.StartsWith("telekinesis") || Options.Smoke.StartsWith("actor-hold") || Options.Smoke.StartsWith("melee") || Options.Smoke.StartsWith("rules") || Options.Smoke.StartsWith("pause"))
            {
                npcs.Step(Time.deltaTime);
                var command=CommandSource?.Invoke() ?? ReadCommand();
                if(CommandSource!=null && Options.Smoke!=null) command=ProbeWeaponSelection.Resolve(actor,command);
                ApplyCommand(command,Time.deltaTime);
            }
            Combat.Step(Time.deltaTime);
            if (poseCadence.TryAdvance(Time.unscaledTimeAsDouble))
            {
                foreach (var owned in Session.World.actors) if (owned.owner==Session.LocalOwner) Session.PublishPose(owned);
            }
        }

        private IEnumerator ReleaseUnusedAssets()
        {
            ReleasingUnusedAssets=true;
            do
            {
                releaseRequested=false;
                // Room objects destroy their owned meshes/materials at frame boundaries.
                yield return null;yield return null;
                yield return Resources.UnloadUnusedAssets();
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
                command.weapon=keyboard.digit1Key.wasPressedThisFrame?"spear":keyboard.digit2Key.wasPressedThisFrame?"crowbar":
                    keyboard.digit3Key.wasPressedThisFrame?"pistol":keyboard.digit4Key.wasPressedThisFrame?"energy":null;
                if(Session.LocalActor?.weaponInventoryVersion==1 && command.weapon!=null)
                {
                    command.weaponShortcut=keyboard.digit1Key.wasPressedThisFrame?"1":keyboard.digit2Key.wasPressedThisFrame?"2":keyboard.digit3Key.wasPressedThisFrame?"3":"4";
                    command.weapon=null;
                }
                command.inventory=keyboard.tabKey.wasPressedThisFrame;
                command.menu=keyboard.escapeKey.wasPressedThisFrame;
                command.reload=keyboard.rKey.wasPressedThisFrame;
                command.interact=keyboard.eKey.wasPressedThisFrame;command.revive=keyboard.eKey.isPressed;command.telekinesis=keyboard.qKey.isPressed;
                command.grenade=keyboard.gKey.wasPressedThisFrame;command.heal=keyboard.hKey.wasPressedThisFrame;
            }
            if(mouse!=null)
            {
                command.hasAim=true;command.aim=cameraView.ScreenToWorldPoint(mouse.position.ReadValue());
                command.fire=(Session.LocalActor?.weapon=="grenade"?mouse.leftButton.wasPressedThisFrame:mouse.leftButton.isPressed) &&
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
            if(command.inventory) hud?.ToggleInventory();
            if(command.menu) hud?.ToggleMenu();
            if(command.interact && !Session.Travel.Pending)
            {
                if(hud.WorldMap.IsOpen) hud.WorldMap.Close();
                else if(hud.ContainerOpen) hud.CloseContainer();
                else if(!hud.BlocksGameplayInput)
                {
                    var target=InteractionSelection.Select(Session.World,actor,Session.Rules,Session.Travel.Points(actor.level));
                    if(target.kind==InteractionKind.Container) hud.OpenContainer(target.id);
                    else if(target.kind==InteractionKind.Door)
                    {
                        var door=Session.World.doors.Find(d=>d.id==target.id);
                        Session.Doors.Request(door.id,!door.open);
                    }
                    else if(target.kind==InteractionKind.Map) hud.WorldMap.Open(Session.Travel.Find(target.id,actor.level));
                    else if(target.kind==InteractionKind.Exit) Session.Travel.RequestVia(target.id);
                }
            }
            if(hud?.BlocksGameplayInput==true) command=default;
            if(Session.Pause.IsPaused(actor.level)) {Revival.Step(false,0);return;}
            if(Session.Travel.Pending) command=default;
            if(!ActorHoldRules.Active(actor)) movement.Step(actor,command.movement,command.jump,dt,command.sprint,command.jumpHeld);
            if(command.weaponItem!=null) EquipWeaponItem(command.weaponItem);
            if(command.weaponShortcut!=null)
            {
                var item=WeaponInventory.Shortcut(actor,command.weaponShortcut);
                if(item!=null) EquipWeaponItem(item.id);
                else Session.ShowNotice("Быстрый слот пуст.");
            }
            if(command.weapon!=null)
            {
                if(actor.weaponInventoryVersion==0) Combat.Weapon=command.weapon;
                else Debug.LogWarning($"Отклонён выбор определения вместо экземпляра: actor={actor.id}, weapon={command.weapon}, inventoryVersion={actor.weaponInventoryVersion}. Используйте weaponItem или weaponShortcut.",this);
            }
            if(actor.weaponInventoryVersion==1) Combat.Weapon=WeaponInventory.Selected(actor)?.definition;
            actor.weapon=Combat.Weapon;
            weaponElevation.Step(actor,Geometry.Get(actor.level),command.movement.y>.1f,dt);
            if(command.hasAim && !Combat.IsMeleeActive) WeaponPose.Aim(actor,command.aim,Geometry.Get(actor.level));
            if(command.reload && !Combat.IsMeleeActive) Combat.Reload.Start(actor,Combat.Weapon);
            Revival.Step(command.revive,dt);
            Telekinesis.Step(command.telekinesis,command.aim,dt);
            if(command.grenade && !actor.down && command.hasAim) Grenades.TryThrow(command.aim);
            if(command.heal && !actor.down) Session.RequestSelfHeal();
            if(command.fire && command.hasAim)
            {
                if(actor.weapon=="grenade") Grenades.TryThrow(command.aim);
                else Combat.Fire(actor,command.aim);
            }
        }

        private void OnApplicationQuit()
        {
            Session?.SaveLocalCharacter();
        }
    }
}
