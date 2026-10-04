using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class MainMenuPanel
    {
        private readonly MainMenuView view;
        public GameObject Root=>view.gameObject;
        public GameObject Controls=>view.Controls;
        public InputField Character=>view.Character;
        public InputField Address=>view.Address;
        public Button Host=>view.Host;
        public Button Worlds=>view.Worlds;
        public Text World=>view.World;
        public Text Notice=>view.Notice;
        public static string DescribeWorld(string name,string branch,long revision)=>
            $"Выбран: {name}\nВетвь {branch.Substring(0,Math.Min(8,branch.Length))} · состояние №{revision}";
        public RectTransform SoundRect=>view.Settings.GetComponent<RectTransform>();
        public MainMenuPanel(Transform parent,Action host,Action join,Action worlds,Action settings,Action newGame)
        {
            var prefab=Resources.Load<MainMenuView>("Presentation/MainMenu");
            if(prefab==null) throw new InvalidOperationException("Нет префаба главного меню Presentation/MainMenu.");
            prefab.Validate();view=UnityEngine.Object.Instantiate(prefab,parent,false);
            view.Host.onClick.AddListener(()=>host());view.Join.onClick.AddListener(()=>join());
            view.Worlds.onClick.AddListener(()=>worlds());view.Settings.onClick.AddListener(()=>settings());
            view.NewGame.onClick.AddListener(()=>newGame());view.Exit.onClick.AddListener(()=>Application.Quit());
        }
    }
}

