using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class InventoryAudioChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка звука инвентаря");
            var preferences=new AudioPreferences(null);
            try
            {
                root.AddComponent<InventoryAudio>().Initialize(preferences);
                var source=root.GetComponent<AudioSource>();
                Require(Mathf.Approximately(source.volume,.1f),"Номинальный уровень должен учитывать общий микс эффектов.");
                Require(source.spatialBlend==0 && source.ignoreListenerPause && !source.playOnAwake,
                    "Звук интерфейса должен быть непространственным, независимым от паузы и без автозапуска.");
                preferences.Preview(new AudioLevels {master=.5f,effects=.4f,music=0});
                Require(Mathf.Approximately(source.volume,.02f),"Изменение настроек должно обновлять существующий источник.");
                preferences.Preview(new AudioLevels {effects=0});
                Require(source.volume==0,"Выключение эффектов должно выключать звук инвентаря.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("Звук инвентаря: "+message);}
    }
}
