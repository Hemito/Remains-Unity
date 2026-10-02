using System;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    public static class MainMenuPrefabChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка меню",typeof(RectTransform));var calls=new int[5];
            try
            {
                var first=new MainMenuPanel(root.transform,()=>calls[0]++,()=>calls[1]++,()=>calls[2]++,()=>calls[3]++,()=>calls[4]++);
                var second=new MainMenuPanel(root.transform,()=>{},()=>{},()=>{},()=>{},()=>{});
                var view=first.Root.GetComponent<MainMenuView>();view.Validate();
                view.Host.onClick.Invoke();view.Join.onClick.Invoke();view.Worlds.onClick.Invoke();view.Settings.onClick.Invoke();view.NewGame.onClick.Invoke();
                foreach(int count in calls) if(count!=1) throw new InvalidOperationException("Действие меню не привязано ровно один раз.");
                second.Host.onClick.Invoke();first.Character.text="Проверка";
                if(calls[0]!=1 || second.Character.text==first.Character.text || Resources.Load<MainMenuView>("Presentation/MainMenu").Character.text==first.Character.text)
                    throw new InvalidOperationException("Экземпляры меню изменяют друг друга или исходный префаб.");
                Debug.Log("Префаб главного меню: PASS — действия кнопок и независимость экземпляров.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        public static void RenderAndCheck() {Run();MainMenuRender.Render();}
    }
}

