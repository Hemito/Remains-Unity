using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Runtime
{
    // Virtual Unity pointer only; operates on an isolated Profiles directory.
    public sealed class CharacterMenuProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private bool finished;
        private float deadline;
        private CharacterLibrary library;
        private double legacySeconds,newSecondsBeforeReturn,newSecondsAfterReturn;
        private bool playTimePersisted;
        private int readyVisits,pendingPresentationFrames;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+60;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Редактору нужен отдельный каталог проверки.");
            library=new CharacterLibrary(Path.Combine(value.Options.SaveDirectory,"Profiles"));
            for(int i=0;i<7;i++)
            {
                string name="Архивный "+i;
                var actor=new ActorState {id="player:"+name,character=name,player=true,level="hub",hair=i};
                PlayerStarterInventory.Give(actor);new SaveStore(library.ProfilePath(name)).SaveCharacter(actor);
            }
            var old=new ActorState {id="player:Старый",character="Старый",player=true,health=77,level="hub",hair=2};
            PlayerStarterInventory.Give(old);new SaveStore(library.ProfilePath(old.character)).SaveCharacter(old);
            Directory.CreateDirectory(library.ProfilePath("Сломанный"));File.WriteAllText(Path.Combine(library.ProfilePath("Сломанный"),"character.json"),"{}");
        }
        private RectTransform Rect(UnityEngine.UI.Selectable element)=>element.GetComponent<RectTransform>();
        private IEnumerator Wait(Func<bool> condition)
        {while(!finished && !condition()) yield return null;yield return null;}
        private IEnumerator WaitForLevel()
        {
            while(!finished && (runtime.Session.LocalActor==null || !runtime.LevelPresentationReady))
            {if(runtime.Session.LocalActor!=null) pendingPresentationFrames++;yield return null;}
            if(!finished) readyVisits++;
            yield return null;
        }
        private IEnumerator Start()
        {
            yield return UiPointerProbe.Click(runtime.Hud.CharactersButtonRect);
            yield return Wait(()=>runtime.Hud.Characters?.Ready==true);
            var list=runtime.Hud.Characters;
            if(list.View.Next.interactable!=true) throw new InvalidOperationException("У списка персонажей нет второй страницы.");
            yield return UiPointerProbe.Click(Rect(list.View.Create));
            var editor=list.Editor;editor.View.Name.text="Отмена";
            yield return UiPointerProbe.Click(Rect(editor.View.HairNext));
            runtime.Hud.ToggleMenu();
            yield return null;
            if(list.Editor!=null || runtime.Hud.Characters!=list || !list.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Первая команда Escape не вернула из редактора в список персонажей.");
            if(Directory.Exists(library.ProfilePath("Отмена"))) throw new InvalidOperationException("Отмена создала профиль.");
            runtime.Hud.ToggleMenu();yield return null;
            if(runtime.Hud.Characters!=null) throw new InvalidOperationException("Вторая команда Escape не закрыла список персонажей.");
            yield return UiPointerProbe.Click(runtime.Hud.CharactersButtonRect);
            yield return Wait(()=>runtime.Hud.Characters?.Ready==true);list=runtime.Hud.Characters;
            string oldPath=Path.Combine(library.ProfilePath("Старый"),"character.json"),oldSource=File.ReadAllText(oldPath);
            yield return UiPointerProbe.Click(Rect(list.View.Create));editor=list.Editor;editor.View.Name.text="Старый";
            yield return UiPointerProbe.Click(Rect(editor.View.Create));
            yield return Wait(()=>!editor.Busy);
            if(!editor.View.Status.text.Contains("существует") || File.ReadAllText(oldPath)!=oldSource)
                throw new InvalidOperationException("Создание занятым именем не показало отказ или изменило старый профиль.");
            editor.View.Name.text="Роза";
            yield return UiPointerProbe.Click(Rect(editor.View.HairPrevious));
            yield return UiPointerProbe.Click(Rect(editor.View.EyePrevious));
            yield return UiPointerProbe.Click(Rect(editor.View.SecondaryEnabled));
            yield return UiPointerProbe.Click(Rect(editor.View.Colors[2]));
            yield return UiPointerProbe.Click(Rect(editor.View.Swatches[6]));
            yield return UiPointerProbe.Click(Rect(editor.View.Colors[0]));
            yield return UiPointerProbe.Click(Rect(editor.View.Swatches[1]));
            yield return UiPointerProbe.Click(Rect(editor.View.Colors[1]));
            yield return UiPointerProbe.Click(Rect(editor.View.Swatches[8]));
            var expected=PonyAppearance.Snapshot(editor.Preview);
            Canvas.ForceUpdateCanvases();ValidatePortrait(editor.View.Portrait);
            Capture("editor");
            yield return UiPointerProbe.Click(Rect(editor.View.Create));
            yield return Wait(()=>list.Editor==null && list.Ready);
            string newPath=Path.Combine(library.ProfilePath("Роза"),"character.json"),newSource=File.ReadAllText(newPath);
            var saved=StateJson.FromJson<ActorState>(newSource);
            if(runtime.Session.CharacterId!="Роза" || !PonyAppearance.Snapshot(saved).Equals(expected) || !CharacterTimeRecord.Read(library.ProfilePath("Роза")).completeHistory)
                throw new InvalidOperationException("Созданная внешность, выбор или новое время не сохранены.");
            yield return Select("Старый");
            yield return Wait(()=>runtime.Hud.Characters==null && runtime.Hud.WorldDescriptionReady);
            yield return UiPointerProbe.Click(runtime.Hud.HostButtonRect);
            yield return WaitForLevel();
            if(runtime.Session.LocalActor.character!="Старый" || runtime.Session.LocalActor.health!=77 || runtime.Session.LocalActor.appearance!=null)
                throw new InvalidOperationException("Выбор старого профиля изменил внешность/здоровье или создал нового персонажа.");
            string sharedWorld=runtime.Session.World.worldId;
            string worldDirectory=runtime.Session.Saves.WorldDirectoryPath;
            var originalSelection=runtime.Session.Saves.SelectedSnapshot();
            var alternative=new WorldState();runtime.Session.Saves.SaveWorld(alternative);
            runtime.Session.Saves.SelectContinuation(originalSelection);
            yield return new WaitForSecondsRealtime(2);Capture("old-player");runtime.Session.StopHost();
            yield return Wait(()=>runtime.Session.LocalActor==null && !Mirror.NetworkClient.active && !Mirror.NetworkServer.active);
            var oldTime=CharacterTimeRecord.Read(library.ProfilePath("Старый"));
            if(oldTime.completeHistory || oldTime.seconds<1 || File.ReadAllText(newPath)!=newSource)
                throw new InvalidOperationException("Учёт старого времени неверен или игра другим персонажем переписала новый профиль.");
            legacySeconds=oldTime.seconds;
            yield return UiPointerProbe.Click(runtime.Hud.CharactersButtonRect);
            yield return Wait(()=>runtime.Hud.Characters?.Ready==true);
            yield return Select("Роза");
            yield return Wait(()=>runtime.Hud.Characters==null && runtime.Hud.WorldDescriptionReady);
            if(runtime.Session.Saves.WorldDirectoryPath!=worldDirectory || runtime.Session.Saves.SelectedSnapshot().WorldId!=sharedWorld ||
                !runtime.Session.Saves.Continuations().Any(copy=>copy.WorldId==alternative.worldId))
                throw new InvalidOperationException("Смена персонажа потеряла общий выбор мира или другой доступный мир.");
            yield return UiPointerProbe.Click(runtime.Hud.HostButtonRect);
            yield return WaitForLevel();
            if(runtime.Session.LocalActor.character!="Роза" || !PonyAppearance.Snapshot(runtime.Session.LocalActor).Equals(expected) || runtime.Session.World.worldId!=sharedWorld)
                throw new InvalidOperationException("Игра не использовала внешность созданного персонажа.");
            runtime.Hud.ToggleInventory();
            int crowbarRow=runtime.Session.LocalActor.inventory.FindIndex(item=>item.definition=="crowbar" && item.weaponState!=null);
            yield return UiPointerProbe.Click(runtime.Hud.DepositRowRect(crowbarRow));
            yield return UiPointerProbe.Click(runtime.Hud.UseItemRect);
            if(runtime.Session.LocalActor.weapon!="crowbar") throw new InvalidOperationException("Экипировка лома через инвентарь не применилась.");
            runtime.Hud.ToggleInventory();
            yield return new WaitForSecondsRealtime(2);Capture("new-player");runtime.Session.StopHost();
            yield return Wait(()=>runtime.Session.LocalActor==null && !Mirror.NetworkClient.active && !Mirror.NetworkServer.active);
            var newTime=CharacterTimeRecord.Read(library.ProfilePath("Роза"));
            if(!newTime.completeHistory || newTime.seconds<1) throw new InvalidOperationException("Новое игровое время не сохранено.");
            newSecondsBeforeReturn=newTime.seconds;
            // Model progress made with this character in another world, then return to its hub record.
            var advanced=new SaveStore(library.ProfilePath("Роза")).LoadCharacter();advanced.health=58;
            advanced.appearance.fur=0xAB3412;new SaveStore(library.ProfilePath("Роза")).SaveCharacter(advanced);
            yield return UiPointerProbe.Click(runtime.Hud.HostButtonRect);
            yield return WaitForLevel();
            if(runtime.Session.World.worldId!=sharedWorld || runtime.Session.LocalActor.health!=58 || runtime.Session.LocalActor.appearance.fur!=0xAB3412 || runtime.Session.LocalActor.weapon!="crowbar")
                throw new InvalidOperationException("Возвращение в хаб восстановило устаревший профиль из мира.");
            runtime.Session.StopHost();
            yield return Wait(()=>runtime.Session.LocalActor==null && !Mirror.NetworkClient.active && !Mirror.NetworkServer.active);
            var returnedTime=CharacterTimeRecord.Read(library.ProfilePath("Роза"));
            if(returnedTime==null || !returnedTime.completeHistory || returnedTime.seconds<newSecondsBeforeReturn || returnedTime.trackedFrom!=newTime.trackedFrom)
                throw new InvalidOperationException("Повторный вход сбросил время или исходную дату учёта персонажа.");
            newSecondsAfterReturn=returnedTime.seconds;playTimePersisted=true;
            yield return UiPointerProbe.Click(runtime.Hud.CharactersButtonRect);
            yield return Wait(()=>runtime.Hud.Characters?.Ready==true);
            list=runtime.Hud.Characters;
            yield return FindPage("Роза");
            int row=Array.FindIndex(list.View.Names,name=>name.gameObject.activeInHierarchy && name.text=="Роза");
            var icon=row<0?null:list.View.Portraits[row].Weapon.sprite;var expectedIcon=new InventoryArtwork().Require("crowbar");
            if(row<0 || icon==null || icon.texture!=expectedIcon.texture || icon.rect!=expectedIcon.rect || !list.View.Times[row].text.StartsWith("В игре:"))
                throw new InvalidOperationException("Карточка не показывает последнее экипированное оружие/время.");
            Canvas.ForceUpdateCanvases();ValidatePortrait(list.View.Portraits[row]);Capture("library");
            Finish(true,"Настоящий UI: список и страницы, команды Escape закрывают редактор и библиотеку последовательно, отмена без файла, занятое имя без перезаписи, формы/цвета/превью, создание и выбор; старый профиль сохранил здоровье/вид, новый вошёл с выбранной внешностью. Время двух отдельных сессий и последнее оружие отражены в карточках.");
        }
        private IEnumerator Select(string name)
        {
            yield return FindPage(name);var view=runtime.Hud.Characters.View;
            int row=Array.FindIndex(view.Names,label=>label.gameObject.activeInHierarchy && label.text==name);
            if(row<0) throw new InvalidOperationException("Нет карточки: "+name);
            yield return UiPointerProbe.Click(Rect(view.Cards[row]));
        }
        private IEnumerator FindPage(string name)
        {
            var view=runtime.Hud.Characters.View;
            while(view.Previous.interactable) yield return UiPointerProbe.Click(Rect(view.Previous));
            while(!view.Names.Any(label=>label.gameObject.activeInHierarchy && label.text==name))
            {
                if(!view.Next.interactable) throw new InvalidOperationException("Нет персонажа в списке: "+name);
                yield return UiPointerProbe.Click(Rect(view.Next));
            }
        }
        private static void ValidatePortrait(CharacterPortraitView view)
        {
            foreach(var part in view.Parts.Where(part=>part.isActiveAndEnabled))
                if(part.GetComponent<CanvasRenderer>()?.materialCount!=1 || part.rectTransform.rect.width<20 || part.rectTransform.rect.height<20)
                    throw new InvalidOperationException("Портрет не подготовлен к отрисовке: "+part.name);
        }
        private void Capture(string name)=>NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+"."+name+".png",runtime.Hud.GetComponent<Canvas>());
        private void Update() {if(!finished && Time.unscaledTime>deadline) Finish(false,"Таймаут редактора персонажей.");}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,legacySeconds=legacySeconds,
                newSecondsBeforeReturn=newSecondsBeforeReturn,newSecondsAfterReturn=newSecondsAfterReturn,playTimePersisted=playTimePersisted,
                readyVisits=readyVisits,pendingPresentationFrames=pendingPresentationFrames},true));Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Report {public bool success,playTimePersisted;public string detail;public double legacySeconds,newSecondsBeforeReturn,newSecondsAfterReturn;public int readyVisits,pendingPresentationFrames;}
    }
}
