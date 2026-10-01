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
        public System.Func<PlayerCommand> CommandSource { get; set; }
        public PrototypeHud Hud => hud;
        private MovementController movement;
        private NpcController npcs;
        private readonly Dictionary<string,PonyVisual> visuals = new();
        private ActorVisualLayers visualLayers;
        private PrototypeHud hud;
        private Camera cameraView;
        private float nextPose;
        private uint poseSequence;
        private GameObject roomObject;
        private string shownLevel="hub";
        private CameraFollower cameraFollower;
        private bool releaseRequested;
        public bool ReleasingUnusedAssets {get;private set;}

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<PrototypeRuntime>() == null) new GameObject("Remains Runtime").AddComponent<PrototypeRuntime>();
        }

        private void Awake()
        {
            try { Initialize(); }
            catch(System.Exception failure) { enabled=false;StartupFailure.Show(failure); }
        }

        private void Initialize()
        {
            Options=LaunchOptions.Parse();
            Anatomy.Load();
            WeaponCatalog.Load();
            Application.runInBackground=true;
            Application.targetFrameRate=60;
            var networkObject=new GameObject("Сетевая сессия");
            networkObject.SetActive(false);
            var transport=TransportSetup.Create(networkObject,Options.Port,Options.NetworkStress);
            Session=networkObject.AddComponent<CoopSession>(); Session.transport=transport;
            Session.ConfigureRules(WeaponCatalog.Rules);
            Session.UseLobby=Options.Smoke==null || Options.Smoke.StartsWith("lobby") || Options.Smoke.StartsWith("protocol") || Options.Smoke.StartsWith("grenade-resume-"); Session.autoCreatePlayer=false; Session.maxConnections=Options.Capacity;
            Session.CharacterId=Options.Character;
            Session.networkAddress=Options.Address;
            Session.Saves=new SaveStore(Options.SaveDirectory ?? Path.Combine(Application.persistentDataPath,"Profiles",Options.Character));
            if(Options.Smoke!=null && Options.Role=="host" && !string.IsNullOrEmpty(Options.Report))
                gameObject.AddComponent<RecoveryTimingProbe>().Initialize(Session,Options.Report);
            // NetworkManager.Awake must see the final transport, including a test wrapper.
            networkObject.SetActive(true);
            DontDestroyOnLoad(gameObject);
            Geometry=new SimulationGeometry(Session);
            Combat=new CombatController(Session,Geometry);
            Revival=new RevivalController(Session);
            Interactions=new InteractionAdapter(Session);
            Grenades=new GrenadeController(Session,Interactions,Combat,Geometry);
            Telekinesis=new TelekinesisController(Session,Interactions,Geometry);
            visualLayers=new ActorVisualLayers(transform);
            new GameObject("Визуальные взаимодействия").AddComponent<InteractionVisuals>().Initialize(Session,Interactions,Grenades,Geometry,visualLayers);
            var room=RoomLayout.Load(shownLevel);
            movement=new MovementController(room);
            npcs=new NpcController(Session,Geometry);
            roomObject=RoomVisual.Build(room);
            cameraView=Camera.main;
            if (cameraView == null) cameraView=new GameObject("Камера").AddComponent<Camera>();
            cameraView.orthographic=true; cameraView.orthographicSize=5;
            cameraView.transform.position=new Vector3(0,3,-10); cameraView.backgroundColor=new Color(0.08f,0.09f,0.1f);
            cameraFollower=new CameraFollower(cameraView,room);
            Audio=new GameObject("Звуки игры").AddComponent<GameplayAudio>();Audio.Initialize(Session,Combat.Reload,cameraView);
            PonyVisual.Prepare();
            Session.Changed+=RefreshVisuals;
            if (!Application.isBatchMode || Options.Smoke?.Contains("-frames-")==true || Options.Smoke?.Contains("-mismatch-")==true || (Options.Smoke?.StartsWith("pause")==true || Options.Smoke?.StartsWith("body")==true) || Options.Smoke?.Contains("-ffui-")==true || Options.Smoke?.StartsWith("journey")==true || Options.Smoke?.StartsWith("session")==true || Options.Smoke?.StartsWith("container")==true || Options.Smoke=="history" || Options.Smoke?.StartsWith("lobby")==true)
            {
                hud=new GameObject("Интерфейс").AddComponent<PrototypeHud>(); hud.Initialize(Session);
                hud.RevivalProgress=()=>Revival.Progress;hud.RevivalPending=()=>Revival.Pending;
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
                if(Options.Smoke.StartsWith("obstacle")) gameObject.AddComponent<NetworkObstacleProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("journey")) gameObject.AddComponent<NetworkJourneyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("lobby")) gameObject.AddComponent<NetworkLobbyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pauserestart")) gameObject.AddComponent<NetworkPauseRestartProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pausegroup")) gameObject.AddComponent<NetworkPauseGroupProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("pause")) gameObject.AddComponent<NetworkPauseProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("rules") || Options.Smoke.StartsWith("pause")) gameObject.AddComponent<NetworkRulesProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("extensions")) gameObject.AddComponent<NetworkExtensionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("protocol")) gameObject.AddComponent<NetworkProtocolProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("bodyrevival")) gameObject.AddComponent<NetworkBodyRevivalProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("body")) gameObject.AddComponent<NetworkBodyInventoryProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("receipt")) gameObject.AddComponent<NetworkReceiptLossProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("container")) gameObject.AddComponent<NetworkContainerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc")) gameObject.AddComponent<NetworkNpcProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("freeze")) gameObject.AddComponent<NetworkFreezeProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("telekinesis")) gameObject.AddComponent<NetworkTelekinesisProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("melee")) gameObject.AddComponent<NetworkMeleeProbe>().Initialize(this);
                else if(Options.Smoke=="history") gameObject.AddComponent<WorldSelectionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("session")) gameObject.AddComponent<NetworkSessionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("grenade")) gameObject.AddComponent<NetworkGrenadeProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("revival")) gameObject.AddComponent<NetworkRevivalProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("ammo")) gameObject.AddComponent<NetworkAmmunitionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("movement")) gameObject.AddComponent<NetworkMovementProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("contracts")) gameObject.AddComponent<NetworkContracts>().Initialize(this);
                else gameObject.AddComponent<NetworkSmoke>().Initialize(this);
            }
        }

        private void Start()
        {
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
            if(Session.LocalActor!=null && shownLevel!=Session.LocalActor.level)
            {
                shownLevel=Session.LocalActor.level;
                var room=Geometry.Get(shownLevel);
                Destroy(roomObject);roomObject=RoomVisual.Build(room);
                movement=new MovementController(room);
                cameraFollower.Room=room;
                releaseRequested=true;
                if(!ReleasingUnusedAssets) StartCoroutine(ReleaseUnusedAssets());
            }
            foreach (var actor in Session.World.actors)
            {
                if (!ActorPresence.IsPresent(actor) || actor.level!=Session.LocalActor.level) continue;
                if (!visuals.TryGetValue(actor.id,out var visual))
                {
                    visual=new GameObject(actor.id).AddComponent<PonyVisual>(); visuals.Add(actor.id,visual);
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

        private void Update()
        {
            Session.Admission.Step();Session.Travel.Step();Session.Lobby.Step();Session.Pause.Step();Session.Deposits.Step();
            var actor=Session.LocalActor;
            if (actor==null || !NetworkClient.isConnected) return;
            cameraFollower.Step(actor,Time.deltaTime);
            foreach(var animated in Session.World.actors)
                if(LevelActivity.HasConnectedPlayer(Session.World,animated.level) && !Session.Pause.IsPaused(animated.level)) ActorPose.Advance(animated,Time.deltaTime);
            Grenades.Step(Time.deltaTime);
            if (Options.Smoke==null || Options.Smoke.StartsWith("obstacle") || Options.Smoke.StartsWith("journey") || Options.Smoke.StartsWith("session") || Options.Smoke.StartsWith("ammo") || Options.Smoke.StartsWith("revival") || Options.Smoke.StartsWith("grenade") || Options.Smoke.StartsWith("npc") || Options.Smoke.StartsWith("container") || Options.Smoke.StartsWith("receipt") || Options.Smoke.StartsWith("body") || Options.Smoke.StartsWith("extensions") || Options.Smoke.StartsWith("freeze") || Options.Smoke.StartsWith("telekinesis") || Options.Smoke.StartsWith("melee") || Options.Smoke.StartsWith("rules") || Options.Smoke.StartsWith("pause"))
            {
                npcs.Step(Time.deltaTime);
                ApplyCommand(CommandSource?.Invoke() ?? ReadCommand(),Time.deltaTime);
            }
            Combat.Step(Time.deltaTime);
            if (Time.unscaledTime >= nextPose)
            {
                nextPose=Time.unscaledTime+0.05f;
                foreach (var owned in Session.World.actors) if (owned.owner==Session.LocalOwner) Session.PublishPose(owned,++poseSequence);
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
                command.sprint=keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                command.weapon=keyboard.digit1Key.wasPressedThisFrame?"spear":keyboard.digit2Key.wasPressedThisFrame?"crowbar":
                    keyboard.digit3Key.wasPressedThisFrame?"pistol":keyboard.digit4Key.wasPressedThisFrame?"energy":null;
                command.inventory=keyboard.iKey.wasPressedThisFrame;
                command.menu=keyboard.escapeKey.wasPressedThisFrame;
                command.reload=keyboard.rKey.wasPressedThisFrame;
                command.revive=keyboard.eKey.isPressed;command.telekinesis=keyboard.qKey.isPressed;
                command.grenade=keyboard.gKey.wasPressedThisFrame;command.heal=keyboard.hKey.wasPressedThisFrame;
            }
            if(mouse!=null)
            {
                command.hasAim=true;command.aim=cameraView.ScreenToWorldPoint(mouse.position.ReadValue());
                command.fire=mouse.leftButton.isPressed &&
                    (UnityEngine.EventSystems.EventSystem.current==null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
            }
            return command;
        }

        private void ApplyCommand(PlayerCommand command,float dt)
        {
            var actor=Session.LocalActor;
            if(command.inventory) hud?.ToggleInventory();
            if(command.menu) hud?.PauseMenu.Toggle();
            if(hud?.BlocksGameplayInput==true) command=default;
            if(Session.Pause.IsPaused(actor.level)) {Revival.Step(false,0);return;}
            if(Session.Travel.Pending) command=default;
            movement.Step(actor,command.movement,command.jump,dt,command.sprint);
            if(command.weapon!=null) Combat.Weapon=command.weapon;
            actor.weapon=Combat.Weapon;
            if(command.hasAim && !Combat.IsMeleeActive) WeaponPose.Aim(actor,command.aim);
            if(command.reload && !Combat.IsMeleeActive) Combat.Reload.Start(actor,Combat.Weapon);
            Revival.Step(command.revive,dt);
            Telekinesis.Step(command.telekinesis,command.aim,dt);
            if(command.grenade && !actor.down) Interactions.ThrowGrenade(command.aim);
            if(command.heal && !actor.down) Session.RequestSelfHeal();
            if(command.fire && command.hasAim) Combat.Fire(actor,command.aim);
        }

        private void OnApplicationQuit()
        {
            Session?.SaveLocalCharacter();
        }
    }
}
