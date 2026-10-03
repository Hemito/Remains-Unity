using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyPaletteChecks
    {
        public static void Run()
        {
            var palette=PonyPaletteAsset.Load();var actors=new List<ActorState>();var colors=new HashSet<Color>();
            for(int i=0;i<8;i++)
            {
                int id=palette.ForNewCharacter(actors);
                if(id!=i || !colors.Add(palette.Hair(id))) throw new InvalidOperationException("Палитра не различает первые восемь персонажей.");
                var actor=new ActorState {player=true,hair=id};actors.Add(actor);
                if(actor.Copy().hair!=id) throw new InvalidOperationException("Копия персонажа теряет цвет.");
            }
            if(palette.ForNewCharacter(actors)!=0) throw new InvalidOperationException("Заполненная палитра не выбирает наименее занятый цвет.");
            actors.RemoveAt(3);
            if(palette.ForNewCharacter(actors)!=3) throw new InvalidOperationException("Свободный цвет не используется новым персонажем.");
            Debug.Log("Палитра: PASS — восемь цветов, стабильные ID, повторное использование свободного цвета.");
        }
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            for(int i=0;i<8;i++)
            {
                var actor=new ActorState {id="palette-"+i,x=(i%4)*3,y=-(i/4)*3,hair=i};
                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();visual.Bind(actor);visual.DrawPose(0);
            }
            var camera=new GameObject("Палитра персонажей").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=4;
            camera.transform.position=new Vector3(4.5f,-.5f,-10);camera.backgroundColor=new Color(.12f,.14f,.16f);
            Remains.Runtime.NetworkFrameCapture.Save(camera,System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..",".reference/tests/pony-palette.png")),width:1920,height:1080);
        }
    }
}
