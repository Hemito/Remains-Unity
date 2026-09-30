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

namespace Remains.Runtime
{
    public sealed class PrototypeRuntime : MonoBehaviour
    {
        public CoopSession Session { get; private set; }
        public CombatController Combat { get; private set; }
        public LaunchOptions Options { get; private set; }
        public RevivalController Revival { get; private set; }
        private MovementController movement;
        private NpcController npcs;
        private readonly Dictionary<string,PonyVisual> visuals = new();
        private PrototypeHud hud;
        private Camera cameraView;
        private float nextPose;
        private uint poseSequence;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<PrototypeRuntime>() == null) new GameObject("Remains Runtime").AddComponent<PrototypeRuntime>();
        }

        private void Awake()
        {
            Options=LaunchOptions.Parse();
            Anatomy.Load();
            Application.runInBackground=true;
            Application.targetFrameRate=60;
            var transport=gameObject.AddComponent<KcpTransport>(); transport.port=Options.Port; transport.statisticsGUI=false;
            Session=gameObject.AddComponent<CoopSession>(); Session.transport=transport; Session.autoCreatePlayer=false; Session.maxConnections=16;
            Session.CharacterId=Options.Character;
            Session.networkAddress=Options.Address;
            Session.Saves=new SaveStore(Options.SaveDirectory ?? Path.Combine(Application.persistentDataPath,"Profiles",Options.Character));
            Combat=new CombatController(Session);
            Revival=new RevivalController(Session);
            var room=RoomLayout.Load();
            movement=new MovementController(room);
            npcs=new NpcController(Session,room);
            RoomVisual.Build(room);
            cameraView=Camera.main;
            if (cameraView == null) cameraView=new GameObject("Камера").AddComponent<Camera>();
            cameraView.orthographic=true; cameraView.orthographicSize=5;
            cameraView.transform.position=new Vector3(0,3,-10); cameraView.backgroundColor=new Color(0.08f,0.09f,0.1f);
            PonyVisual.Prepare();
            Session.Changed+=RefreshVisuals;
            if (!Application.isBatchMode)
            {
                hud=new GameObject("Интерфейс").AddComponent<PrototypeHud>(); hud.Initialize(Session);
                hud.HostRequested+=()=>Session.StartHost();
                hud.JoinRequested+=address=> { Session.networkAddress=address; Session.StartClient(); };
            }
            if (Options.Smoke != null) gameObject.AddComponent<NetworkSmoke>().Initialize(this);
        }

        private void Start()
        {
            if (Options.Role=="host") Session.StartHost();
            else if (Options.Role=="client") Session.StartClient();
        }

        private void RefreshVisuals()
        {
            if (Session.World==null) return;
            foreach (var actor in Session.World.actors)
            {
                if (!visuals.TryGetValue(actor.id,out var visual))
                {
                    visual=new GameObject(actor.id).AddComponent<PonyVisual>(); visuals.Add(actor.id,visual);
                }
                visual.Bind(actor);
            }
            var removed=new List<string>();
            foreach (var pair in visuals) if (Session.World.Find(pair.Key)==null) { Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            foreach (var id in removed) visuals.Remove(id);
        }

        private void Update()
        {
            var actor=Session.LocalActor;
            if (actor==null || !NetworkClient.isConnected) return;
            if (Options.Smoke==null)
            {
                npcs.Step(Time.deltaTime);
                var keyboard=Keyboard.current; var mouse=Mouse.current;
                if (keyboard!=null)
                {
                    var input=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),
                        (keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
                    movement.Step(actor,input,keyboard.spaceKey.wasPressedThisFrame,Time.deltaTime);
                    if(keyboard.digit1Key.wasPressedThisFrame) Combat.Weapon="spear";
                    if(keyboard.digit2Key.wasPressedThisFrame) Combat.Weapon="crowbar";
                    if(keyboard.digit3Key.wasPressedThisFrame) Combat.Weapon="pistol";
                    if(keyboard.digit4Key.wasPressedThisFrame) Combat.Weapon="energy";
                    if(keyboard.iKey.wasPressedThisFrame) hud?.ToggleInventory();
                    Revival.Step(keyboard.eKey.isPressed,Time.deltaTime);
                }
                if(mouse!=null && mouse.leftButton.isPressed)
                {
                    var point=cameraView.ScreenToWorldPoint(mouse.position.ReadValue()); Combat.Fire(actor,point);
                }
            }
            if (Time.unscaledTime >= nextPose)
            {
                nextPose=Time.unscaledTime+0.05f;
                foreach (var owned in Session.World.actors) if (owned.owner==Session.LocalOwner) Session.PublishPose(owned,++poseSequence);
            }
        }

        private void OnApplicationQuit()
        {
            if(Session.World!=null) Session.Saves.SaveWorld(Session.World);
            if(Session.LocalActor!=null) Session.Saves.SaveCharacter(Session.LocalActor);
        }
    }
}
