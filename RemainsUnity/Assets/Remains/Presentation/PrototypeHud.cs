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
    public sealed class PrototypeHud : MonoBehaviour
    {
        private CoopSession session;
        private Text status;
        private Text message;
        private GameObject lobby;
        private MainMenuPanel mainMenu;
        private GameObject inventory;
        private GameObject worldPicker;
        private Text selectedWorld;
        private Button worldPickerButton,hostButton;
        private SaveStore describedStore;
        private bool worldDescriptionDirty=true;
        private readonly Dictionary<string,RectTransform> worldChoices=new();
        public RectTransform WorldPickerButtonRect=>worldPickerButton.GetComponent<RectTransform>();
        public RectTransform HostButtonRect=>hostButton.GetComponent<RectTransform>();
        public RectTransform WorldChoiceRect(string id)=>worldChoices[id];
        private InputField address;
        private InputField character;
        public Func<string,bool> SelectCharacter { get; set; }
        private readonly Button[] slots = new Button[9];
        private readonly Image[] slotIcons=new Image[9];
        private InventoryArtwork inventoryArtwork;
        private Text containerTitle;
        private ContainerHint containerHint;
        private Button claimQuantity;
        private bool claimOne;
        public RectTransform ClaimQuantityRect=>claimQuantity.GetComponent<RectTransform>();
        private InventorySource shownContainer;
        public string ShownInventorySourceId=>shownContainer?.id;
        private int containerPage;
        private Button previousContainer,nextContainer;
        public int ContainerPage=>containerPage;
        public RectTransform ContainerNextRect=>nextContainer.GetComponent<RectTransform>();
        private Font font;
        private HudElements elements;
        public WorldLobbyPanel NetworkLobby {get;private set;}
        public CharacterContentPanel CharacterContent {get;private set;}
        public SessionModPanel SessionMods {get;private set;}
        public PauseMenu PauseMenu {get;private set;}
        public AudioSettingsPanel AudioSettings {get;private set;}
        public RectTransform MainMenuSoundRect=>mainMenu.SoundRect;
        public bool BlocksGameplayInput=>inventory.activeSelf || PauseMenu.IsOpen || AudioSettings.IsOpen;
        public bool InventoryOpen=>inventory.activeSelf;
        private string inventoryActorVersion;
        private CombatHud combatHud;
        private HudFeedbackView feedback;
        private GameObject quickActions;
        private float noticeUntil;
        private PersonalInventoryPanel personalItems;
        public InventoryCategory PersonalCategory=>personalItems.Category;
        public RectTransform PersonalCategoryRect(InventoryCategory category)=>personalItems.CategoryRect(category);
        private InventoryWindow inventoryWindow;
        private InventoryAudio inventoryAudio;
        public RectTransform PersonalTabRect=>inventoryWindow.PersonalTab;
        public RectTransform ContainerTabRect=>inventoryWindow.SharedTab;
        public RectTransform DepositRowRect(int index)=>personalItems.RowRect(index);
        public int PersonalPage=>personalItems.Page;
        public int PersonalPageSize=>personalItems.PageSize;
        public RectTransform PersonalNextRect=>personalItems.NextRect;
        public RectTransform PersonalPreviousRect=>personalItems.PreviousRect;
        public RectTransform DepositActionRect=>personalItems.DepositRect;
        public RectTransform UseItemRect=>personalItems.UseRect;
        public RectTransform InventoryWeaponRect(int index)=>personalItems.WeaponRect(index);
        public Action<string> EquipWeapon {get;set;}
        public Action<string> EquipWeaponItem {get;set;}
        private Text revival;
        public Func<float> RevivalProgress {get;set;}
        public Func<bool> RevivalPending {get;set;}
        public Func<float?> ReloadRemaining {get;set;}
        private Button friendlyFire;
        private Button friendlyDecision;
        private Button leave;
        private Button factory;
        public event Action HostRequested;
        public event Action<string> JoinRequested;

        public void Initialize(CoopSession value,AudioPreferences audioPreferences=null)
        {
            session = value;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Не найден системный шрифт Unity LegacyRuntime.ttf.");
            elements=new HudElements(font);
            inventoryArtwork=new InventoryArtwork();
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            containerHint=new ContainerHint(transform,elements);
            audioPreferences??=new AudioPreferences(null);
            inventoryAudio=gameObject.AddComponent<InventoryAudio>();inventoryAudio.Initialize(audioPreferences);
            AudioSettings=new AudioSettingsPanel(transform,audioPreferences);
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            var sessionHud=SessionHudView.Create(transform);status=sessionHud.Status;
            combatHud=new CombatHud(transform,weapon=>EquipWeapon?.Invoke(weapon),item=>EquipWeaponItem?.Invoke(item));
            quickActions=sessionHud.gameObject;
            feedback=HudFeedbackView.Create(transform);message=feedback.Notice;revival=feedback.Revival;
            mainMenu=new MainMenuPanel(transform,
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) HostRequested?.Invoke();},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) JoinRequested?.Invoke(address.text);},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) ShowWorldPicker(0);},()=>AudioSettings.Open(),()=>
                {
                    if(SelectCharacter?.Invoke(character.text)!=true) return;
                    try {new WorldLibrary(session.Saves).PrepareNewGame();RefreshSelectedWorld();HostRequested?.Invoke();}
                    catch(Exception error) {ShowMessage("Не удалось создать новую игру: "+error.Message);Debug.LogError(error);}
                });
            lobby=mainMenu.Controls;selectedWorld=mainMenu.World;worldPickerButton=mainMenu.Worlds;hostButton=mainMenu.Host;
            character=mainMenu.Character;address=mainMenu.Address;character.text=session.CharacterId??"pony-one";
            character.onEndEdit.AddListener(name=>{if(SelectCharacter?.Invoke(name)==true) RefreshSelectedWorld();});
            var inventoryView=InventoryWindowView.Create(transform);inventory=inventoryView.gameObject;
            var containerView=inventoryView.SharedContents;
            containerTitle=containerView.Title;
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                slots[i]=containerView.Slots[i];slotIcons[i]=containerView.Icons[i];
                slots[i].onClick.AddListener(()=>
                {
                    if (shownContainer!=null && containerPage*9+index < shownContainer.items.Count)
                        session.RequestClaim(shownContainer.id,shownContainer.items[containerPage*9+index].id,claimOne?1:0);
                });
            }
            claimQuantity=inventoryView.Quantity;
            claimQuantity.onClick.AddListener(()=>
            {claimOne=!claimOne;claimQuantity.GetComponentInChildren<Text>().text=claimOne?"Количество: 1 предмет":"Количество: вся стопка";Refresh();});
            previousContainer=containerView.Previous;nextContainer=containerView.Next;
            previousContainer.onClick.AddListener(()=>{containerPage--;Refresh();});
            nextContainer.onClick.AddListener(()=>{containerPage++;Refresh();});
            inventory.SetActive(false);
            personalItems=new PersonalInventoryPanel(inventoryView.PersonalContents,session,ItemName,()=>claimOne?1:0,weapon=>EquipWeapon?.Invoke(weapon),inventoryArtwork,item=>EquipWeaponItem?.Invoke(item));
            inventoryWindow=new InventoryWindow(inventoryView,ToggleInventory,inventoryAudio.Page);
            var pauseView=PauseMenuView.Create(transform);
            friendlyFire=pauseView.FriendlyFire;friendlyFire.onClick.AddListener(()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=pauseView.FriendlyDecision;friendlyDecision.onClick.AddListener(()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            leave=pauseView.Leave;leave.onClick.AddListener(()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            factory=sessionHud.Travel;factory.onClick.AddListener(()=>
            {if(session.LocalActor?.level=="factory:0") session.Travel.RequestHub();else session.ReturnToFactory();});
            factory.gameObject.SetActive(false);
            session.Changed += Refresh;
            session.Notice += ShowMessage;
            SessionMods=new SessionModPanel(transform,elements,session);
            NetworkLobby=new WorldLobbyPanel(transform,session,Refresh);
            CharacterContent=new CharacterContentPanel(transform,elements,session);
            PauseMenu=new PauseMenu(pauseView,session,()=>inventory.activeSelf,()=>AudioSettings.Open());
            sessionHud.Inventory.onClick.AddListener(ToggleInventory);
            sessionHud.Menu.onClick.AddListener(()=>PauseMenu.Toggle());
            Refresh();
        }

        public void ToggleMenu() {if(AudioSettings.IsOpen) AudioSettings.Cancel();else PauseMenu.Toggle();}
        public void ToggleInventory() {if(AudioSettings.IsOpen) return; inventoryAudio.Toggle();inventory.SetActive(!inventory.activeSelf);if(inventory.activeSelf) inventory.transform.SetAsLastSibling();Refresh();PauseMenu.PublishIntent(); }
        public RectTransform FactoryButtonRect => factory.GetComponent<RectTransform>();
        public RectTransform FriendlyFireButtonRect=>friendlyFire.GetComponent<RectTransform>();
        public RectTransform FriendlyDecisionButtonRect=>friendlyDecision.GetComponent<RectTransform>();
        public bool FriendlyDecisionInteractable=>friendlyDecision.interactable;
        public RectTransform ContainerSlotRect(int index)=>slots[index].GetComponent<RectTransform>();
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
            if(inventoryActorVersion!=version) {inventoryActorVersion=version;inventory.SetActive(false);}
            bool gameplayVisible=actor!=null && !inventory.activeSelf;
            status.transform.parent.gameObject.SetActive(gameplayVisible);
            combatHud.Refresh(actor,gameplayVisible && !PauseMenu.IsOpen,ReloadRemaining?.Invoke());quickActions.SetActive(gameplayVisible && !PauseMenu.IsOpen);
            LayoutNotice();
            friendlyDecision.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            if (actor == null)
            {containerHint.Refresh(null,false);lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld(false);inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);factory.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
            worldDescriptionDirty=true;
            lobby.SetActive(false);
            leave.gameObject.SetActive(gameplayVisible);friendlyFire.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            factory.gameObject.SetActive(gameplayVisible && (actor.level=="hub" || actor.level=="factory:0"));
            factory.GetComponentInChildren<Text>().text=actor.level=="hub"?"На завод 0":"Вернуться в хаб";
            friendlyFire.GetComponentInChildren<Text>().text="Дружественный огонь: "+(session.World.friendlyFire?"включён":"выключен");
            friendlyDecision.interactable=session.World.friendlyFire;
            friendlyDecision.GetComponentInChildren<Text>().text="Прямое попадание:\nрешает "+(session.World.friendlyFireDecision==FriendlyFireDecision.Attacker?"атакующий":"пострадавший");
            status.text=$"{(actor.level=="hub"?"Хаб":"Завод 0")} · игроков: {session.World.actors.FindAll(a=>a.player && a.connected).Count}";

            var nearest=ContainerAccess.Nearest(session.World,actor,session.Rules.Loot,session.Rules.Deposit);
            if(nearest?.id!=shownContainer?.id) containerPage=0;
            shownContainer=nearest;
            containerHint.Refresh(shownContainer==null?null:session.World.containers.Find(c=>c.id==shownContainer.id),
                gameplayVisible && !PauseMenu.IsOpen && !AudioSettings.IsOpen,actor);
            int pages=Math.Max(1,((shownContainer?.items.Count??0)+8)/9);
            containerPage=Mathf.Clamp(containerPage,0,pages-1);
            previousContainer.interactable=containerPage>0;nextContainer.interactable=containerPage<pages-1;
            personalItems.Refresh(shownContainer);
            containerTitle.text=shownContainer==null?"Рядом нет контейнера": $"{(shownContainer.Body==null?$"Контейнер · {shownContainer.Mass:0.#} кг":"Тело")} · {containerPage+1}/{pages}";
            if(shownContainer!=null && session.World.pendingGrants.Exists(g=>g.containerId==shownContainer.id && g.recipient==actor.id))
                containerTitle.text="Подбор подтверждается…";
            for (int i = 0; i < slots.Length; i++)
            {
                var item = shownContainer!=null && containerPage*9+i < shownContainer.items.Count ? shownContainer.items[containerPage*9+i] : null;
                slots[i].GetComponentInChildren<Text>().text = item == null ? "Пусто" : $"{ItemName(item.definition)}\n{item.count}";
                inventoryArtwork.Set(slotIcons[i],item?.definition);
                slots[i].interactable = item != null && ContainerAccess.ClaimRejection(actor,shownContainer,item,session.Rules.Loot,claimOne?1:0)==null;
            }
        }
        private void RefreshSelectedWorld(bool force=true)
        {
            if(!force && !worldDescriptionDirty && ReferenceEquals(describedStore,session.Saves)) return;
            describedStore=session.Saves;worldDescriptionDirty=false;
            var saved=session.Saves.LoadWorld(prepareForPlay:false);
            hostButton.GetComponentInChildren<Text>().text=saved==null?"Начать в новом мире":"Продолжить выбранный мир";
            selectedWorld.text=saved==null?"Сохранённый мир не выбран.\n«Миры и сохранения»: выбор, имя и удаление.":
                MainMenuPanel.DescribeWorld(new WorldLibrary(session.Saves).Name(saved.worldId),saved.branchId,saved.revision);
        }

        private void ShowWorldPicker(int page)
        {
            RefreshSelectedWorld();
            if(worldPicker!=null) Destroy(worldPicker);
            worldChoices.Clear();
            var copies=session.Saves.Continuations();
            var selected=session.Saves.SelectedSnapshot();
            var view=WorldPickerView.Create(transform);worldPicker=view.gameObject;
            int capacity=view.Choices.Length;
            int pages=Math.Max(1,(copies.Count+capacity-1)/capacity);page=Math.Clamp(page,0,pages-1);
            for(int row=0;row<capacity;row++)
            {
                int index=page*capacity+row;bool visible=index<copies.Count;
                view.Choices[row].gameObject.SetActive(visible);view.Manage[row].gameObject.SetActive(visible);
                if(!visible) continue;
                var copy=copies[index];var state=copy.CreateLiveWorld();
                string relation=selected==null?"Сохранённая копия":WorldHistory.Description(
                    WorldHistory.Compare(state,copy.Id,selected.CreateLiveWorld(),selected.Id));
                var choice=view.Choices[row];
                view.ShowChoice(row,new WorldLibrary(session.Saves).Name(state.worldId),state.branchId,state.revision,relation,selected?.Id==copy.Id);
                choice.onClick.AddListener(()=>
                {
                    try {session.Saves.SelectContinuation(copy);Destroy(worldPicker);RefreshSelectedWorld();ShowMessage("");}
                    catch(Exception error)
                    {
                        // Return to the menu so its notice is not obscured by the picker.
                        Destroy(worldPicker);
                        ShowMessage("Не удалось выбрать мир: сохранение недоступно или повреждено.\nПодробности — в журнале игры. Можно выбрать другой мир.");
                        Debug.LogError($"Выбор мира {state.worldId}, снимок {copy.Id}: {error}");
                    }
                });
                worldChoices[copy.Id]=choice.GetComponent<RectTransform>();
                view.Manage[row].onClick.AddListener(()=>
                    WorldManagementPanel.Open(transform,session.Saves,state.worldId,()=>{ShowWorldPicker(page);RefreshSelectedWorld();}));
            }
            view.Empty.gameObject.SetActive(copies.Count==0);
            view.Previous.onClick.AddListener(()=>ShowWorldPicker(page-1));view.Previous.interactable=page>0;
            view.Page.text=$"{page+1} / {pages}";
            view.Next.onClick.AddListener(()=>ShowWorldPicker(page+1));view.Next.interactable=page+1<pages;
            view.Close.onClick.AddListener(()=>Destroy(worldPicker));
        }

        private static string ItemName(string id)
        {
            switch(id)
            {
                case "spear":return "Копьё";
                case "crowbar":return "Лом";
                case "pistol":return "10-мм пистолет";
                case "energy":return "Лазерный пистолет";
                case "caps":return "Крышки";
                case "heal":return "Аптечка";
                case "grenade":return "Граната";
                case "p10":return "10-мм патроны";
                case "batt":return "Энергобатареи";
                default:Debug.LogWarning($"Нет русской подписи предмета в инвентаре: {id}.");return "["+id+"]";
            }
        }

        private void Update()
        {
            if(session.LocalActor==null && AudioSettings.IsOpen && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true) AudioSettings.Cancel();
            factory.interactable=session.LocalActor is {down:false} && !session.Travel.Pending;
            if(ContainerAccess.Nearest(session.World,session.LocalActor,session.Rules.Loot,session.Rules.Deposit)?.id!=shownContainer?.id) Refresh();
            bool show=session.LocalActor!=null && !inventory.activeSelf && !PauseMenu.IsOpen;
            containerHint.Refresh(shownContainer==null?null:session.World?.containers.Find(c=>c.id==shownContainer.id),show && !AudioSettings.IsOpen,session.LocalActor);
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




