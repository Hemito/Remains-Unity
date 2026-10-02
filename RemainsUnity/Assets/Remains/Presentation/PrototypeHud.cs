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
        public RectTransform DepositRowRect(int index)=>personalItems.RowRect(index);
        public RectTransform DepositActionRect=>personalItems.DepositRect;
        public RectTransform UseItemRect=>personalItems.UseRect;
        public RectTransform InventoryWeaponRect(int index)=>personalItems.WeaponRect(index);
        public Action<string> EquipWeapon {get;set;}
        private Text revival;
        public Func<float> RevivalProgress {get;set;}
        public Func<bool> RevivalPending {get;set;}
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
            AudioSettings=new AudioSettingsPanel(transform,elements,audioPreferences??new AudioPreferences(null));
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            status=elements.BackedLabel(transform,"",new Vector2(720,-20),new Vector2(285,44));
            combatHud=new CombatHud(transform,elements,inventoryArtwork,weapon=>EquipWeapon?.Invoke(weapon));
            quickActions=Panel(transform,"Действия",new Vector2(1020,-14),new Vector2(244,96));
            message=elements.BackedLabel(transform,"",new Vector2(350,-140),new Vector2(880,55));
            revival=elements.BackedLabel(transform,"",new Vector2(480,-625),new Vector2(750,35));
            mainMenu=new MainMenuPanel(transform,elements,
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) HostRequested?.Invoke();},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) JoinRequested?.Invoke(address.text);},
                ()=>{if(SelectCharacter?.Invoke(character.text)==true) ShowWorldPicker(0);},()=>AudioSettings.Open());
            lobby=mainMenu.Controls;selectedWorld=mainMenu.World;worldPickerButton=mainMenu.Worlds;hostButton=mainMenu.Host;
            character=mainMenu.Character;address=mainMenu.Address;character.text=session.CharacterId??"pony-one";
            character.onEndEdit.AddListener(name=>{if(SelectCharacter?.Invoke(name)==true) RefreshSelectedWorld();});
            inventory = Panel(transform,"Инвентарь",new Vector2(20,-110),new Vector2(1240,580));
            var inventoryRect=inventory.GetComponent<RectTransform>();inventoryRect.anchorMin=inventoryRect.anchorMax=inventoryRect.pivot=new Vector2(.5f,.5f);inventoryRect.anchoredPosition=new Vector2(0,-40);
            inventory.GetComponent<Image>().color=new Color(.015f,.025f,.02f,1);
            var containerPanel=Panel(inventory.transform,"Общий контейнер",new Vector2(870,-72),new Vector2(330,435));
            containerTitle=Label(containerPanel.transform,"Общий контейнер · 3 × 3",new Vector2(12,-12),new Vector2(305,40));
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                slots[i] = Button(containerPanel.transform,"Пусто",new Vector2(12+i%3*105,-60-i/3*95),new Vector2(98,86),()=>
                {
                    if (shownContainer!=null && containerPage*9+index < shownContainer.items.Count)
                        session.RequestClaim(shownContainer.id,shownContainer.items[containerPage*9+index].id,claimOne?1:0);
                });
                slotIcons[i]=inventoryArtwork.Create(slots[i].transform,new Vector2(33,-5),new Vector2(32,30));
                var slotLabel=slots[i].GetComponentInChildren<Text>();slotLabel.fontSize=16;
                HudElements.Rect(slotLabel.rectTransform,new Vector2(5,-38),new Vector2(88,43));
            }
            claimQuantity=Button(containerPanel.transform,"Количество: вся стопка",new Vector2(12,-350),new Vector2(305,32),()=>
            {claimOne=!claimOne;claimQuantity.GetComponentInChildren<Text>().text=claimOne?"Количество: 1 предмет":"Количество: вся стопка";Refresh();});
            previousContainer=Button(containerPanel.transform,"←",new Vector2(12,-390),new Vector2(65,32),()=>{containerPage--;Refresh();});
            nextContainer=Button(containerPanel.transform,"→",new Vector2(252,-390),new Vector2(65,32),()=>{containerPage++;Refresh();});
            Button(inventory.transform,"Закрыть · I",new Vector2(1010,-522),new Vector2(190,40),ToggleInventory);
            inventory.SetActive(false);
            var personalPanel=Panel(inventory.transform,"Личный инвентарь",new Vector2(20,-20),new Vector2(820,530));
            ApplyOriginalImage(personalPanel.GetComponent<Image>(),"inventory-panel",false);
            personalItems=new PersonalInventoryPanel(personalPanel.transform,elements,session,ItemName,()=>claimOne?1:0,weapon=>EquipWeapon?.Invoke(weapon),inventoryArtwork);
            friendlyFire=Button(transform,"Дружественный огонь",new Vector2(20,-270),new Vector2(290,45),()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=Button(transform,"Решение о попадании",new Vector2(20,-320),new Vector2(290,60),()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            leave=Button(transform,"Сохранить и выйти из сессии",new Vector2(20,-390),new Vector2(290,45),()=>
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
            PauseMenu=new PauseMenu(transform,elements,session,()=>inventory.activeSelf,()=>AudioSettings.Open());
            friendlyFire.transform.SetParent(PauseMenu.SessionControls,false);HudElements.Rect(friendlyFire.GetComponent<RectTransform>(),new Vector2(12,-50),new Vector2(286,40));
            friendlyDecision.transform.SetParent(PauseMenu.SessionControls,false);HudElements.Rect(friendlyDecision.GetComponent<RectTransform>(),new Vector2(12,-100),new Vector2(286,60));
            leave.transform.SetParent(PauseMenu.SessionControls,false);HudElements.Rect(leave.GetComponent<RectTransform>(),new Vector2(12,-185),new Vector2(286,60));
            Button(quickActions.transform,"Инвентарь · I",new Vector2(8,-52),new Vector2(112,36),ToggleInventory).GetComponentInChildren<Text>().fontSize=14;
            Button(quickActions.transform,"Меню · Esc",new Vector2(126,-52),new Vector2(110,36),()=>PauseMenu.Toggle()).GetComponentInChildren<Text>().fontSize=14;
            Refresh();
        }

        public void ToggleMenu() {if(AudioSettings.IsOpen) AudioSettings.Cancel();else PauseMenu.Toggle();}
        public void ToggleInventory() {if(AudioSettings.IsOpen) return; inventory.SetActive(!inventory.activeSelf);if(inventory.activeSelf) inventory.transform.SetAsLastSibling();Refresh();PauseMenu.PublishIntent(); }
        public RectTransform FactoryButtonRect => factory.GetComponent<RectTransform>();
        public RectTransform FriendlyFireButtonRect=>friendlyFire.GetComponent<RectTransform>();
        public RectTransform FriendlyDecisionButtonRect=>friendlyDecision.GetComponent<RectTransform>();
        public bool FriendlyDecisionInteractable=>friendlyDecision.interactable;
        public RectTransform ContainerSlotRect(int index)=>slots[index].GetComponent<RectTransform>();
        public void ShowMessage(string text) {noticeUntil=Time.unscaledTime+8;message.text=text;if(mainMenu!=null) mainMenu.Notice.text=text;}
        private void Refresh()
        {
            var actor = session.LocalActor;
            mainMenu.Root.SetActive(actor==null);
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(inventoryActorVersion!=version) {inventoryActorVersion=version;inventory.SetActive(false);}
            bool gameplayVisible=actor!=null && !inventory.activeSelf;
            status.transform.parent.gameObject.SetActive(gameplayVisible);
            combatHud.Refresh(actor,gameplayVisible && !PauseMenu.IsOpen);quickActions.SetActive(gameplayVisible && !PauseMenu.IsOpen);
            Vector2 noticePosition=inventory.activeSelf?new Vector2(20,-20):new Vector2(350,-140);
            Vector2 noticeSize=inventory.activeSelf?new Vector2(1240,70):new Vector2(880,55);
            HudElements.Rect(message.transform.parent.GetComponent<RectTransform>(),noticePosition+new Vector2(-6,6),noticeSize+new Vector2(12,12));
            HudElements.Rect(message.rectTransform,new Vector2(6,-6),noticeSize);
            friendlyDecision.gameObject.SetActive(gameplayVisible && Mirror.NetworkServer.active);
            if (actor == null)
            {lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld();inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);factory.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
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
            selectedWorld.text=saved==null?"Будет создан новый мир":
                $"Выбран мир {saved.worldId.Substring(0,8)} · ревизия {saved.revision}\nВетвь {saved.branchId.Substring(0,8)} · "+
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
                var choice=Button(worldPicker.transform,$"Мир {state.worldId.Substring(0,8)} · ветвь {state.branchId.Substring(0,8)} · ревизия {state.revision}\n{relation}",
                    new Vector2(20,-75-row*70),new Vector2(900,62),()=>
                    {session.Saves.SelectContinuation(copy);Destroy(worldPicker);RefreshSelectedWorld();});
                worldChoices[copy.Id]=choice.GetComponent<RectTransform>();
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
            if(inventory.activeSelf && ContainerAccess.Nearest(session.World,session.LocalActor,session.Rules.Loot,session.Rules.Deposit)?.id!=shownContainer?.id) Refresh();
            bool show=session.LocalActor!=null && !inventory.activeSelf && !PauseMenu.IsOpen;
            combatHud.Refresh(session.LocalActor,show);quickActions.SetActive(show);
            status.transform.parent.gameObject.SetActive(show);
            message.transform.parent.gameObject.SetActive(session.LocalActor!=null && Time.unscaledTime<noticeUntil && !string.IsNullOrEmpty(message.text));
            if(revival==null) return;
            float progress=RevivalProgress?.Invoke() ?? 0;
            revival.text=RevivalPending?.Invoke()==true?"Ожидаем подтверждения поднятия…":
                progress>0?$"Поднимаем союзника: {progress:P0} · удерживайте E":"";
            revival.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(revival.text));
        }
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
