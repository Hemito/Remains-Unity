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
        private GameObject quickActions;
        private float noticeUntil;
        private PersonalInventoryPanel personalItems;
        private InventoryWindow inventoryWindow;
        private InventoryAudio inventoryAudio;
        public RectTransform PersonalTabRect=>inventoryWindow.PersonalTab;
        public RectTransform ContainerTabRect=>inventoryWindow.SharedTab;
        public RectTransform DepositRowRect(int index)=>personalItems.RowRect(index);
        public RectTransform DepositActionRect=>personalItems.DepositRect;
        public RectTransform UseItemRect=>personalItems.UseRect;
        public RectTransform InventoryWeaponRect(int index)=>personalItems.WeaponRect(index);
        public Action<string> EquipWeapon {get;set;}
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
            AudioSettings=new AudioSettingsPanel(transform,elements,audioPreferences);
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            status=elements.BackedLabel(transform,"",new Vector2(720,-20),new Vector2(285,44));
            combatHud=new CombatHud(transform,weapon=>EquipWeapon?.Invoke(weapon));
            quickActions=Panel(transform,"Действия",new Vector2(1020,-14),new Vector2(244,96));
            message=elements.BackedLabel(transform,"",new Vector2(350,-140),new Vector2(880,55));
            revival=elements.BackedLabel(transform,"",new Vector2(480,-625),new Vector2(750,35));
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
            personalItems=new PersonalInventoryPanel(inventoryView.PersonalContents,session,ItemName,()=>claimOne?1:0,weapon=>EquipWeapon?.Invoke(weapon),inventoryArtwork);
            inventoryWindow=new InventoryWindow(inventoryView,ToggleInventory,inventoryAudio.Page);
            var pauseView=PauseMenuView.Create(transform);
            friendlyFire=pauseView.FriendlyFire;friendlyFire.onClick.AddListener(()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=pauseView.FriendlyDecision;friendlyDecision.onClick.AddListener(()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            leave=pauseView.Leave;leave.onClick.AddListener(()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            factory=Button(quickActions.transform,"На завод 0",new Vector2(8,-8),new Vector2(228,36),()=>
            {if(session.LocalActor?.level=="factory:0") session.Travel.RequestHub();else session.ReturnToFactory();});
            factory.gameObject.SetActive(false);
            session.Changed += Refresh;
            session.Notice += ShowMessage;
            SessionMods=new SessionModPanel(transform,elements,session);
            NetworkLobby=new WorldLobbyPanel(transform,elements,session,Refresh);
            CharacterContent=new CharacterContentPanel(transform,elements,session);
            PauseMenu=new PauseMenu(pauseView,session,()=>inventory.activeSelf,()=>AudioSettings.Open());
            Button(quickActions.transform,"Инвентарь · I",new Vector2(8,-52),new Vector2(112,36),ToggleInventory).GetComponentInChildren<Text>().fontSize=14;
            Button(quickActions.transform,"Меню · Esc",new Vector2(126,-52),new Vector2(110,36),()=>PauseMenu.Toggle()).GetComponentInChildren<Text>().fontSize=14;
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
            // Text measurement runs on changes, never in the per-frame HUD refresh.
            float width=Mathf.Clamp(message.preferredWidth,220,880);
            message.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
            float height=Mathf.Max(26,message.preferredHeight);
            var panel=(RectTransform)message.transform.parent;
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(.5f,1);
            panel.anchoredPosition=new Vector2(0,inventory.activeSelf?-16:-140);
            panel.sizeDelta=new Vector2(width+24,height+16);
            HudElements.Rect(message.rectTransform,new Vector2(12,-8),new Vector2(width,height));
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
            {containerHint.Refresh(null,false);lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld();inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);factory.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
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
                gameplayVisible && !PauseMenu.IsOpen && !AudioSettings.IsOpen);
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
        private void RefreshSelectedWorld()
        {
            var saved=session.Saves.LoadWorld(prepareForPlay:false);
            hostButton.GetComponentInChildren<Text>().text=saved==null?"Начать игру / создать сессию":"Продолжить / создать сессию";
            selectedWorld.text=saved==null?"Будет создан новый мир":
                $"{new WorldLibrary(session.Saves).Name(saved.worldId)} · ревизия {saved.revision}\nВетвь {saved.branchId.Substring(0,8)} · "+
                (saved.historyVersion==1?$"снимок {saved.checkpointSequence}":"история не подтверждена");
        }

        private void ShowWorldPicker(int page)
        {
            if(worldPicker!=null) Destroy(worldPicker);
            worldChoices.Clear();
            var copies=session.Saves.Continuations();
            var selected=session.Saves.SelectedSnapshot();
            worldPicker=Panel(transform,"Продолжения миров",new Vector2(170,-90),new Vector2(940,510));
            Label(worldPicker.transform,"Выберите целую копию мира. Другие ветви сохранятся; объединения нет.",new Vector2(20,-15),new Vector2(900,50));
            int pages=Math.Max(1,(copies.Count+4)/5);page=Math.Clamp(page,0,pages-1);
            for(int row=0;row<5 && page*5+row<copies.Count;row++)
            {
                var copy=copies[page*5+row];var state=copy.CreateLiveWorld();
                string relation=selected==null?"Сохранённая копия":WorldHistory.Description(
                    WorldHistory.Compare(state,copy.Id,selected.CreateLiveWorld(),selected.Id));
                var choice=Button(worldPicker.transform,$"{new WorldLibrary(session.Saves).Name(state.worldId)} · ветвь {state.branchId.Substring(0,8)} · ревизия {state.revision}\n{relation}",
                    new Vector2(20,-75-row*70),new Vector2(680,62),()=>
                    {session.Saves.SelectContinuation(copy);Destroy(worldPicker);RefreshSelectedWorld();});
                worldChoices[copy.Id]=choice.GetComponent<RectTransform>();
                Button(worldPicker.transform,"Имя / удалить",new Vector2(710,-75-row*70),new Vector2(210,62),()=>
                    WorldManagementPanel.Open(transform,elements,session.Saves,state.worldId,()=>{ShowWorldPicker(page);RefreshSelectedWorld();}));
            }
            if(copies.Count==0) Label(worldPicker.transform,"Сохранённых миров пока нет.",new Vector2(20,-90),new Vector2(850,50));
            Button(worldPicker.transform,"Назад",new Vector2(20,-440),new Vector2(180,45),()=>ShowWorldPicker(page-1)).interactable=page>0;
            Label(worldPicker.transform,$"{page+1} / {pages}",new Vector2(220,-440),new Vector2(160,45));
            Button(worldPicker.transform,"Далее",new Vector2(410,-440),new Vector2(180,45),()=>ShowWorldPicker(page+1)).interactable=page+1<pages;
            Button(worldPicker.transform,"Закрыть",new Vector2(730,-440),new Vector2(190,45),()=>Destroy(worldPicker));
        }

        private static string ItemName(string id)
        {
            switch(id)
            {
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
            containerHint.Refresh(shownContainer==null?null:session.World?.containers.Find(c=>c.id==shownContainer.id),show && !AudioSettings.IsOpen);
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




