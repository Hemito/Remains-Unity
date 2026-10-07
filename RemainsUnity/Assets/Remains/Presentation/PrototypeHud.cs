using System;
using Remains.Networking;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
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
        private IReadOnlyList<RectTransform> containerLayoutReservations;
        public Func<ActorVisual> LocalActorVisual {get;set;}
        public Func<SpriteRenderer> LocalWeaponArtwork {get;set;}
        public Func<SpriteRenderer> LocalHeldItemArtwork {get;set;}
        public Rect? LocalActorArtworkBounds
        {
            get
            {
                var actor=session?.LocalActor;var visual=LocalActorVisual?.Invoke();
                return actor!=null && visual!=null && visual.ActorId==actor.id && visual.TryGetDrawnBounds(out var bounds)?ActorArtworkBounds.WorldRect(bounds):null;
            }
        }
        public Rect? LocalWeaponArtworkBounds=>ActorArtworkBounds.VisibleSprite(LocalWeaponArtwork?.Invoke());
        public Rect? LocalHeldItemArtworkBounds=>ActorArtworkBounds.VisibleSprite(LocalHeldItemArtwork?.Invoke());
        public IReadOnlyList<RectTransform> ContainerLayoutReservations=>containerLayoutReservations??
            throw new InvalidOperationException("Окно контейнера: HUD ещё не собрал области интерфейса.");
        public bool ContainerOpen=>containerPanel?.IsOpen==true;
        public int ContainerCellBindingCount=>containerPanel?.CellBindingCount??0;
        public int InteractionHintTextRevisionCount=>containerHint?.TextRevisionCount??0;
        public int InteractionHintPlacementEvaluationCount=>containerHint?.PlacementEvaluationCount??0;
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
        public WorldInteractionOutline InteractionOutline=>containerHint?.WorldOutline;
        public void ConfigureWorldOutline(Camera camera,Func<InteractionTarget,WorldOutlineSource> resolve)=>
            containerHint.ConfigureWorldOutline(camera,resolve);
        public string ShownInventorySourceId=>containerPanel.Source?.id??
            (CurrentInteraction.kind is InteractionKind.Container or InteractionKind.Pickup?CurrentInteraction.id:null);
        private Font font;
        private HudElements elements;
        public WorldLobbyPanel NetworkLobby {get;private set;}
        public CharacterContentPanel CharacterContent {get;private set;}
        public SessionModPanel SessionMods {get;private set;}
        public PauseMenu PauseMenu {get;private set;}
        private RoomLoadingView roomLoading;
        private Transform pauseMenuLayer;
        private bool roomPresentationPending;
        public bool RoomPresentationPending=>roomPresentationPending && session?.LocalActor!=null;
        public bool RoomLoadingVisible=>roomLoading!=null && roomLoading.IsVisible;
        public RectTransform RoomLoadingRect=>roomLoading.Rect;
        public AudioSettingsPanel AudioSettings {get;private set;}
        public RectTransform MainMenuSoundRect=>mainMenu.SoundRect;
        public bool BlocksGameplayInput=>RoomPresentationPending || inventory.activeSelf || PauseMenu.IsOpen || AudioSettings.IsOpen || WorldMap.IsOpen;
        public bool InventoryOpen=>inventory.activeSelf;
        public bool BlocksPointerGameplay(Vector2 screenPosition)=>BlocksGameplayInput || containerPanel.PointerBlocked(screenPosition);
        public void CloseContainerForSprint(bool sprint,Vector2 movement)
        {if(sprint && movement.sqrMagnitude>0 && !BlocksGameplayInput) CloseContainer();}
        private string inventoryActorId;
        private int inventoryActorGeneration,inventoryActorLifecycle;
        private string statusLevel;
        private int statusPlayers=-1;
        private Text friendlyFireText,friendlyDecisionText;
        private bool? displayedFriendlyFire;
        private string displayedFriendlyDecision;
        public int PersonalInventoryRefreshCount {get;private set;}
        private CombatHud combatHud;
        private HudFeedbackView feedback;
        private GameObject quickActions;
        private float noticeUntil;
        private PersonalInventoryPanel personalItems;
        private ItemShortcutActions itemActions;
        public string PersonalSelectedItemId=>personalItems.SelectedItemId;
        public void ConfigureItemActions(ItemShortcutActions actions)
        {
            itemActions=actions??throw new ArgumentNullException(nameof(actions));
            if(personalItems!=null) RefreshPersonalInventory();
        }
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
            containerHint=new InteractionHint(transform,elements,()=>LocalActorArtworkBounds);
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
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) PrepareConnection(copy=>
                {session.ConfigureHostStartup(copy==null?SessionStartupIntent.NewWorld():SessionStartupIntent.Continue(copy));HostRequested?.Invoke();},true);},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) PrepareConnection(_=>JoinRequested?.Invoke(address.text));},
                ()=>ShowWorldPicker(0),()=>AudioSettings.Open(),()=>
                {
                    if(SelectCharacter?.Invoke(character.text)!=true) return;
                    if(newGameOperation.Busy) return;
                    session.BeginJoinSelection();
                    var store=session.Saves;mainMenu.SetActionsBusy(true);selectedWorldPanel.SetPending("Подготовка нового мира…");ShowMessage("Подготовка нового мира…");
                    newGameOperation.Run(_=>{store.RecoverPendingPair();new WorldLibrary(store).PrepareNewGame();return true;},_=>
                    {mainMenu.SetActionsBusy(false);RefreshSelectedWorld();ShowMessage("");session.ConfigureHostStartup(SessionStartupIntent.NewWorld());HostRequested?.Invoke();},error=>
                    {session.CancelJoinSelection();mainMenu.SetActionsBusy(false);RefreshSelectedWorld();ShowMessage("Не удалось создать новую игру: "+error.Message);Debug.LogError(error);});
                },ShowCharacterLibrary,()=>
                {
                    if(SelectCharacter?.Invoke(character.text)!=true) return;
                    PrepareConnection(_=>{session.ConfigureHostStartup(SessionStartupIntent.ChooseCopies());HostRequested?.Invoke();});
                });
            lobby=mainMenu.Controls;selectedWorld=mainMenu.World;worldPickerButton=mainMenu.Worlds;hostButton=mainMenu.Host;
            selectedWorldPanel=mainMenu.Root.AddComponent<SelectedWorldPanel>();selectedWorldPanel.Initialize(selectedWorld,hostButton,()=>!newGameOperation.Busy && !session.CharacterSelectionLocked);
            newGameOperation=mainMenu.Root.AddComponent<BackgroundSaveOperation>();
            character=mainMenu.Character;address=mainMenu.Address;character.text=session.CharacterId??"pony-one";
            var inventoryView=InventoryWindowView.Create(transform);inventory=inventoryView.gameObject;
            containerWindow=ContainerWindowView.Create(transform);
            containerPanel=new FloatingContainerPanel(containerWindow,session,inventoryArtwork,worldCamera,()=>
            {RefreshPersonalInventory();PauseMenu?.PublishIntent();});
            var reservedAreas=new List<RectTransform>(combatHud.LayoutReservations) {
                (RectTransform)sessionHud.transform,(RectTransform)message.transform.parent,(RectTransform)revival.transform.parent};
            var containerReservations=reservedAreas.ToArray();
            containerLayoutReservations=Array.AsReadOnly(containerReservations);
            containerPanel.ConfigureArtworkLayout(containerReservations,()=>LocalActorArtworkBounds,()=>LocalWeaponArtworkBounds,()=>LocalHeldItemArtworkBounds,
                ()=>$"actor={session.LocalActor?.id??"не назначен"}, type={session.LocalActor?.actorType??"не назначен"}");
            inventory.SetActive(false);
            personalItems=new PersonalInventoryPanel(inventoryView.PersonalContents,session,ItemName,()=>0,weapon=>EquipWeapon?.Invoke(weapon),inventoryArtwork,item=>EquipWeaponItem?.Invoke(item),inventoryAudio.Page,itemText,()=>itemActions);
            var pauseView=PauseMenuView.Create(transform);
            pauseMenuLayer=pauseView.transform;
            friendlyFire=pauseView.FriendlyFire;friendlyFire.onClick.AddListener(()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=pauseView.FriendlyDecision;friendlyDecision.onClick.AddListener(()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            friendlyFireText=friendlyFire.GetComponentInChildren<Text>();friendlyDecisionText=friendlyDecision.GetComponentInChildren<Text>();
            leave=pauseView.Leave;leave.onClick.AddListener(()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            WorldMap=new WorldMapPanel(transform,session);
            session.Changed += Refresh;
            session.SessionStopped+=RefreshAfterSessionStop;
            session.Notice += ShowMessage;
            SessionMods=new SessionModPanel(transform,elements,session);
            NetworkLobby=new WorldLobbyPanel(transform,session,Refresh);
            CharacterContent=new CharacterContentPanel(transform,elements,session);
            PauseMenu=new PauseMenu(pauseView,session,()=>inventory.activeSelf || ContainerOpen,()=>AudioSettings.Open(),()=>WorldMap.IsOpen);
            WorldMap.StateChanged+=PauseMenu.PublishIntent;
            sessionHud.Inventory.onClick.AddListener(ToggleInventory);
            sessionHud.Menu.onClick.AddListener(ToggleMenu);
            roomLoading=RoomLoadingView.Create(transform,font,pauseMenuLayer);
            Refresh();
        }

        // Explicit presentation events own this flag; loading is not a pause request.
        public void SetRoomPresentationPending(bool pending)
        {
            if(session==null || roomLoading==null)
                throw new InvalidOperationException("Загрузка локации: HUD ещё не инициализирован.");
            bool next=pending && session.LocalActor!=null;
            if(roomPresentationPending==next) return;
            roomPresentationPending=next;
            if(next)
            {
                inventory.SetActive(false);CloseContainer();WorldMap.Close();
                PauseMenu.PublishIntent();
            }
            Refresh();
        }
        private void RefreshRoomLoading()
        {
            if(session.LocalActor==null) roomPresentationPending=false;
            bool visible=RoomPresentationPending;
            if(roomLoading.IsVisible==visible) return;
            roomLoading.SetVisible(visible);
        }

        public void ConfigureCharacters(CharacterLibrary library,bool hideUncreatedSelection)
        {
            characterLibrary=library;
            if(hideUncreatedSelection && !System.IO.File.Exists(System.IO.Path.Combine(library.ProfilePath(session.CharacterId),"character.json")))
                character.text="";
        }
        private void PrepareConnection(Action<RecoverySnapshot> start,bool readSelected=false)
        {
            if(newGameOperation.Busy || Mirror.NetworkClient.active || Mirror.NetworkServer.active) return;
            session.BeginJoinSelection();
            var store=session.Saves;mainMenu.SetActionsBusy(true);
            selectedWorldPanel.SetPending("Подготовка общей библиотеки миров…");
            newGameOperation.Run(_=>{store.RecoverPendingPair();store.EnsureWorldLibrary();return readSelected?store.SelectedSnapshot():null;},copy=>
            {mainMenu.SetActionsBusy(false);RefreshSelectedWorld();start(copy);},error=>
            {
                session.CancelJoinSelection();mainMenu.SetActionsBusy(false);RefreshSelectedWorld();
                ShowMessage("Не удалось подготовить библиотеку миров: "+error.Message);Debug.LogError(error);
            });
        }
        public void ShowCharacterLibrary()
        {
            if(characterLibrary==null) {ShowMessage("Библиотека персонажей ещё не подключена.");return;}
            if(Characters!=null || session.LocalActor!=null || newGameOperation.Busy || session.CharacterSelectionLocked) return;
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
        public void ToggleInventory() {if(RoomPresentationPending || AudioSettings.IsOpen || PauseMenu.IsOpen) return;if(WorldMap.IsOpen) WorldMap.Close();inventoryAudio.Toggle();inventory.SetActive(!inventory.activeSelf);if(inventory.activeSelf) inventory.transform.SetAsLastSibling();Refresh();PauseMenu.PublishIntent();}
        public void OpenContainer() {if(!ContainerOpen) ToggleContainer();}
        public void ToggleContainer()
        {
            if(ContainerOpen) {CloseContainer();return;}
            if(RoomPresentationPending || AudioSettings.IsOpen || PauseMenu.IsOpen || session.Travel.Pending || session.LocalActor is not {down:false}) return;
            var target=CurrentInteraction;
            if(target.kind!=InteractionKind.Container) {ShowMessage("Курсор не выбрал доступный контейнер.");return;}
            OpenContainer(target.id);
        }
        public void OpenContainer(string id)
        {
            if(RoomPresentationPending || AudioSettings.IsOpen || PauseMenu.IsOpen || session.Travel.Pending || session.LocalActor is not {down:false}) return;
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
            RefreshRoomLoading();
            mainMenu.Root.SetActive(actor==null);
            if(actor==null) mainMenu.SetActionsBusy(newGameOperation.Busy || session.CharacterSelectionLocked);
            if(inventoryActorId!=actor?.id || actor!=null && (inventoryActorGeneration!=actor.generation || inventoryActorLifecycle!=actor.lifecycleSequence))
            {
                if(inventory.activeSelf || ContainerOpen)
                    Debug.Log($"Окна инвентаря закрыты при смене состояния персонажа: прежнее={inventoryActorId}:{inventoryActorGeneration}:{inventoryActorLifecycle}, текущее={actor?.id}:{actor?.generation}:{actor?.lifecycleSequence}, container={containerPanel.Source?.id}.");
                inventoryActorId=actor?.id;inventoryActorGeneration=actor?.generation??0;inventoryActorLifecycle=actor?.lifecycleSequence??0;inventory.SetActive(false);containerPanel.Close();
            }
            bool gameplayVisible=actor!=null && !RoomPresentationPending && !inventory.activeSelf && !WorldMap.IsOpen;
            status.transform.parent.gameObject.SetActive(gameplayVisible);
            quickActions.SetActive(gameplayVisible && !PauseMenu.IsOpen);
            LayoutNotice();
            friendlyDecision.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            if (actor == null)
            {statusLevel=null;statusPlayers=-1;containerHint.Refresh(null,false);lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld(false);inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
            worldDescriptionDirty=true;
            lobby.SetActive(false);
            leave.gameObject.SetActive(actor!=null);friendlyFire.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            if(displayedFriendlyFire!=session.World.friendlyFire)
            {displayedFriendlyFire=session.World.friendlyFire;friendlyFireText.text="Дружественный огонь: "+(session.World.friendlyFire?"включён":"выключен");}
            friendlyDecision.interactable=session.World.friendlyFire;
            if(displayedFriendlyDecision!=session.World.friendlyFireDecision)
            {displayedFriendlyDecision=session.World.friendlyFireDecision;friendlyDecisionText.text="Прямое попадание:\nрешает "+(session.World.friendlyFireDecision==FriendlyFireDecision.Attacker?"атакующий":"пострадавший");}
            int players=0;foreach(var member in session.World.actors) if(member.player && member.connected) players++;
            if(statusLevel!=actor.level || statusPlayers!=players)
            {statusLevel=actor.level;statusPlayers=players;status.text=$"{(actor.level=="hub"?"Хаб":"Завод 0")} · игроков: {players}";}

            containerPanel.Refresh();
            RefreshPersonalInventory();
        }
        private void RefreshAfterSessionStop()
        {
            // The early state-clear event still hides gameplay. Refresh the menu
            // again only after network shutdown and the final save writes are done.
            worldDescriptionDirty=true;
            Refresh();
        }
        private void RefreshPersonalInventory()
        {
            if(!inventory.activeSelf || personalItems==null) return;
            PersonalInventoryRefreshCount++;personalItems.Refresh(containerPanel.Source);
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
            var actor=session.LocalActor;
            bool show=actor!=null && !ContainerOpen && !BlocksGameplayInput;
            var interaction=actor==null?default:CurrentInteraction;
            if(interaction.kind is InteractionKind.Map or InteractionKind.Exit)
                containerHint.Refresh(session.Travel.Find(interaction.id,actor.level),show,actor);
            else if(interaction.kind==InteractionKind.Door)
            {
                DoorState selected=null;foreach(var door in session.World.doors) if(door.id==interaction.id) {selected=door;break;}
                containerHint.Refresh(selected,show,actor);
            }
            else if(interaction.kind==InteractionKind.Revival)
                containerHint.RefreshRevival(session.World.Find(interaction.id),show,actor);
            else if(interaction.kind==InteractionKind.Mechanism)
            {
                MechanismButtonState selected=null;foreach(var button in session.World.mechanismButtons) if(button.id==interaction.id) {selected=button;break;}
                containerHint.RefreshMechanism(selected,interaction.bounds,show,actor,MechanismProgress?.Invoke()??0,MechanismPending?.Invoke()==true);
            }
            else containerHint.RefreshSource(interaction.kind is InteractionKind.Container or InteractionKind.Pickup?
                InventorySource.Resolve(session.World,interaction.id):null,show,actor,PickupPending?.Invoke()==true);
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
            bool show=session.LocalActor!=null && !RoomPresentationPending && !inventory.activeSelf && !PauseMenu.IsOpen && !WorldMap.IsOpen;
            RefreshInteractionHint();
            combatHud.Refresh(session.LocalActor,show,ReloadRemaining?.Invoke());quickActions.SetActive(show);
            status.transform.parent.gameObject.SetActive(show);
            message.transform.parent.gameObject.SetActive(session.LocalActor!=null && !RoomPresentationPending && Time.unscaledTime<noticeUntil && !string.IsNullOrEmpty(message.text));
            if(revival==null) return;
            float progress=RevivalProgress?.Invoke() ?? 0;
            revival.text=RevivalPending?.Invoke()==true?"Ожидаем подтверждения поднятия…":
                progress>0?$"Поднимаем союзника: {progress:P0} · удерживайте E":"";
            revival.transform.parent.gameObject.SetActive(!RoomPresentationPending && !string.IsNullOrEmpty(revival.text));
        }
        private void OnDestroy()
        {
            containerHint?.Dispose();
            if(session!=null) {session.Changed-=Refresh;session.SessionStopped-=RefreshAfterSessionStop;}
        }
        private void LateUpdate()=>containerPanel?.Step(RoomPresentationPending || InventoryOpen || PauseMenu.IsOpen || AudioSettings.IsOpen || WorldMap.IsOpen);
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




