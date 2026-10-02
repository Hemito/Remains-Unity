using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    public sealed class WorldManagementProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+35;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка меню требует отдельного временного профиля.");
        }
        private RectTransform Button(string text)=>runtime.Hud.GetComponentsInChildren<Button>()
            .Single(value=>value.GetComponentInChildren<Text>().text==text).GetComponent<RectTransform>();
        private IEnumerator Start()
        {
            var store=runtime.Session.Saves;var first=new WorldState();var snapshot=store.SaveWorld(first);
            yield return UiPointerProbe.Click(runtime.Hud.WorldPickerButtonRect);
            yield return UiPointerProbe.Click(Button("Имя / удалить"));
            var panel=GameObject.Find("Управление миром");panel.GetComponentInChildren<InputField>().text="Проверка меню";
            yield return UiPointerProbe.Click(Button("Сохранить имя"));
            if(new WorldLibrary(store).Name(first.worldId)!="Проверка меню" || store.SelectedSnapshot().Id!=snapshot.Id)
            {Finish(false,"Переименование через меню не сохранилось или изменило снимок.");yield break;}
            yield return UiPointerProbe.Click(Button("Имя / удалить"));
            yield return UiPointerProbe.Click(Button("Удалить мир"));
            if(store.SelectedSnapshot()==null) {Finish(false,"Мир удалён без подтверждения.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".confirm.png",runtime.Hud.GetComponent<Canvas>());
            yield return UiPointerProbe.Click(Button("Да, удалить мир"));
            if(store.SelectedSnapshot()!=null || store.Continuations().Count!=0)
            {Finish(false,"Удаление через меню оставило мир выбранным или в списке.");yield break;}
            yield return UiPointerProbe.Click(Button("Закрыть"));
            for(int i=0;i<6;i++) store.SaveWorld(new WorldState());
            string lastWorld=store.Continuations()[5].WorldId;
            yield return UiPointerProbe.Click(runtime.Hud.WorldPickerButtonRect);
            yield return UiPointerProbe.Click(Button("Далее"));
            var picker=runtime.Hud.GetComponentInChildren<Remains.Presentation.WorldPickerView>();
            if(picker.Page.text!="2 / 2" || picker.Choices.Count(x=>x.gameObject.activeSelf)!=1)
            {Finish(false,"Вторая страница не показывает единственную оставшуюся копию.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".page-2.png",runtime.Hud.GetComponent<Canvas>());
            yield return UiPointerProbe.Click(Button("Имя / удалить"));
            yield return UiPointerProbe.Click(Button("Удалить мир"));
            yield return UiPointerProbe.Click(Button("Да, удалить мир"));
            picker=runtime.Hud.GetComponentInChildren<Remains.Presentation.WorldPickerView>();
            if(store.Continuations().Count!=5 || store.Continuations().Any(x=>x.WorldId==lastWorld) ||
                picker.Page.text!="1 / 1" || picker.Next.interactable || picker.Choices.Count(x=>x.gameObject.activeSelf)!=5)
            {Finish(false,"После удаления последней записи страница не вернулась к пяти оставшимся мирам.");yield break;}
            yield return UiPointerProbe.Click(Button("Закрыть"));
            var old=new WorldState();store.SaveWorld(old);
            yield return UiPointerProbe.Click(Button("Новая игра"));
            while(runtime.Session.LocalActor==null && !finished) yield return null;
            if(finished) yield break;
            bool valid=runtime.Session.ServerWorld.worldId!=old.worldId && store.Continuations().Any(value=>value.WorldId==old.worldId);
            Finish(valid,"UI: имя сохранено, удаление подтверждено, удаление последнего мира второй страницы вернуло первую; новая игра сохранила старый мир.");
        }
        private void Update() {if(!finished && Time.unscaledTime>deadline) Finish(false,"Таймаут проверки меню сохранений.");}
        private void Finish(bool success,string detail)
        {
            finished=true;File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));
            Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
