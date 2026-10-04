using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Runtime
{
    // Exercises the actual menu with an isolated archive, never a user's profile.
    public sealed class WorldLibraryVolumeProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        private int loadingFrames,deletingFrames;
        private float largestFrameMs;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+75;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Проверка объёма библиотеки требует отдельного профиля.");
        }
        private RectTransform Button(string text)=>runtime.Hud.GetComponentsInChildren<Button>()
            .Single(value=>value.GetComponentInChildren<Text>().text==text).GetComponent<RectTransform>();
        private IEnumerator Start()
        {
            var store=runtime.Session.Saves;
            var world=new WorldState();WorldHistory.BeginContinuation(world);
            world.extensions=new System.Collections.Generic.Dictionary<string,Newtonsoft.Json.Linq.JToken>
                {{"volume-probe",new string('x',16384)}};
            string first=store.SaveWorld(world).Json;
            string characterPath=Path.Combine(store.DirectoryPath,"character-sentinel.json");
            const string character="Персонаж не изменён";File.WriteAllText(characterPath,character);
            var preparation=Task.Run(()=>
            {
                var state=StateJson.FromJson<WorldState>(first);RecoverySnapshot last=null;
                for(int i=1;i<1140;i++)
                {
                    state.checkpointSequence++;state.revision++;
                    last=new RecoverySnapshot(StateJson.ToJson(state));
                    File.WriteAllText(Path.Combine(store.DirectoryPath,"Worlds",state.worldId,"Snapshots",last.Id+".json"),last.Json);
                }
                store.SaveRecovery(last);
            });
            while(!preparation.IsCompleted && !finished) yield return null;
            if(finished) yield break;
            if(preparation.IsFaulted) {Finish(false,"Подготовка архива: "+preparation.Exception);yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.WorldPickerButtonRect);
            while(!runtime.Hud.WorldPickerReady && !finished) yield return null;
            if(finished) yield break;
            var warm=Task.Run(()=>store.Catalog.Read());
            while(!warm.IsCompleted && !finished) yield return null;
            if(finished) yield break;
            if(warm.IsFaulted || warm.Result.inspectedFiles!=1142 || warm.Result.readPayloads!=0 || warm.Result.entries.Count!=1)
            {Finish(false,"Повторный список не сохранил одну текущую ветвь или перечитал тела снимков.");yield break;}
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".catalog.png",runtime.Hud.GetComponent<Canvas>());
            yield return UiPointerProbe.Click(Button("Имя / удалить"));
            yield return UiPointerProbe.Click(Button("Удалить мир"));
            yield return UiPointerProbe.Click(Button("Да, удалить мир"));
            while((runtime.Hud.GetComponentInChildren<WorldManagementView>()!=null || !runtime.Hud.WorldPickerReady) && !finished) yield return null;
            if(finished) yield break;
            int moved=Directory.EnumerateFiles(Path.Combine(store.DirectoryPath,"DeletedWorlds"),"*.json",SearchOption.AllDirectories).Count();
            bool valid=moved==1142 && store.SelectedSnapshot()==null && store.Catalog.Read().entries.Count==0 &&
                File.ReadAllText(characterPath)==character && loadingFrames>2 && deletingFrames>2 && largestFrameMs<1500;
            Finish(valid,$"Библиотека: 1142 файла, одна ветвь; повторное чтение payload=0; удаление перенесло {moved} файлов; кадры списка={loadingFrames}, удаления={deletingFrames}, максимальный интервал={largestFrameMs:F1} мс.");
        }
        private void Update()
        {
            if(finished) return;
            var picker=runtime.Hud.GetComponentInChildren<WorldPickerPanel>();
            var management=runtime.Hud.GetComponentInChildren<WorldManagementView>();
            bool loading=picker!=null && picker.Busy;
            bool deleting=management!=null && management.GetComponent<BackgroundSaveOperation>().Busy;
            if(loading) loadingFrames++;if(deleting) deletingFrames++;
            if(loading || deleting) largestFrameMs=Math.Max(largestFrameMs,Time.unscaledDeltaTime*1000);
            if(Time.unscaledTime>deadline) Finish(false,"Таймаут проверки большой библиотеки.");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=detail,
                loadingFrames=loadingFrames,deletingFrames=deletingFrames,largestFrameMs=largestFrameMs},true));
            Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Report
        {public bool success;public string detail;public int loadingFrames,deletingFrames;public float largestFrameMs;}
    }
}
