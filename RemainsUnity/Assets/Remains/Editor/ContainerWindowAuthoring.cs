using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class ContainerWindowAuthoring
    {
        public static void Run()
        {
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var root=ui.Panel(null,"Окно содержимого контейнера",Vector2.zero,new Vector2(228,214));
            try
            {
                var rect=(RectTransform)root.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,0);
                var view=root.AddComponent<ContainerWindowView>();view.Slots=new Button[9];view.Icons=new Image[9];view.Intents=new ContainerSlotIntent[9];
                for(int i=0;i<9;i++)
                {
                    var cell=ui.Panel(root.transform,"Ячейка "+(i+1),new Vector2(8+i%3*66,-8-i/3*66),new Vector2(62,62));
                    cell.GetComponent<Image>().color=new Color(.13f,.23f,.18f,1);
                    var button=cell.AddComponent<Button>();button.targetGraphic=cell.GetComponent<Image>();view.Slots[i]=button;
                    view.Intents[i]=cell.AddComponent<ContainerSlotIntent>();
                    var icon=ui.Panel(cell.transform,"Иконка предмета",new Vector2(5,-5),new Vector2(52,52)).GetComponent<Image>();
                    icon.color=Color.white;icon.raycastTarget=false;icon.preserveAspect=true;view.Icons[i]=icon;
                }
                var rail=ui.Panel(root.transform,"Прокрутка",new Vector2(210,-8),new Vector2(10,194));
                view.Scrollbar=rail.AddComponent<Scrollbar>();view.Scrollbar.direction=Scrollbar.Direction.TopToBottom;
                var handle=ui.Panel(rail.transform,"Ползунок",Vector2.zero,new Vector2(10,40)).GetComponent<Image>();
                handle.color=new Color(.4f,.8f,.5f,1);view.Scrollbar.handleRect=handle.rectTransform;view.Scrollbar.targetGraphic=handle;
                view.Pending=ui.Panel(root.transform,"Подбор ожидает подтверждения",new Vector2(4,-4),new Vector2(6,6)).GetComponent<Image>();
                view.Pending.color=new Color(1,.8f,.1f,1);view.Pending.raycastTarget=false;view.Pending.gameObject.SetActive(false);
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,"Assets/Remains/Resources/Presentation/ContainerWindow.prefab");
            }
            finally {Object.DestroyImmediate(root);}
            AssetDatabase.SaveAssets();PrototypeBuild.Build();
        }
    }
}
