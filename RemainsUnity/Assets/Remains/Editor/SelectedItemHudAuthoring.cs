using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class SelectedItemHudAuthoring
    {
        public const string HudPath="Assets/Remains/Resources/Presentation/CombatHud.prefab";
        private const string SelectedName="Выбранная вещь";

        [MenuItem("Remains/Интерфейс/Сохранить компактный блок выбранной вещи")]
        public static void Apply()
        {
            var root=PrefabUtility.LoadPrefabContents(HudPath);
            try
            {
                var hud=root.GetComponent<CombatHudView>();
                if(hud==null || hud.Loadout==null || hud.Ammunition==null || hud.Grenades==null || hud.GrenadeIcon==null ||
                    hud.Weapons==null || hud.Weapons.Length!=12)
                    throw new InvalidOperationException("Компактный HUD: нужен сохранённый HUD с двенадцатью назначениями и запасом гранат.");
                var panel=(RectTransform)hud.Loadout.transform;
                panel.anchorMin=panel.anchorMax=Vector2.zero;panel.pivot=new Vector2(0,1);
                panel.sizeDelta=new Vector2(800,108);panel.localScale=new Vector3(.8f,.8f,1);
                panel.anchoredPosition=new Vector2(18,16+108*.8f);
                var background=hud.Loadout.GetComponent<Image>();
                if(background!=null) {background.enabled=false;background.raycastTarget=false;}
                for(int i=0;i<hud.Weapons.Length;i++)
                {
                    var button=hud.Weapons[i];
                    if(button==null) throw new InvalidOperationException("Компактный HUD: отсутствует назначение, slot="+i);
                    HudElements.Rect((RectTransform)button.transform,new Vector2(14+i*64,-6),new Vector2(62,38));
                }
                var selected=panel.Find(SelectedName) as RectTransform;
                if(selected==null)
                {
                    var created=new GameObject(SelectedName,typeof(RectTransform));created.transform.SetParent(panel,false);
                    selected=(RectTransform)created.transform;
                }
                HudElements.Rect(selected,new Vector2(14,-50),new Vector2(772,50));
                var view=selected.GetComponent<SelectedItemHudView>() ?? selected.gameObject.AddComponent<SelectedItemHudView>();
                var plate=selected.GetComponent<Image>() ?? selected.gameObject.AddComponent<Image>();
                plate.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Resources/Presentation/MainMenuArtwork/button-glow.asset");
                if(plate.sprite==null) throw new InvalidOperationException("Компактный HUD: нет оригинального сохранённого рисунка кнопки.");
                plate.type=Image.Type.Sliced;plate.color=new Color(1,1,1,.12f);plate.raycastTarget=false;
                var ui=new HudElements(hud.Ammunition.font);
                view.Icon=ImageChild(selected,"Изображение выбранной вещи",new Vector2(0,-2),new Vector2(64,44));
                view.AmmoIcon=ImageChild(selected,"Изображение боеприпасов",new Vector2(420,-2),new Vector2(40,20));
                view.ItemName=TextChild(ui,selected,"Название выбранной вещи",new Vector2(74,0),new Vector2(330,22),18);
                view.ActionState=TextChild(ui,selected,"Действие выбранной вещи",new Vector2(74,-24),new Vector2(454,24),15);
                hud.Ammunition.transform.SetParent(selected,false);hud.Ammunition.name="Количество выбранной вещи";
                Configure(hud.Ammunition,new Vector2(466,0),new Vector2(306,22),18);view.Counter=hud.Ammunition;
                hud.GrenadeIcon.transform.SetParent(selected,false);
                HudElements.Rect(hud.GrenadeIcon.rectTransform,new Vector2(590,-27),new Vector2(18,18));hud.GrenadeIcon.raycastTarget=false;
                hud.Grenades.transform.SetParent(selected,false);
                Configure(hud.Grenades,new Vector2(616,-24),new Vector2(156,24),16);
                view.ItemName.text="Оружие не выбрано";view.Counter.text="";view.ActionState.text="";
                view.Icon.enabled=false;view.Icon.sprite=null;view.AmmoIcon.enabled=false;view.AmmoIcon.sprite=null;
                hud.SelectedItem=view;view.Validate();hud.Validate();
                EditorUtility.SetDirty(hud);EditorUtility.SetDirty(view);
                PrefabUtility.SaveAsPrefabAsset(root,HudPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Debug.Log("Компактный HUD сохранён: двенадцать назначений, выбранная вещь, боеприпасы и быстрый запас гранат; игровые данные не изменены.");
        }

        private static Image ImageChild(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var existing=parent.Find(name);
            var obj=existing!=null?existing.gameObject:new GameObject(name,typeof(RectTransform));
            if(existing==null) obj.transform.SetParent(parent,false);
            var image=obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            HudElements.Rect(image.rectTransform,position,size);image.preserveAspect=true;image.raycastTarget=false;image.color=Color.white;
            return image;
        }
        private static Text TextChild(HudElements ui,Transform parent,string name,Vector2 position,Vector2 size,int fontSize)
        {
            var existing=parent.Find(name);
            var text=existing!=null?existing.GetComponent<Text>():ui.Label(parent,"",position,size);
            if(text==null) throw new InvalidOperationException("Компактный HUD: сохранённый элемент не является текстом: "+name);
            text.name=name;Configure(text,position,size,fontSize);return text;
        }
        private static void Configure(Text text,Vector2 position,Vector2 size,int fontSize)
        {
            HudElements.Rect(text.rectTransform,position,size);text.fontSize=fontSize;text.alignment=TextAnchor.MiddleLeft;
            text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.resizeTextForBestFit=false;text.supportRichText=false;
        }
    }
}
