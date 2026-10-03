using System;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CombatMusicChecks
    {
        public static void Run()
        {
            var catalog=Resources.Load<MusicCatalogAsset>("Content/area-music");
            var state=new CombatMusicState(catalog.CreateCombatTracks());
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,level="factory:0"};
            var enemy=new ActorState {id="enemy",actorType="raider",target=player.id,level=player.level};
            world.actors.Add(player);world.actors.Add(enemy);
            state.SetContext(world.worldId,player.level);
            state.ObservePreparation(world,enemy);
            Require(state.Track==null,"Назначение сетевой цели включило бой.");
            enemy.perception.hasKnownPosition=true;state.ObservePreparation(world,enemy);
            Require(state.Track=="raider-combat","Подтверждённый контакт не включил музыку до атаки.");
            enemy.attackBusy=true;enemy.attackTarget=player.id;
            state.ObservePreparation(world,enemy);
            Require(state.Track=="raider-combat","Подготовка атаки не включила бой.");
            state.Step(100,true);Require(state.Track!=null,"Пауза расходует боевую выдержку.");
            state.Step(6,false);Require(state.Track!=null,"Боевая музыка завершилась раньше выдержки.");
            Require(state.Step(1.01f,false) && state.Track==null,"Не восстановлена фоновая музыка.");
            enemy.level="factory:1";state.ObservePreparation(world,enemy);
            Require(state.Track==null,"Бой другого уровня переключил музыку.");
            enemy.level=player.level;enemy.down=true;state.ObserveAttack(enemy);
            Require(state.Track==null,"Лежащий противник запустил бой.");
            enemy.down=false;state.ObserveAttack(enemy);state.SetContext(world.worldId,"hub");
            Require(state.Track==null,"Бой перешёл за игроком в хаб.");
            state.SetContext(world.worldId,player.level);state.ObserveAttack(enemy);
            state.SetContext("other-world",player.level);Require(state.Track==null,"Бой сохранился между сессиями.");

            var root=new GameObject("Проверка музыкальных переходов");
            try
            {
                var session=root.AddComponent<CoopSession>();
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,player.id);
                enemy.attackBusy=false;enemy.perception.hasKnownPosition=false;
                var music=root.AddComponent<AreaMusic>();music.Initialize(session);
                Require(music.CurrentTrack=="factory","Нет фоновой музыки завода.");
                enemy.attackBusy=true;
                var refresh=typeof(AreaMusic).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic);
                refresh.Invoke(music,null);
                Require(music.CurrentTrack=="raider-combat","AreaMusic не выбрал боевую запись.");
                int changes=music.Changes;refresh.Invoke(music,null);
                Require(music.Changes==changes,"Повторное событие перезапускает дорожку.");
                player.level="hub";refresh.Invoke(music,null);
                Require(music.CurrentTrack=="hub","После выхода осталась боевая музыка.");
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,null);refresh.Invoke(music,null);
                Require(music.CurrentTrack=="menu","Отключение не вернуло музыку меню.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("Проверены боевая музыка, разделение уровней, пауза и возврат к фону.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
