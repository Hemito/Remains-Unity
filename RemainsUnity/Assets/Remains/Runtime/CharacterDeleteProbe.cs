using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Remains.Runtime
{
    // Live run of deleting characters from the real list with the virtual pointer, in an isolated library: the list must
    // come back after each deletion, a press on the button must not choose the character, and the log stays clean.
    public sealed class CharacterDeleteProbe : MonoBehaviour
    {
        private const string First="pony-one",Second="pony-delete",Third="pony-stay";
        [Serializable] private sealed class Report {public bool success;public string detail;public int errors;}
        private PrototypeRuntime runtime;
        private CharacterLibrary library;
        private int errors;
        private string firstError;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;Application.logMessageReceived+=Log;
            StartCoroutine(Run());
        }
        private void Log(string text,string stack,LogType type)
        {
            if(type is LogType.Exception or LogType.Error or LogType.Assert) {errors++;firstError??=text;}
        }
        private CharacterLibraryPanel Panel=>runtime.Hud.Characters;
        private IEnumerator Run()
        {
            float limit=Time.unscaledTime+120;
            string profiles=Path.Combine(Path.GetFullPath(runtime.Options.SaveDirectory),"Profiles");
            library=new CharacterLibrary(profiles);
            foreach(string name in new[]{First,Second,Third}) new CharacterLibrary(profiles,library.ProfilePath(name)).Create(Profile(name));
            yield return new WaitForSecondsRealtime(1);
            var menu=runtime.Hud.GetComponentInChildren<MainMenuView>(true);
            runtime.Hud.ShowCharacterLibrary();
            while(Panel==null || !Panel.Ready) {if(Time.unscaledTime>limit) {Finish(false,"Список не прочитан.");yield break;}yield return null;}
            Canvas.ForceUpdateCanvases();
            yield return Delete(Second,limit);if(finished) yield break;
            if(runtime.Session.CharacterId!=First || menu.Character.text!=First) {Finish(false,"Удаление другого персонажа сменило выбранного.");yield break;}
            yield return Delete(First,limit);if(finished) yield break;
            if(menu.Character.text.Length!=0) {Finish(false,"Меню показывает удалённого персонажа.");yield break;}
            if(!Names().Contains(Third)) {Finish(false,"Оставшийся персонаж пропал из списка.");yield break;}
            var moved=Directory.GetDirectories(library.DeletedDirectory);
            if(moved.Length!=2) {Finish(false,$"В DeletedCharacters {moved.Length} копий вместо 2.");yield break;}
            Finish(errors==0,errors==0?"Удалено 2, список вернулся оба раза.":$"Ошибки журнала {errors}: {firstError}");
        }
        private string[] Names()
        {
            var view=Panel.View;
            return view.Names.Where(text=>text.gameObject.activeInHierarchy).Select(text=>text.text).ToArray();
        }
        private IEnumerator Delete(string name,float limit)
        {
            var view=Panel.View;
            int row=Array.FindIndex(view.Names,text=>text.gameObject.activeInHierarchy && text.text==name);
            if(row<0) {Finish(false,"Нет карточки "+name);yield break;}
            var button=view.Delete[row];
            if(!Press(button)) {Finish(false,"Кнопка «Удалить» не первая под указателем.");yield break;}
            yield return null;
            if(Panel==null) {Finish(false,"Нажатие на «Удалить» выбрало персонажа и закрыло список.");yield break;}
            if(button.GetComponentInChildren<Text>().text!="Точно?") {Finish(false,$"Нет подтверждения у {name}: «{button.GetComponentInChildren<Text>().text}».");yield break;}
            if(!Directory.Exists(library.ProfilePath(name))) {Finish(false,"Удалено без подтверждения: "+name);yield break;}
            if(!Press(button)) {Finish(false,"Кнопка подтверждения не первая под указателем.");yield break;}
            while(Directory.Exists(library.ProfilePath(name)) || Panel==null || !Panel.Ready)
            {
                if(Time.unscaledTime>limit) {Finish(false,$"Список не вернулся после удаления {name}: «{view.Status.text}», {firstError}");yield break;}
                yield return null;
            }
            yield return null;
            if(Names().Contains(name)) {Finish(false,"Удалённый остался в списке: "+name);yield break;}
        }
        // A batch Editor has no pointer input, so the event system answers what lies under the point and the press goes to it:
        // the button must be first there, above its card.
        private static bool Press(Button button)
        {
            var hits=new List<RaycastResult>();var system=EventSystem.current;
            var data=new PointerEventData(system) {position=UiPointerProbe.ScreenBounds(button.GetComponent<RectTransform>()).center};
            system.RaycastAll(data,hits);
            if(hits.Count==0 || !hits[0].gameObject.transform.IsChildOf(button.transform)) return false;
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,ExecuteEvents.pointerClickHandler);
            return true;
        }
        private static ActorState Profile(string name)
        {
            var actor=new ActorState {id="player:"+name,character=name,player=true,level="hub",controlStateVersion=1,
                appearance=new CharacterAppearance {version=1,fur=0xA3A3A3,hair=0x854609}};
            PlayerStarterInventory.Give(actor);actor.weaponContent=WeaponCatalog.ManifestCopy();return actor;
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;Application.logMessageReceived-=Log;
            Debug.Log("Удаление персонажа: "+(success?"PASS":"FAIL")+" — "+detail);
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,errors=errors}));
            Application.Quit(success?0:1);
        }
    }
}
