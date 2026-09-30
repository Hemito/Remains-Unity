using System;
using Remains.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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
        private readonly Button[] slots = new Button[9];
        private Font font;
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
            gameObject.AddComponent<GraphicRaycaster>();
            var events = new GameObject("События интерфейса"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            status = Label(transform, "Подключение не установлено", new Vector2(20,-20), new Vector2(1050,60));
            message = Label(transform, "A/D — движение · Пробел — прыжок · W/S — лестница · ЛКМ — атака · E — поднять союзника · I — инвентарь", new Vector2(20,-650), new Vector2(1230,55));
            lobby = Panel(transform, "Подключение", new Vector2(420,-240), new Vector2(440,230));
            Button(lobby.transform,"Создать игру",new Vector2(20,-20),new Vector2(400,45),()=>HostRequested?.Invoke());
            address = Input(lobby.transform, new Vector2(20,-80));
            Button(lobby.transform,"Подключиться",new Vector2(20,-140),new Vector2(400,45),()=>JoinRequested?.Invoke(address.text));
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
            session.Changed += Refresh;
            session.Notice += text => message.text = text;
        }

        public void ToggleInventory() { inventory.SetActive(!inventory.activeSelf); Refresh(); }
        private void Refresh()
        {
            var actor = session.LocalActor;
            if (actor == null) return;
            lobby.SetActive(false);
            status.text = $"{actor.character} · здоровье {actor.health:0}/100 · {(actor.down ? "Без сознания" : "В сознании")} · игроков: {session.World.actors.FindAll(a=>a.player && a.connected).Count}\n1 — копьё · 2 — лом · 3 — пистолет · 4 — энергомагическое оружие";
            for (int i = 0; i < slots.Length; i++)
            {
                var item = i < session.World.container.Count ? session.World.container[i] : null;
                slots[i].GetComponentInChildren<Text>().text = item == null ? "Пусто" : $"{ItemName(item.definition)}\n{item.count}";
                slots[i].interactable = item != null && !actor.down;
            }
        }
        private static string ItemName(string id) => id switch { "caps"=>"Крышки", "heal"=>"Хилка", "grenade"=>"Граната", _=>id };

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
