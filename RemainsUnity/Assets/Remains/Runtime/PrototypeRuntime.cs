using System.Collections.Generic;
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
        public System.Func<PlayerCommand> CommandSource { get; set; }
        public PrototypeHud Hud => hud;
        private MovementController movement;
        private NpcController npcs;
        private readonly Dictionary<string,PonyVisual> visuals = new();
        private PrototypeHud hud;
        private Camera cameraView;
        private float nextPose;
        private uint poseSequence;
        private GameObject roomObject;
        private string shownLevel="hub";
        private CameraFollower cameraFollower;

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
            Session.UseLobby=Options.Smoke==null || Options.Smoke.StartsWith("lobby") || Options.Smoke.StartsWith("protocol") || Options.Smoke.StartsWith("grenade-resume-"); Session.autoCreatePlayer=false; Session.maxConnections=Options.Capacity;
            Session.CharacterId=Options.Character;
            Session.networkAddress=Options.Address;
            Session.Saves=new SaveStore(Options.SaveDirectory ?? Path.Combine(Application.persistentDataPath,"Profiles",Options.Character));
            // NetworkManager.Awake must see the final transport, including a test wrapper.
            networkObject.SetActive(true);
            DontDestroyOnLoad(gameObject);
            Combat=new CombatController(Session);
            Revival=new RevivalController(Session);
            Interactions=new InteractionAdapter(Session);
            Grenades=new GrenadeController(Session,Interactions,Combat);
            Telekinesis=new TelekinesisController(Session,Interactions);
            new GameObject("Визуальные взаимодействия").AddComponent<InteractionVisuals>().Initialize(Session,Interactions,Grenades);
            var room=RoomLayout.Load(shownLevel);
            movement=new MovementController(room);
            npcs=new NpcController(Session,room,RoomLayout.Load("factory:0"));
            roomObject=RoomVisual.Build(room);
            cameraView=Camera.main;
            if (cameraView == null) cameraView=new GameObject("Камера").AddComponent<Camera>();
            cameraView.orthographic=true; cameraView.orthographicSize=5;
            cameraView.transform.position=new Vector3(0,3,-10); cameraView.backgroundColor=new Color(0.08f,0.09f,0.1f);
            cameraFollower=new CameraFollower(cameraView,room);
            PonyVisual.Prepare();
            Session.Changed+=RefreshVisuals;
            if (!Application.isBatchMode || Options.Smoke?.StartsWith("session")==true || Options.Smoke?.StartsWith("container")==true || Options.Smoke=="history" || Options.Smoke?.StartsWith("lobby")==true)
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
            if (Options.Smoke != null)
            {
                if(Options.Smoke.StartsWith("lobby")) gameObject.AddComponent<NetworkLobbyProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("extensions")) gameObject.AddComponent<NetworkExtensionProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("protocol")) gameObject.AddComponent<NetworkProtocolProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("receipt")) gameObject.AddComponent<NetworkReceiptLossProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("container")) gameObject.AddComponent<NetworkContainerProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("npc")) gameObject.AddComponent<NetworkNpcProbe>().Initialize(this);
                else if(Options.Smoke.StartsWith("freeze")) gameObject.AddComponent<NetworkFreezeProbe>().Initialize(this);
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
            if (Session.World==null) { foreach(var visual in visuals.Values) visual.SetVisible(false);return; }
            if(Session.LocalActor!=null && shownLevel!=Session.LocalActor.level)
            {
                shownLevel=Session.LocalActor.level;
                var room=RoomLayout.Load(shownLevel);
                Destroy(roomObject);roomObject=RoomVisual.Build(room);
                movement=new MovementController(room);
                cameraFollower.Room=room;
            }
            foreach (var actor in Session.World.actors)
            {
                if (!ActorPresence.IsPresent(actor)) continue;
                if (!visuals.TryGetValue(actor.id,out var visual))
                {
                    visual=new GameObject(actor.id).AddComponent<PonyVisual>(); visuals.Add(actor.id,visual);
                }
                visual.Bind(actor);
                visual.SetVisible(actor.level==Session.LocalActor?.level);
            }
            var removed=new List<string>();
            foreach (var pair in visuals) if (!ActorPresence.IsPresent(Session.World.Find(pair.Key))) { Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            foreach (var id in removed) visuals.Remove(id);
        }

        private void Update()
        {
            Session.Admission.Step();Session.Travel.Step();Session.Lobby.Step();
            var actor=Session.LocalActor;
            if (actor==null || !NetworkClient.isConnected) return;
            cameraFollower.Step(actor,Time.deltaTime);
            foreach(var animated in Session.World.actors)
                if(LevelActivity.HasConnectedPlayer(Session.World,animated.level)) animated.poseTime+=Time.deltaTime;
            Grenades.Step(Time.deltaTime);
            if (Options.Smoke==null || Options.Smoke.StartsWith("session") || Options.Smoke.StartsWith("ammo") || Options.Smoke.StartsWith("revival") || Options.Smoke.StartsWith("grenade") || Options.Smoke.StartsWith("npc") || Options.Smoke.StartsWith("container") || Options.Smoke.StartsWith("receipt") || Options.Smoke.StartsWith("extensions") || Options.Smoke.StartsWith("freeze"))
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
            if(Session.Travel.Pending) command=default;
            if(command.hasAim && !Combat.IsMeleeActive)
            {
                var direction=command.aim-(actor.Position+new Vector2(0,.8f));
                actor.weaponAngle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            }
            movement.Step(actor,command.movement,command.jump,dt,command.sprint);
            if(command.weapon!=null) Combat.Weapon=command.weapon;
            actor.weapon=Combat.Weapon;
            if(command.inventory) hud?.ToggleInventory();
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
