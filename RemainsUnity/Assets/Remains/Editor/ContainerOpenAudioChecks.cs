using System;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class ContainerOpenAudioChecks
    {
        public static void Run()
        {
            var catalog=ContainerOpenAudio.Load();
            foreach(var expected in new[]{("safe","container-safe-open"),("locker","container-metal-open")})
            {
                var entry=catalog.entries.Single(e=>e.definition==expected.Item1);
                Require(entry.clip=="Original/Audio/"+expected.Item2 && entry.gain==.5f,"Изменён явный исходный банк: "+expected.Item1);
                var clip=Resources.Load<AudioClip>(entry.clip);
                Require(clip!=null && clip.samples>0 && clip.frequency>0 && clip.channels>0,"Не импортирован исходный звук: "+entry.clip);
            }
            foreach(int fault in Enumerable.Range(0,6))
            {
                var invalid=new ContainerOpenAudio.Catalog {version=1,entries=new[]{new ContainerOpenAudio.Entry {definition="safe",clip="bank",gain=.5f}}};
                if(fault==0)invalid.version=0;
                if(fault==1)invalid.entries=null;
                if(fault==2)invalid.entries[0].definition="";
                if(fault==3)invalid.entries[0].clip="";
                if(fault==4)invalid.entries[0].gain=float.NaN;
                if(fault==5)invalid.entries=new[]{invalid.entries[0],invalid.entries[0]};
                bool refused=false;try {ContainerOpenAudio.Validate(invalid);}catch(InvalidOperationException){refused=true;}
                Require(refused,"Некорректный модовый звуковой каталог не отклонён: case="+fault);
            }
            Debug.Log("Открывание контейнеров: PASS — исходные импортированные банки сейфа/шкафчика и шесть отказов данных; игровое воспроизведение проверяется отдельно.");
        }
        private static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException("Звуки контейнеров: "+reason);}
    }
}
