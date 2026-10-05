using System;
using Remains.Networking;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Remains.Content;
using Remains.Domain;
using System.Collections.Generic;

namespace Remains.Presentation
{
    [DefaultExecutionOrder(100)]
    public sealed class PrototypeHud : MonoBehaviour
    {
        private CoopSession session;
        private Text status;
        private Text message;
        private GameObject lobby;
        private MainMenuPanel mainMenu;
        private GameObject inventory;
        private ContainerWindowView containerWindow;
        private FloatingContainerPanel containerPanel;
        public bool ContainerOpen=>containerPanel?.IsOpen==true;
        public RectTransform ContainerWindowRect=>containerPanel.WindowRect;
        public int FirstVisibleContainerItem=>containerPanel.FirstItem;
        public int ContainerRowCount=>containerPanel.RowCount;
        private GameObject worldPicker;
        private Text selectedWorld;
        private Button worldPickerButton,hostButton;
        private SaveStore describedStore;
        private bool worldDescriptionDirty=true;
        private SelectedWorldPanel selectedWorldPanel;
        private BackgroundSaveOperation newGameOperation;
        public bool WorldPickerReady=>worldPicker!=null && worldPicker.GetComponent<WorldPickerPanel>().Ready;
        public bool WorldDescriptionReady=>selectedWorldPanel!=null && !selectedWorldPanel.Busy;
        private readonly Dictionary<string,RectTransform> worldChoices=new();
        public RectTransform WorldPickerButtonRect=>worldPickerButton.GetComponent<RectTransform>();
        public RectTransform HostButtonRect=>hostButton.GetComponent<RectTransform>();
        public RectTransform WorldChoiceRect(string id)=>worldChoices[id];
        private InputField address;
        private InputField character;
        private CharacterLibrary characterLibrary;
        public CharacterLibraryPanel Characters {get;private set;}
        public RectTransform CharactersButtonRect=>mainMenu.Characters.GetComponent<RectTransform>();
        public Func<string,bool> SelectCharacter { get; set; }
        private InventoryArtwork inventoryArtwork;
        private ItemTextCatalog itemText;
        private InteractionHint containerHint;
        public Func<InteractionTarget> SelectedInteraction {get;set;}
        public Func<bool> PickupPending {get;set;}
        public Func<double> MechanismProgress {get;set;}
        public Func<bool> MechanismPending {get;set;}
        public InteractionTarget CurrentInteraction=>SelectedInteraction?.Invoke()??default;
        public string ShownInventorySourceId=>containerPanel.Source?.id??
            (CurrentInteraction.kind is InteractionKind.Container or InteractionKind.Pickup?CurrentInteraction.id:null);
        private Font font;
        private HudElements elements;
        public WorldLobbyPanel NetworkLobby {get;private set;}
        public CharacterContentPanel CharacterContent {get;private set;}
        public SessionModPanel SessionMods {get;private set;}
        public PauseMenu PauseMenu {get;private set;}
        public AudioSettingsPanel AudioSettings {get;private set;}
        public RectTransform MainMenuSoundRect=>mainMenu.SoundRect;
        public bool BlocksGameplayInput=>inventory.activeSelf || PauseMenu.IsOpen || AudioSettings.IsOpen || WorldMap.IsOpen;
        public bool InventoryOpen=>inventory.activeSelf;
        public bool BlocksPointerGameplay(Vector2 screenPosition)=>BlocksGameplayInput || containerPanel.PointerBlocked(screenPosition);
        public void CloseContainerForSprint(bool sprint,Vector2 movement)
        {if(sprint && movement.sqrMagnitude>0 && !BlocksGameplayInput) CloseContainer();}
        private string inventoryActorVersion;
        private CombatHud combatHud;
        private HudFeedbackView feedback;
        private GameObject quickActions;
        private float noticeUntil;
        private PersonalInventoryPanel personalItems;
        public InventoryCategory PersonalCategory=>personalItems.Category;
        public RectTransform PersonalCategoryRect(InventoryCategory category)=>personalItems.CategoryRect(category);
        private InventoryAudio inventoryAudio;
        public RectTransform DepositRowRect(int index)=>personalItems.RowRect(index);
        public int PersonalPage=>personalItems.Page;
        public int PersonalFirstVisibleRow=>personalItems.FirstVisibleRow;
        public int PersonalPageSize=>personalItems.PageSize;
        public RectTransform PersonalNextRect=>personalItems.NextRect;
        public RectTransform PersonalPreviousRect=>personalItems.PreviousRect;
        public RectTransform DepositActionRect=>personalItems.DepositRect;
        public RectTransform UseItemRect=>personalItems.UseRect;
        public RectTransform InventoryWeaponRect(int index)=>personalItems.WeaponRect(index);
        public Action<string> EquipWeapon {get;set;}
        public Action<string> EquipWeaponItem {get;set;}
        public Action<string> UseItemShortcut {get;set;}
        private Text revival;
        public Func<float> RevivalProgress {get;set;}
        public Func<bool> RevivalPending {get;set;}
        public Func<float?> ReloadRemaining {get;set;}
        private Button friendlyFire;
        private Button friendlyDecision;
        private Button leave;
        public WorldMapPanel WorldMap {get;private set;}
        public event Action HostRequested;
        public event Action<string> JoinRequested;

