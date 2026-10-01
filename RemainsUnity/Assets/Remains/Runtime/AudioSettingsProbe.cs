using System;
using System.Collections;
using System.IO;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class AudioSettingsProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        public void Initialize(PrototypeRuntime value) {runtime=value;deadline=Time.unscaledTime+40;}
        private IEnumerator Start()
        {
            yield return null;
            if(runtime.Options.Smoke.Contains("-resume"))
            {
                Require(Near(runtime.Audio.Volume,.325f) && Near(runtime.Music.Volume,.05f),"После перезапуска громкость не восстановилась.");
                finished=true;File.WriteAllText(runtime.Options.Report,"{\"success\":true,\"restart\":true}");Application.Quit(0);yield break;
            }
            var hud=runtime.Hud;var panel=hud.AudioSettings;
            yield return UiPointerProbe.Click(hud.MainMenuSoundRect);
            yield return UiPointerProbe.Click(panel.MasterRect);
            Require(panel.IsOpen && Near(runtime.Audio.Volume,.325f) && Near(runtime.Music.Volume,.1f),"Общая громкость не применяется из главного меню.");
            yield return UiPointerProbe.Click(panel.CancelRect);
            Require(!panel.IsOpen && Near(runtime.Audio.Volume,.65f) && Near(runtime.Music.Volume,.2f),"Отмена не восстановила громкость.");
            yield return UiPointerProbe.Click(hud.MainMenuSoundRect);
            yield return UiPointerProbe.Click(panel.MasterRect);
            yield return UiPointerProbe.Click(panel.MusicRect);
            Require(Near(runtime.Music.Volume,.05f) && Near(runtime.Audio.Volume,.325f),"Регулятор музыки меняет игровые звуки.");
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".menu.png",hud.GetComponent<Canvas>());
            yield return UiPointerProbe.Click(panel.SaveRect);
            var saved=new AudioPreferences(Path.Combine(runtime.Options.SaveDirectory,"Settings","audio.json")).Current;
            Require(Near(saved.master,.5f) && Near(saved.music,.5f) && Near(saved.effects,1),"Настройки не сохраняются отдельно от персонажа.");
            runtime.Session.StartHost();
            while(runtime.Session.LocalActor==null && !finished) yield return null;
            if(finished) yield break;
            hud.PauseMenu.Toggle();yield return null;
            yield return UiPointerProbe.Click(hud.PauseMenu.SoundRect);
            Require(panel.IsOpen && hud.BlocksGameplayInput && !runtime.Session.Pause.IsPaused(runtime.Session.LocalActor.level),"Настройки не открылись в сессии или неявно остановили уровень.");
            NetworkFrameCapture.Save(Camera.main,runtime.Options.Report+".session.png",hud.GetComponent<Canvas>());
            hud.ToggleMenu();yield return null;
            Require(!panel.IsOpen && hud.PauseMenu.IsOpen,"Escape закрыл родительское меню вместо настроек.");
            var previous=runtime.AudioPreferences.Current;
            runtime.AudioPreferences.Preview(new AudioLevels {master=0,music=1,effects=1});
            Require(runtime.Audio.Volume==0 && runtime.Music.Volume==0,"Нулевая общая громкость не выключает оба канала.");
            runtime.AudioPreferences.Preview(previous);
            Require(Array.TrueForAll(FindObjectsByType<AudioSource>(FindObjectsSortMode.None),source=>source.mute),"Фоновая проверка не заглушена.");
            finished=true;File.WriteAllText(runtime.Options.Report,"{\"success\":true,\"menu\":true,\"session\":true,\"previewCancel\":true,\"persistence\":true,\"muted\":true}");
            Application.Quit(0);
        }
        private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.005f;
        private void Require(bool valid,string reason)
        {if(!valid) {Fail(reason);throw new InvalidOperationException(reason);}}
        private void Update() {if(!finished && Time.unscaledTime>deadline) Fail("Таймаут проверки настроек звука.");}
        private void Fail(string reason)
        {finished=true;Debug.LogError(reason);File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Failure {detail=reason}));Application.Quit(2);}
        [Serializable] private sealed class Failure {public bool success=false;public string detail;}
    }
}
