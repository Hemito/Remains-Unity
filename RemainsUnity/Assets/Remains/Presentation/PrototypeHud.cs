using System;
using Remains.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        private CoopSession session;
        private Text status;
        private Text message;
        private GameObject lobby;
        private GameObject inventory;
        private InputField address;
        private InputField character;
        public Func<string,bool> SelectCharacter { get; set; }
        private readonly Button[] slots = new Button[9];
        private Font font;
        private Image healthBar;
        private Image manaBar;
        private Text personalItems;
        private Button friendlyFire;
        private Button leave;
        private Button factory;
        public event Action HostRequested;
        public event Action<string> JoinRequested;

        public void Initialize(CoopSession value)
        {
            session = value;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) throw new InvalidOperationException("Не найден системный шрифт Unity LegacyRuntime.ttf.");
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720);
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            status = Label(transform, "Подключение не установлено", new Vector2(20,-20), new Vector2(1050,60));
            healthBar=OriginalBar("health",new Vector2(20,-95),new Vector2(300,34));
            manaBar=OriginalBar("mana",new Vector2(20,-135),new Vector2(300,20));
            message = Label(transform, "A/D — ходьба · Shift — бег · Пробел — прыжок · W/S — лестница · ЛКМ — атака · E — поднять · I — инвентарь", new Vector2(20,-650), new Vector2(1230,55));
            lobby = Panel(transform, "Подключение", new Vector2(420,-240), new Vector2(440,300));
            var saved=session.Saves.LoadWorld();
            if(saved!=null) Label(transform,$"Локальная копия: мир {saved.worldId.Substring(0,8)} · ревизия {saved.revision}",new Vector2(420,-180),new Vector2(620,55));
            character=Input(lobby.transform,new Vector2(20,-20));character.text=session.CharacterId??"pony-one";
            Button(lobby.transform,"Создать игру",new Vector2(20,-80),new Vector2(400,45),()=>
            {if(SelectCharacter?.Invoke(character.text)==true) HostRequested?.Invoke();});
            address = Input(lobby.transform, new Vector2(20,-140));
            Button(lobby.transform,"Подключиться",new Vector2(20,-200),new Vector2(400,45),()=>
            {if(SelectCharacter?.Invoke(character.text)==true) JoinRequested?.Invoke(address.text);});
            inventory = Panel(transform,"Общий контейнер",new Vector2(920,-130),new Vector2(330,390));
            Label(inventory.transform,"Общий контейнер · 3 × 3",new Vector2(12,-12),new Vector2(305,40));
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                slots[i] = Button(inventory.transform,"Пусто",new Vector2(12+i%3*105,-60-i/3*95),new Vector2(98,86),()=>
                {
                    if (session.World != null && index < session.World.container.Count)
                        session.RequestClaim(session.World.container[index].id);
                });
            }
            inventory.SetActive(false);
            var personalPanel=Panel(inventory.transform,"Личный инвентарь",new Vector2(-380,0),new Vector2(360,390));
            ApplyOriginalImage(personalPanel.GetComponent<Image>(),"inventory-panel",false);
            personalItems=Label(personalPanel.transform,"",new Vector2(20,-20),new Vector2(320,340));
            friendlyFire=Button(transform,"Дружественный огонь",new Vector2(20,-550),new Vector2(290,45),()=>session.SetFriendlyFire(!session.World.friendlyFire));
            leave=Button(transform,"Сохранить и выйти из сессии",new Vector2(20,-600),new Vector2(290,45),()=>
            {if(Mirror.NetworkServer.active) session.StopHost();else session.StopClient();});
            friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);
            factory=Button(transform,"Вернуться на завод 0",new Vector2(20,-500),new Vector2(290,45),()=>session.ReturnToFactory());
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
            Refresh();
        }

        public void ToggleInventory() { inventory.SetActive(!inventory.activeSelf); Refresh(); }
        public RectTransform FactoryButtonRect => factory.GetComponent<RectTransform>();
        public void ShowMessage(string text)=>message.text=text;
        private void Refresh()
        {
            var actor = session.LocalActor;
            if (actor == null)
            {lobby.SetActive(true);inventory.SetActive(false);friendlyFire.gameObject.SetActive(false);leave.gameObject.SetActive(false);factory.gameObject.SetActive(false);status.text="Подключение не установлено";return;}
            lobby.SetActive(false);
            leave.gameObject.SetActive(true);friendlyFire.gameObject.SetActive(Mirror.NetworkServer.active);
            factory.gameObject.SetActive(actor.level=="hub");
            friendlyFire.GetComponentInChildren<Text>().text="Дружественный огонь: "+(session.World.friendlyFire?"включён":"выключен");
            status.text = $"{actor.character} · здоровье {actor.health:0}/100 · {(actor.down ? "Без сознания" : "В сознании")} · игроков: {session.World.actors.FindAll(a=>a.player && a.connected).Count} · {(actor.level=="hub"?"Хаб": "Завод 0")}\n1 — копьё · 2 — лом · 3 — пистолет · 4 — энергомагическое оружие";
            healthBar.fillAmount=Mathf.Clamp01(actor.health/100);
            manaBar.fillAmount=Mathf.Clamp01(actor.mana/100);
            personalItems.text="Личный инвентарь\n";
            foreach(var item in actor.inventory) personalItems.text+=$"\n{ItemName(item.definition)} × {item.count}";
            for (int i = 0; i < slots.Length; i++)
            {
                var item = i < session.World.container.Count ? session.World.container[i] : null;
                slots[i].GetComponentInChildren<Text>().text = item == null ? "Пусто" : $"{ItemName(item.definition)}\n{item.count}";
                slots[i].interactable = item != null && !actor.down;
            }
        }
        private static string ItemName(string id)
        {
            switch(id)
            {
                case "caps":return "Крышки";
                case "heal":return "Хилка";
                case "grenade":return "Граната";
                default:Debug.LogWarning($"Нет русской подписи предмета в инвентаре: {id}.");return "["+id+"]";
            }
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

        private GameObject Panel(Transform parent, string title, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(title, typeof(RectTransform)); obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),position,size);
            obj.AddComponent<Image>().color = new Color(0.04f,0.08f,0.07f,0.95f);
            return obj;
        }
        private Text Label(Transform parent, string content, Vector2 position, Vector2 size)
        {
            var obj = new GameObject("Текст",typeof(RectTransform)); obj.transform.SetParent(parent,false);
            Rect(obj.GetComponent<RectTransform>(),position,size);
            var label = obj.AddComponent<Text>(); label.font=font; label.fontSize=18; label.text=content;
            label.color = new Color(0.65f,1,0.75f); label.raycastTarget=false;
            return label;
        }
        private Button Button(Transform parent, string content, Vector2 position, Vector2 size, Action action)
        {
            var obj=Panel(parent,content,position,size);
            var button=obj.AddComponent<Button>(); button.onClick.AddListener(()=>action());
            var label=Label(obj.transform,content,new Vector2(5,-5),size-new Vector2(10,10));
            label.alignment=TextAnchor.MiddleCenter;
            return button;
        }
        private InputField Input(Transform parent,Vector2 position)
        {
            var obj=Panel(parent,"Адрес сервера",position,new Vector2(400,45));
            var label=Label(obj.transform,"127.0.0.1",new Vector2(10,-5),new Vector2(380,35));
            var input=obj.AddComponent<InputField>(); input.textComponent=label; input.text="127.0.0.1";
            return input;
        }
        private static void Rect(RectTransform rect,Vector2 position,Vector2 size)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=position; rect.sizeDelta=size;
        }
    }
}