        public void Initialize(CoopSession value,AudioPreferences audioPreferences=null,Camera worldCamera=null)
        {
            session = value;
            itemText=ItemTextCatalog.Load();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Не найден системный шрифт Unity LegacyRuntime.ttf.");
            elements=new HudElements(font);
            inventoryArtwork=new InventoryArtwork();
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            containerHint=new InteractionHint(transform,elements);
            audioPreferences??=new AudioPreferences(null);
            inventoryAudio=gameObject.AddComponent<InventoryAudio>();inventoryAudio.Initialize(audioPreferences);
            AudioSettings=new AudioSettingsPanel(transform,audioPreferences);
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            var sessionHud=SessionHudView.Create(transform);status=sessionHud.Status;
            combatHud=new CombatHud(transform,weapon=>EquipWeapon?.Invoke(weapon),item=>EquipWeaponItem?.Invoke(item),slot=>
            {
                if(UseItemShortcut==null) {Debug.LogWarning("Не подключено действие быстрой клавиши: slot="+slot,this);return;}
                UseItemShortcut(slot);
            });
            quickActions=sessionHud.gameObject;
            feedback=HudFeedbackView.Create(transform);message=feedback.Notice;revival=feedback.Revival;
            mainMenu=new MainMenuPanel(transform,
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) PrepareConnection(()=>HostRequested?.Invoke());},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) PrepareConnection(()=>JoinRequested?.Invoke(address.text));},
                ()=>ShowWorldPicker(0),()=>AudioSettings.Open(),()=>
                {
                    if(SelectCharacter?.Invoke(character.text)!=true) return;
                    if(newGameOperation.Busy) return;
                    var store=session.Saves;mainMenu.SetActionsBusy(true);selectedWorldPanel.SetPending("Подготовка нового мира…");ShowMessage("Подготовка нового мира…");
                    newGameOperation.Run(_=>{new WorldLibrary(store).PrepareNewGame();return true;},_=>
                    {mainMenu.SetActionsBusy(false);RefreshSelectedWorld();ShowMessage("");HostRequested?.Invoke();},error=>
                    {mainMenu.SetActionsBusy(false);RefreshSelectedWorld();ShowMessage("Не удалось создать новую игру: "+error.Message);Debug.LogError(error);});
                },ShowCharacterLibrary);
            lobby=mainMenu.Controls;selectedWorld=mainMenu.World;worldPickerButton=mainMenu.Worlds;hostButton=mainMenu.Host;
            selectedWorldPanel=mainMenu.Root.AddComponent<SelectedWorldPanel>();selectedWorldPanel.Initialize(selectedWorld,hostButton);
            newGameOperation=mainMenu.Root.AddComponent<BackgroundSaveOperation>();
            character=mainMenu.Character;address=mainMenu.Address;character.text=session.CharacterId??"pony-one";
            var inventoryView=InventoryWindowView.Create(transform);inventory=inventoryView.gameObject;
            containerWindow=ContainerWindowView.Create(transform);
            containerPanel=new FloatingContainerPanel(containerWindow,session,inventoryArtwork,worldCamera,()=>
            {personalItems?.Refresh(containerPanel?.Source);PauseMenu?.PublishIntent();});
            inventory.SetActive(false);
            personalItems=new PersonalInventoryPanel(inventoryView.PersonalContents,session,ItemName,()=>0,weapon=>EquipWeapon?.Invoke(weapon),inventoryArtwork,item=>EquipWeaponItem?.Invoke(item),inventoryAudio.Page,itemText);
            var pauseView=PauseMenuView.Create(transform);
            friendlyFire=pauseView.FriendlyFire;friendlyFire.onClick.AddListener(()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=pauseView.FriendlyDecision;friendlyDecision.onClick.AddListener(()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            leave=pauseView.Leave;leave.onClick.AddListener(()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            WorldMap=new WorldMapPanel(transform,session);
            session.Changed += Refresh;
            session.Notice += ShowMessage;
            SessionMods=new SessionModPanel(transform,elements,session);
            NetworkLobby=new WorldLobbyPanel(transform,session,Refresh);
            CharacterContent=new CharacterContentPanel(transform,elements,session);
            PauseMenu=new PauseMenu(pauseView,session,()=>inventory.activeSelf || ContainerOpen,()=>AudioSettings.Open(),()=>WorldMap.IsOpen);
            WorldMap.StateChanged+=PauseMenu.PublishIntent;
            sessionHud.Inventory.onClick.AddListener(ToggleInventory);
            sessionHud.Menu.onClick.AddListener(ToggleMenu);
            Refresh();
        }

        public void ConfigureCharacters(CharacterLibrary library,bool hideUncreatedSelection)
        {
            characterLibrary=library;
            if(hideUncreatedSelection && !System.IO.File.Exists(System.IO.Path.Combine(library.ProfilePath(session.CharacterId),"character.json")))
                character.text="";
        }
        private void PrepareConnection(Action start)
        {
            if(newGameOperation.Busy) return;
            var store=session.Saves;mainMenu.SetActionsBusy(true);
            selectedWorldPanel.SetPending("Подготовка общей библиотеки миров…");
            newGameOperation.Run(_=>{store.EnsureWorldLibrary();return true;},_=>
            {mainMenu.SetActionsBusy(false);RefreshSelectedWorld();start();},error=>
            {
                mainMenu.SetActionsBusy(false);RefreshSelectedWorld();
                ShowMessage("Не удалось подготовить библиотеку миров: "+error.Message);Debug.LogError(error);
            });
        }
        public void ShowCharacterLibrary()
        {
            if(characterLibrary==null) {ShowMessage("Библиотека персонажей ещё не подключена.");return;}
            if(Characters!=null || session.LocalActor!=null) return;
            mainMenu.SetActionsBusy(true);
            Characters=CharacterLibraryPanel.Open(transform,characterLibrary,inventoryArtwork,session.CharacterId,name=>
            {
                if(SelectCharacter?.Invoke(name)!=true) return false;
                character.text=name;RefreshSelectedWorld();return true;
            },()=>{Characters=null;mainMenu.SetActionsBusy(false);RefreshSelectedWorld();});
        }

        public void ToggleMenu()
        {
            if(Characters!=null) {Characters.Close();return;}
            if(AudioSettings.IsOpen) {AudioSettings.Cancel();return;}
            if(WorldMap.IsOpen) {WorldMap.Close();return;}
            if(InventoryOpen) {ToggleInventory();CloseContainer();return;}
            if(ContainerOpen) {CloseContainer();return;}
            PauseMenu.Toggle();
        }
        public void ToggleInventory() {if(AudioSettings.IsOpen || PauseMenu.IsOpen) return;if(WorldMap.IsOpen) WorldMap.Close();inventoryAudio.Toggle();inventory.SetActive(!inventory.activeSelf);if(inventory.activeSelf) inventory.transform.SetAsLastSibling();Refresh();PauseMenu.PublishIntent();}
        public void OpenContainer() {if(!ContainerOpen) ToggleContainer();}
        public void ToggleContainer()
        {
            if(ContainerOpen) {CloseContainer();return;}
            if(AudioSettings.IsOpen || PauseMenu.IsOpen || session.Travel.Pending || session.LocalActor is not {down:false}) return;
            var target=CurrentInteraction;
            if(target.kind!=InteractionKind.Container) {ShowMessage("Курсор не выбрал доступный контейнер.");return;}
            OpenContainer(target.id);
        }
        public void OpenContainer(string id)
        {
            if(AudioSettings.IsOpen || PauseMenu.IsOpen || session.Travel.Pending || session.LocalActor is not {down:false}) return;
            var source=InventorySource.Resolve(session.World,id);
            if(source?.IsLooseItem==true) {ShowMessage("Свободная вещь подбирается по E целиком.");return;}
            if(!ContainerAccess.Accessible(session.LocalActor,source,session.Rules.Loot,session.Rules.Deposit))
            {ShowMessage("Контейнер больше недоступен.");return;}
            if(!containerPanel.Open(source.id)) return;
            inventoryAudio.Toggle();Refresh();PauseMenu.PublishIntent();
        }
        public void CloseContainer()
        {
            containerPanel.Close();
        }
        public RectTransform FriendlyFireButtonRect=>friendlyFire.GetComponent<RectTransform>();
        public RectTransform FriendlyDecisionButtonRect=>friendlyDecision.GetComponent<RectTransform>();
        public bool FriendlyDecisionInteractable=>friendlyDecision.interactable;
        public RectTransform ContainerSlotRect(int index)=>containerPanel.SlotRect(index);
        public void ShowMessage(string text)
        {
            noticeUntil=Time.unscaledTime+8;message.text=text;LayoutNotice();
            if(mainMenu!=null) mainMenu.Notice.text=text;
        }
        private void LayoutNotice()
        {
            feedback.LayoutNotice(inventory.activeSelf);
        }
        private void Refresh()
        {
            var actor = session.LocalActor;
            mainMenu.Root.SetActive(actor==null);
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(inventoryActorVersion!=version)
            {
                if(inventory.activeSelf || ContainerOpen)
                    Debug.Log($"Окна инвентаря закрыты при смене состояния персонажа: прежнее={inventoryActorVersion}, текущее={version}, container={containerPanel.Source?.id}.");
                inventoryActorVersion=version;inventory.SetActive(false);containerPanel.Close();
            }
            bool gameplayVisible=actor!=null && !inventory.activeSelf && !WorldMap.IsOpen;
            status.transform.parent.gameObject.SetActive(gameplayVisible);
            combatHud.Refresh(actor,gameplayVisible && !PauseMenu.IsOpen,ReloadRemaining?.Invoke());quickActions.SetActive(gameplayVisible && !PauseMenu.IsOpen);
            LayoutNotice();
            friendlyDecision.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            if (actor == null)
            {containerHint.Refresh(null,false);lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld(false);inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
            worldDescriptionDirty=true;
            lobby.SetActive(false);
            leave.gameObject.SetActive(gameplayVisible);friendlyFire.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            friendlyFire.GetComponentInChildren<Text>().text="Дружественный огонь: "+(session.World.friendlyFire?"включён":"выключен");
            friendlyDecision.interactable=session.World.friendlyFire;
            friendlyDecision.GetComponentInChildren<Text>().text="Прямое попадание:\nрешает "+(session.World.friendlyFireDecision==FriendlyFireDecision.Attacker?"атакующий":"пострадавший");
            status.text=$"{(actor.level=="hub"?"Хаб":"Завод 0")} · игроков: {session.World.actors.FindAll(a=>a.player && a.connected).Count}";

            containerPanel.Refresh();
            RefreshInteractionHint();
            personalItems.Refresh(containerPanel.Source);
        }
        private void RefreshSelectedWorld(bool force=true)
        {
            if(!force && !worldDescriptionDirty && ReferenceEquals(describedStore,session.Saves)) return;
            describedStore=session.Saves;worldDescriptionDirty=false;
            selectedWorldPanel.Refresh(session.Saves);
        }

        private void ShowWorldPicker(int page)
        {
            RefreshSelectedWorld();
            if(worldPicker!=null) Destroy(worldPicker);
            worldChoices.Clear();
            var view=WorldPickerView.Create(transform);worldPicker=view.gameObject;
            var panel=worldPicker.AddComponent<WorldPickerPanel>();
            panel.Initialize(view,session.Saves,page,worldChoices,()=>RefreshSelectedWorld(),ShowMessage);
        }

        private string ItemName(string id)=>itemText.Name(id,"PrototypeHud.ItemName");

        private void RefreshInteractionHint()
        {
            bool show=session.LocalActor!=null && !ContainerOpen && !BlocksGameplayInput;
            var interaction=session.LocalActor==null?default:CurrentInteraction;
            if(interaction.kind is InteractionKind.Map or InteractionKind.Exit)
                containerHint.Refresh(session.Travel.Find(interaction.id,session.LocalActor.level),show,session.LocalActor);
            else if(interaction.kind==InteractionKind.Door)
                containerHint.Refresh(session.World.doors.Find(door=>door.id==interaction.id),show,session.LocalActor);
            else if(interaction.kind==InteractionKind.Revival)
                containerHint.RefreshRevival(session.World.Find(interaction.id),show,session.LocalActor);
            else if(interaction.kind==InteractionKind.Mechanism)
                containerHint.RefreshMechanism(session.World.mechanismButtons.Find(button=>button.id==interaction.id),interaction.bounds,
                    show,session.LocalActor,MechanismProgress?.Invoke()??0,MechanismPending?.Invoke()==true);
            else containerHint.RefreshSource(interaction.kind is InteractionKind.Container or InteractionKind.Pickup?
                InventorySource.Resolve(session.World,interaction.id):null,show,session.LocalActor,PickupPending?.Invoke()==true);
            containerHint.Highlight(interaction,show);
        }
        private void Update()
        {
            if(session.LocalActor==null && Application.isFocused && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
            {
                if(Characters!=null) ToggleMenu();
                else if(AudioSettings.IsOpen) AudioSettings.Cancel();
            }
            WorldMap.Refresh();
            bool show=session.LocalActor!=null && !inventory.activeSelf && !PauseMenu.IsOpen && !WorldMap.IsOpen;
            RefreshInteractionHint();
            combatHud.Refresh(session.LocalActor,show,ReloadRemaining?.Invoke());quickActions.SetActive(show);
            status.transform.parent.gameObject.SetActive(show);
            message.transform.parent.gameObject.SetActive(session.LocalActor!=null && Time.unscaledTime<noticeUntil && !string.IsNullOrEmpty(message.text));
            if(revival==null) return;
            float progress=RevivalProgress?.Invoke() ?? 0;
            revival.text=RevivalPending?.Invoke()==true?"Ожидаем подтверждения поднятия…":
                progress>0?$"Поднимаем союзника: {progress:P0} · удерживайте E":"";
            revival.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(revival.text));
        }
        private void OnDestroy()=>containerHint?.Dispose();
        private void LateUpdate()=>containerPanel?.Step(InventoryOpen || PauseMenu.IsOpen || AudioSettings.IsOpen || WorldMap.IsOpen);
        private static void ApplyOriginalImage(Image image,string key,bool filled)
        {
            var texture=Resources.Load<Texture2D>("Original/Hud/"+key);
            if(texture==null) throw new InvalidOperationException($"Не найдена графика интерфейса: {key}.");
            image.color=Color.white;
            image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f));
            image.type=filled?Image.Type.Filled:Image.Type.Simple;
            image.fillMethod=Image.FillMethod.Horizontal;image.fillOrigin=0;
        }

        private GameObject Panel(Transform parent,string title,Vector2 position,Vector2 size)=>elements.Panel(parent,title,position,size);
        private Text Label(Transform parent,string content,Vector2 position,Vector2 size)=>elements.Label(parent,content,position,size);
        private Button Button(Transform parent,string content,Vector2 position,Vector2 size,Action action)=>elements.Button(parent,content,position,size,action);
        private InputField Input(Transform parent,Vector2 position)=>elements.Input(parent,position);
    }
}




