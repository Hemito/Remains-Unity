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
        private Text containerTitle;
        private Button claimQuantity;
        private bool claimOne;
        public RectTransform ClaimQuantityRect=>claimQuantity.GetComponent<RectTransform>();
        private ContainerState shownContainer;
        private Font font;
        private HudElements elements;
        public WorldLobbyPanel NetworkLobby {get;private set;}
        public CharacterContentPanel CharacterContent {get;private set;}
        public PauseMenu PauseMenu {get;private set;}
        public bool BlocksGameplayInput=>inventory.activeSelf || PauseMenu.IsOpen;
        public bool InventoryOpen=>inventory.activeSelf;
        private string inventoryActorVersion;
        private Image healthBar;
        private Image manaBar;
        private Text personalItems;
        private Text ammunition;
        private Text revival;
        public Func<float> RevivalProgress {get;set;}
        public Func<bool> RevivalPending {get;set;}
        private readonly HashSet<string> missingMagazines=new();
        private Button friendlyFire;
        private Button friendlyDecision;
        private Button leave;
        private Button factory;
        public event Action HostRequested;
        public event Action<string> JoinRequested;

        public void Initialize(CoopSession value)
        {
            session = value;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Не найден системный шрифт Unity LegacyRuntime.ttf.");
            elements=new HudElements(font);
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            status = Label(transform, "Подключение не установлено", new Vector2(20,-20), new Vector2(1050,60));
            healthBar=OriginalBar("health",new Vector2(20,-95),new Vector2(300,34));
            manaBar=OriginalBar("mana",new Vector2(20,-135),new Vector2(300,20));
            ammunition=Label(transform,"",new Vector2(20,-165),new Vector2(380,45));
            message = Label(transform, "A/D — ходьба · Shift — бег · Пробел — прыжок · W/S — лестница · ЛКМ — атака · Удерживать E — поднять · I — инвентарь", new Vector2(20,-650), new Vector2(1230,55));
            revival=Label(transform,"",new Vector2(420,-605),new Vector2(800,40));
            lobby = Panel(transform, "Подключение", new Vector2(420,-240), new Vector2(440,300));
            selectedWorld=Label(lobby.transform,"",new Vector2(0,95),new Vector2(720,90));
            worldPickerButton=Button(lobby.transform,"Выбрать сохранённое продолжение",new Vector2(20,-250),new Vector2(400,40),()=>
            {if(SelectCharacter?.Invoke(character.text)==true) ShowWorldPicker(0);});
            character=Input(lobby.transform,new Vector2(20,-20));character.text=session.CharacterId??"pony-one";
            character.onEndEdit.AddListener(name=>{if(SelectCharacter?.Invoke(name)==true) RefreshSelectedWorld();});
            hostButton=Button(lobby.transform,"Создать игру",new Vector2(20,-80),new Vector2(400,45),()=>
            {if(SelectCharacter?.Invoke(character.text)==true) HostRequested?.Invoke();});
            address = Input(lobby.transform, new Vector2(20,-140));
            Button(lobby.transform,"Подключиться",new Vector2(20,-200),new Vector2(400,45),()=>
            {if(SelectCharacter?.Invoke(character.text)==true) JoinRequested?.Invoke(address.text);});
            inventory = Panel(transform,"Общий контейнер",new Vector2(920,-130),new Vector2(330,390));
            containerTitle=Label(inventory.transform,"Общий контейнер · 3 × 3",new Vector2(12,-12),new Vector2(305,40));
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                slots[i] = Button(inventory.transform,"Пусто",new Vector2(12+i%3*105,-60-i/3*95),new Vector2(98,86),()=>
                {
                    if (shownContainer!=null && index < shownContainer.items.Count)
                        session.RequestClaim(shownContainer.id,shownContainer.items[index].id,claimOne?1:0);
                });
            }
            claimQuantity=Button(inventory.transform,"Брать: всю стопку",new Vector2(12,-350),new Vector2(305,32),()=>
            {claimOne=!claimOne;claimQuantity.GetComponentInChildren<Text>().text=claimOne?"Брать: по одной единице":"Брать: всю стопку";Refresh();});
            inventory.SetActive(false);
            var personalPanel=Panel(inventory.transform,"Личный инвентарь",new Vector2(-380,0),new Vector2(360,390));
            ApplyOriginalImage(personalPanel.GetComponent<Image>(),"inventory-panel",false);
            personalItems=Label(personalPanel.transform,"",new Vector2(20,-20),new Vector2(320,340));
            friendlyFire=Button(transform,"Дружественный огонь",new Vector2(20,-550),new Vector2(290,45),()=>session.SetFriendlyFire(!session.World.friendlyFire));
            friendlyDecision=Button(transform,"Решение о попадании",new Vector2(320,-550),new Vector2(450,45),()=>
                session.SetFriendlyFireDecision(session.World.friendlyFireDecision==FriendlyFireDecision.Victim?FriendlyFireDecision.Attacker:FriendlyFireDecision.Victim));
            friendlyDecision.gameObject.SetActive(false);
            leave=Button(transform,"Сохранить и выйти из сессии",new Vector2(20,-600),new Vector2(290,45),()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            factory=Button(transform,"На завод 0",new Vector2(20,-500),new Vector2(290,45),()=>
            {if(session.LocalActor?.level=="factory:0") session.Travel.RequestHub();else session.ReturnToFactory();});
            factory.gameObject.SetActive(false);
            session.Changed += Refresh;
            session.Notice += text => message.text = text;
            session.ModMismatch+=hostMods=>
            {
                var warning=Panel(transform,"Моды не совпадают",new Vector2(240,-160),new Vector2(800,430));
                Label(warning.transform,$"Моды или их порядок не совпадают. Продолжить на свой риск?\n\nУ вас:\n{WeaponCatalog.ModDescription}\n\nУ хоста:\n{hostMods}",new Vector2(20,-20),new Vector2(750,320));
                Button(warning.transform,"Войти",new Vector2(20,-350),new Vector2(360,50),()=>{session.SendJoin(true);Destroy(warning);});
                Button(warning.transform,"Отмена",new Vector2(410,-350),new Vector2(360,50),()=>{session.StopClient();Destroy(warning);});
            };
            NetworkLobby=new WorldLobbyPanel(transform,elements,session,Refresh);
            CharacterContent=new CharacterContentPanel(transform,elements,session);
            PauseMenu=new PauseMenu(transform,elements,session,()=>inventory.activeSelf);
            Refresh();
        }

        public void ToggleInventory() { inventory.SetActive(!inventory.activeSelf); Refresh();PauseMenu.PublishIntent(); }
        public RectTransform FactoryButtonRect => factory.GetComponent<RectTransform>();
        public RectTransform FriendlyFireButtonRect=>friendlyFire.GetComponent<RectTransform>();
        public RectTransform FriendlyDecisionButtonRect=>friendlyDecision.GetComponent<RectTransform>();
        public bool FriendlyDecisionInteractable=>friendlyDecision.interactable;
        public RectTransform ContainerSlotRect(int index)=>slots[index].GetComponent<RectTransform>();
        public void ShowMessage(string text)=>message.text=text;
        private void Refresh()
        {
            var actor = session.LocalActor;
            string version=actor==null?null:$"{actor.id}:{actor.generation}:{actor.lifecycleSequence}";
            if(inventoryActorVersion!=version) {inventoryActorVersion=version;inventory.SetActive(false);}
            friendlyDecision.gameObject.SetActive(actor!=null && Mirror.NetworkServer.active);
            if (actor == null)
            {lobby.SetActive(!session.Lobby.ClientWaiting);RefreshSelectedWorld();inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);factory.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
            lobby.SetActive(false);
            leave.gameObject.SetActive(true);friendlyFire.gameObject.SetActive(Mirror.NetworkServer.active);
            factory.gameObject.SetActive(actor.level=="hub" || actor.level=="factory:0");
            factory.GetComponentInChildren<Text>().text=actor.level=="hub"?"На завод 0":"Вернуться в хаб";
            friendlyFire.GetComponentInChildren<Text>().text="Дружественный огонь: "+(session.World.friendlyFire?"включён":"выключен");
            friendlyDecision.interactable=session.World.friendlyFire;
            friendlyDecision.GetComponentInChildren<Text>().text="Прямое попадание решает: "+(session.World.friendlyFireDecision==FriendlyFireDecision.Attacker?"атакующий":"пострадавший");
            status.text = $"{actor.character} · здоровье {actor.health:0}/100 · {(actor.down ? "Без сознания" : "В сознании")} · игроков: {session.World.actors.FindAll(a=>a.player && a.connected).Count} · {(actor.level=="hub"?"Хаб": "Завод 0")}\n1 — копьё · 2 — лом · 3 — 10-мм пистолет · 4 — лазерный пистолет · R — перезарядка";
            RefreshAmmunition();
            healthBar.fillAmount=Mathf.Clamp01(actor.health/100);
            manaBar.fillAmount=Mathf.Clamp01(actor.mana/100);
            personalItems.text="Личный инвентарь\n";
            foreach(var item in actor.inventory) personalItems.text+=$"\n{ItemName(item.definition)} × {item.count}";
            shownContainer=ContainerAccess.Nearest(session.World,actor,session.Rules.Loot);
            containerTitle.text=shownContainer==null?"Рядом нет контейнера":"Общий контейнер · 3 × 3";
            if(shownContainer!=null && session.World.pendingGrants.Exists(g=>g.containerId==shownContainer.id && g.recipient==actor.id))
                containerTitle.text="Подбор подтверждается…";
            for (int i = 0; i < slots.Length; i++)
            {
                var item = shownContainer!=null && i < shownContainer.items.Count ? shownContainer.items[i] : null;
                slots[i].GetComponentInChildren<Text>().text = item == null ? "Пусто" : $"{ItemName(item.definition)}\n{item.count}";
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
                case "heal":return "Хилка";
                case "grenade":return "Граната";
                case "p10":return "10-мм патроны";
                case "batt":return "Энергобатареи";
                default:Debug.LogWarning($"Нет русской подписи предмета в инвентаре: {id}.");return "["+id+"]";
            }
        }

        private void Update()
        {
            factory.interactable=session.LocalActor is {down:false} && !session.Travel.Pending;
            if(inventory.activeSelf && ContainerAccess.Nearest(session.World,session.LocalActor,session.Rules.Loot)?.id!=shownContainer?.id) Refresh();
            RefreshAmmunition();
            if(revival==null) return;
            float progress=RevivalProgress?.Invoke() ?? 0;
            revival.text=RevivalPending?.Invoke()==true?"Ожидаем подтверждения поднятия…":
                progress>0?$"Поднимаем союзника: {progress:P0} · удерживайте E":"";
        }
        private void RefreshAmmunition()
        {
            var actor=session?.LocalActor;
            if(actor==null) {if(ammunition!=null) ammunition.text="";return;}
            var definition=WeaponCatalog.Get(actor.weapon);
            if(string.IsNullOrEmpty(definition.ammo)) {ammunition.text="";return;}
            var magazine=WeaponAmmunition.Magazine(actor,actor.weapon);
            if(magazine==null)
            {
                if(missingMagazines.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Нет магазина: actor={actor.id}, weapon={actor.weapon}, loadoutVersion={actor.loadoutVersion}.",this);
                ammunition.text="Ошибка данных магазина — см. журнал.";return;
            }
            ammunition.text=$"Заряжено: {magazine.rounds}/{definition.magazineSize} · запас: {InventoryJournal.Count(actor,definition.ammo)}";
        }

        private Image OriginalBar(string key,Vector2 position,Vector2 size)
        {
            var obj=Panel(transform,"Шкала "+key,position,size);
            var image=obj.GetComponent<Image>();ApplyOriginalImage(image,key,true);
            return image;
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
