using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;
namespace Remains.Editor
{
    public static class MainMenuPrefabChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка меню",typeof(RectTransform));var calls=new int[6];
            try
            {
                var first=new MainMenuPanel(root.transform,()=>calls[0]++,()=>calls[1]++,()=>calls[2]++,()=>calls[3]++,()=>calls[4]++,()=>{},()=>calls[5]++);
                var second=new MainMenuPanel(root.transform,()=>{},()=>{},()=>{},()=>{},()=>{});
                var view=first.Root.GetComponent<MainMenuView>();view.Validate();
                view.World.text=MainMenuRender.LongWorldDescription;
                if(view.World.preferredHeight>view.World.rectTransform.rect.height+.1f)
                    throw new InvalidOperationException("Длинное допустимое название мира обрезает сведения о сохранении.");
                var description=view.World.rectTransform;var picker=view.Worlds.GetComponent<RectTransform>();
                if(description.anchoredPosition.y-description.rect.height<picker.anchoredPosition.y)
                    throw new InvalidOperationException("Описание мира перекрывает кнопку выбора сохранения.");
                var compare=view.CompareCopies.GetComponent<RectTransform>();var address=view.Address.GetComponent<RectTransform>();
                if(view.Host.GetComponent<RectTransform>().anchoredPosition.y-view.Host.GetComponent<RectTransform>().rect.height<compare.anchoredPosition.y ||
                    compare.anchoredPosition.y-compare.rect.height<address.anchoredPosition.y+40)
                    throw new InvalidOperationException("Сравнение копий перекрывает запуск или поля подключения.");
                if(view.CompareCopies.GetComponentInChildren<Text>().preferredWidth>compare.rect.width)
                    throw new InvalidOperationException("Надпись сравнения копий не помещается в кнопку.");
                first.SetActionsBusy(true);
                if(view.CompareCopies.IsInteractable() || view.Host.IsInteractable())
                    throw new InvalidOperationException("Подготовка подключения оставляет вторую кнопку запуска активной.");
                first.SetActionsBusy(false);
                if(!view.CompareCopies.IsInteractable()) throw new InvalidOperationException("Меню не разблокировало сравнение копий.");
                view.Host.onClick.Invoke();view.Join.onClick.Invoke();view.Worlds.onClick.Invoke();view.Settings.onClick.Invoke();view.NewGame.onClick.Invoke();view.CompareCopies.onClick.Invoke();
                foreach(int count in calls) if(count!=1) throw new InvalidOperationException("Действие меню не привязано ровно один раз.");
                second.Host.onClick.Invoke();first.Character.text="Проверка";
                if(calls[0]!=1 || second.Character.text==first.Character.text || GameAssets.Load<MainMenuView>("Presentation/MainMenu").Character.text==first.Character.text)
                    throw new InvalidOperationException("Экземпляры меню изменяют друг друга или исходный префаб.");
                Debug.Log("Префаб главного меню: PASS — действия кнопок и независимость экземпляров.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void RenderAndCheck() {Run();MainMenuRender.Render();}
    }
}

