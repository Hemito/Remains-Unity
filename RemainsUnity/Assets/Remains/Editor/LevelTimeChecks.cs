using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LevelTimeChecks
    {
        public static void RunStandalone()
        {Remains.Content.WeaponCatalog.Load();Run();}
        public static void Run()
        {
            var world=new WorldState();bool preserved=false;
            Require(LevelTimeMigration.Prepare(world,new[]{"hub","factory:0","factory:6","factory:-27","hub"},()=>
            {
                Require(world.levelTimeVersion==0 && world.levelTimes.Count==0,"Исходник должен быть сохранён до миграции");preserved=true;
            }) && preserved && world.levelTimes.Count==4,"Явная установка четырёх независимых часов");
            Require(!LevelTimeMigration.Prepare(world,new[]{"hub"},()=>throw new Exception("Не нужна повторная миграция")),"Повторная установка не создаёт часы заново");
            var clock=new LevelTimeAuthority(world);
            var hub=new ActorState {id="hub-player",player=true,connected=true,level="hub"};
            var player=new ActorState {id="factory-player",player=true,connected=true,level="factory:0",down=true};
            var offline=new ActorState {id="offline",player=true,connected=false,level="factory:6",health=3};
            world.actors.AddRange(new[]{hub,player,offline});
            var pauses=new LevelPauseCoordinator(new CooperativePauseRule());
            bool Active(string level)=>LevelActivity.HasConnectedPlayer(world,level) && !pauses.IsPaused(world,level);
            clock.Advance(2,Active);
            Require(clock.Read("hub")==2 && clock.Read("factory:0")==2 && clock.Read("factory:6")==0 && clock.Read("factory:-27")==0,
                "Активный хаб и подключённое тело идут, отключённый и пустой заводы заморожены");
            Require(pauses.SetManual(player,0,0,true)==null,"Ручная пауза своего уровня");
            clock.Advance(3,Active);
            Require(clock.Read("hub")==5 && clock.Read("factory:0")==2,"Пауза не замораживает соседнюю локацию");
            Require(pauses.SetManual(player,0,0,false)==null,"Снятие паузы");
            player.connected=false;clock.Advance(20,Active);
            Require(clock.Read("factory:0")==2,"Отключение последнего игрока не добавляет офлайн-время");
            player.connected=true;clock.Advance(.25,Active);
            Require(clock.Read("factory:0")==2.25,"Возобновление идёт с сохранённого времени");

            world.levelTimes.Single(s=>s.level=="factory:0").extensions=new System.Collections.Generic.Dictionary<string,JToken>
                {{"mod:clock-note",JObject.Parse("{\"version\":7,\"retained\":true}")}};
            string directory=Path.Combine(Path.GetTempPath(),"Remains-level-time-"+Guid.NewGuid().ToString("N"));
            try
            {
                var saves=new SaveStore(directory);saves.SaveWorld(world);
                var restored=saves.LoadWorld();var loaded=new LevelTimeAuthority(restored);
                Require(loaded.Read("factory:0")==2.25 && restored.levelTimes.Single(s=>s.level=="factory:0").extensions["mod:clock-note"]["retained"].Value<bool>(),
                    "Реальный save/reload сохраняет часы и неизвестные данные модов");
                loaded.Advance(.5,level=>level=="factory:0");
                Require(loaded.Read("factory:0")==2.75,"Новая сессия не считает время между сохранением и загрузкой");
                // A corrupt complete payload may have valid lightweight lineage.
                // It must not make every other continuation disappear from the lobby.
                foreach(int invalidClock in new[]{0,1,2})
                {
                    var damaged=world.Copy();damaged.worldId=Guid.NewGuid().ToString("N");
                    if(invalidClock==0) damaged.levelTimeVersion=2;
                    else if(invalidClock==1) damaged.levelTimes[0].elapsedSeconds=-1;
                    else damaged.levelTimes.Add(StateJson.Copy(damaged.levelTimes[0]));
                    string json=StateJson.ToJson(damaged);bool invalidData=false;
                    try {new RecoverySnapshot(json);}catch(InvalidDataException) {invalidData=true;}
                    Require(invalidData,"Повреждённые часы классифицируются как ошибка сохранённых данных");
                    File.WriteAllText(Path.Combine(saves.WorldDirectoryPath,"Worlds",damaged.worldId+".json"),json);
                    File.WriteAllText(Path.Combine(saves.WorldDirectoryPath,"world.json"),json);
                    Require(saves.SelectedSnapshotForListing()==null,"Повреждённый выбранный мир не обрывает просмотр библиотеки");
                    var continuations=saves.Continuations();
                    Require(continuations.Any(copy=>copy.WorldId==world.worldId) && continuations.All(copy=>copy.WorldId!=damaged.worldId),
                        "Корректный мир остаётся доступен рядом с повреждёнными часами");
                }
                string damagedSelection=File.ReadAllText(Path.Combine(saves.WorldDirectoryPath,"world.json"));
                var validSelection=saves.Continuations().First(copy=>copy.WorldId==world.worldId);
                saves.SelectContinuation(validSelection);
                Require(saves.SelectedSnapshot().Id==validSelection.Id &&
                    Directory.GetFiles(Path.Combine(saves.WorldDirectoryPath,"Recovery"),"*.json").Any(file=>File.ReadAllText(file)==damagedSelection),
                    "Выбор исправного мира сохраняет точную резервную копию повреждённого выбранного файла");
            }
            finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}

            foreach(int fps in new[]{30,60,144})
            {
                var timed=new WorldState();LevelTimeMigration.Prepare(timed,new[]{"factory:0"});var current=new LevelTimeAuthority(timed);
                for(int n=0;n<fps*6;n++) current.Advance(1d/fps,_=>true);
                Require(Math.Abs(current.Read("factory:0")-6)<1e-10,"Время за шесть секунд не зависит от числа кадров: "+fps);
            }
            string before=StateJson.ToJson(world);
            foreach(double invalid in new[]{-1d,double.NaN,double.PositiveInfinity})
                Reject(()=>clock.Advance(invalid,_=>true),"Недопустимый шаг времени");
            int calls=0;
            Reject(()=>clock.Advance(1,_=>++calls==2?throw new InvalidOperationException("Отказ политики мода"):true),"Атомарность при отказе политики");
            Require(StateJson.ToJson(world)==before,"Отклонённый шаг не изменяет ни один уровень");
            Reject(()=>clock.Read("factory:404"),"Отсутствующие часы не подменяются нулём");
            var legacy=new WorldState();
            Reject(()=>LevelTimeMigration.Prepare(legacy,new[]{"hub"},()=>throw new IOException("Нет копии источника")),"Нет миграции без обещанной копии");
            Require(legacy.levelTimeVersion==0 && legacy.levelTimes.Count==0 && legacy.revision==0,"Неудачная миграция сохраняет исходное состояние");
            var broken=world.Copy();broken.levelTimes.Add(StateJson.Copy(broken.levelTimes[0]));
            Reject(()=>new RecoverySnapshot(StateJson.ToJson(broken)),"Recovery отказывает при повторе часов");
            broken=world.Copy();broken.levelTimes[0].elapsedSeconds=-1;
            Reject(()=>new LevelTimeAuthority(broken),"Отрицательное сохранённое время");
            broken=world.Copy();broken.levelTimeVersion=2;
            Reject(()=>LevelTimeMigration.ValidateWorld(broken),"Будущая версия не сбрасывается");
            Debug.Log("Часы уровней: пауза, пустой уровень, отключённое тело, сохранение, моды и 30/60/144 FPS проверены.");
        }
        private static void Reject(Action action,string label)
        {
            bool rejected=false;try {action();}catch(Exception error) when(error is ArgumentException || error is InvalidOperationException || error is IOException || error is InvalidDataException) {rejected=true;}
            Require(rejected,label);
        }
        private static void Require(bool condition,string label)
        {if(!condition) throw new InvalidOperationException("Часы уровней: "+label);}
    }
}
