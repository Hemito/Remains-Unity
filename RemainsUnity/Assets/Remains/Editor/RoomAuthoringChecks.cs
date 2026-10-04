using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomAuthoringChecks
    {
        public static void Run()
        {
            var source=RoomAuthoring.Load("factory:0");var instance=UnityEngine.Object.Instantiate(source);
            try
            {
                var authored=instance.CreateLayout();string authoredBefore=Remains.Serialization.StateJson.ToJson(authored);
                instance.Configure(authored);
                var serialized=new UnityEditor.SerializedObject(instance);
                if(serialized.FindProperty("settings").FindPropertyRelative("artwork").arraySize!=0 ||
                    authoredBefore!=Remains.Serialization.StateJson.ToJson(instance.CreateLayout()))
                    throw new InvalidOperationException("Настройка авторской комнаты создала вторую копию оформления или изменила дочерние данные.");
                string gameplayBefore=Remains.Serialization.StateJson.ToJson(instance.CreateLayout(includeArtwork:false));
                var decoration=instance.GetComponentInChildren<RoomArtworkAuthoring>();
                if(decoration==null) throw new InvalidOperationException("Проверка комнаты требует авторского декоративного слоя.");
                decoration.enabled=false;
                if(gameplayBefore!=Remains.Serialization.StateJson.ToJson(instance.CreateLayout(includeArtwork:false)))
                    throw new InvalidOperationException("Декоративный слой изменил игровой манифест комнаты.");
                decoration.enabled=true;
                var original=source.CreateLayout();var floor=instance.GetComponentsInChildren<RoomSurfaceAuthoring>()[0];
                floor.transform.localPosition+=Vector3.up;
                if(gameplayBefore==Remains.Serialization.StateJson.ToJson(instance.CreateLayout(includeArtwork:false)))
                    throw new InvalidOperationException("Изменение опоры не отражено в игровом манифесте комнаты.");
                var changed=instance.CreateLayout();
                if(!RoomSupport.IsSupported(changed,new Vector2(0,1)) || RoomSupport.IsSupported(changed,Vector2.zero))
                    throw new InvalidOperationException("Перемещение поверхности в редакторе не изменило опору симуляции.");
                if(!RoomSupport.IsSupported(source.CreateLayout(),Vector2.zero)) throw new InvalidOperationException("Правка экземпляра затронула префаб уровня.");
                var ladder=instance.GetComponentInChildren<RoomLadderAuthoring>();ladder.transform.localScale=new Vector3(1,5,1);
                if(instance.CreateLayout().ladders[0].top!=5) throw new InvalidOperationException("Масштаб лестницы не задаёт высоту движения.");
                var ladderImage=ladder.GetComponentInChildren<MeshRenderer>();
                if(ladderImage==null) throw new InvalidOperationException("Графика лестницы не находится внутри её объекта.");
                float beforeImageX=ladderImage.transform.position.x;float beforeAxisX=instance.CreateLayout().ladders[0].x;
                ladder.transform.localPosition+=Vector3.right;
                if(Mathf.Abs(ladderImage.transform.position.x-beforeImageX-1)>.00001f || Mathf.Abs(instance.CreateLayout().ladders[0].x-beforeAxisX-1)>.00001f)
                    throw new InvalidOperationException("Изображение лестницы и ось движения расходятся после переноса.");
                ladder.gameObject.SetActive(false);
                if(instance.CreateLayout().ladders.Length!=0) throw new InvalidOperationException("Отключённая лестница осталась в симуляции.");
                floor.transform.localRotation=Quaternion.Euler(0,0,15);bool rejected=false;
                try {instance.CreateLayout();} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Неподдерживаемый наклон поверхности молча принят.");
            }
            finally {UnityEngine.Object.DestroyImmediate(instance.gameObject);}
            Debug.Log("Авторинг комнаты: PASS — Transform влияет на опору и лестницу, отключение и защита префаба, явный отказ наклона.");
        }
    }
}
