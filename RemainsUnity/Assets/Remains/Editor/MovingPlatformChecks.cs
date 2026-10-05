using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class MovingPlatformChecks
    {
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            var type=Definition();var state=State();state.Validate(type);
            var motion=new MovingPlatformMotion(type);
            string before=StateJson.ToJson(state);
            Check(motion.Evaluate(state,9).Anchor==state.Start && !motion.Evaluate(state,9).Started,"Опора двинулась до начала segment.");
            var start=motion.Evaluate(state,10);var middle=motion.Evaluate(state,13);var end=motion.Evaluate(state,16);
            Check(start.Anchor==state.Start && start.Started && start.Moving && start.Progress==0,"Неверно начало перехода.");
            Check(Near(middle.Anchor,new Vector2(57.6f,9.62f)) && middle.Progress==.5 && middle.Moving,"Неверна середина перехода.");
            Check(end.Anchor==state.End && end.Completed && !end.Moving && motion.Evaluate(state,900).Anchor==state.End,"Переход не остановился на конце.");
            Check(StateJson.ToJson(state)==before,"Evaluator изменил сохранённое состояние.");
            Check(Math.Abs(middle.SupportBounds.width-2.4f)<.00001f && Math.Abs(middle.SupportBounds.yMax-10.4f)<.00001f &&
                Math.Abs(middle.GraphicBounds.height-.7f)<.00001f && Math.Abs(middle.GraphicBounds.yMin-9.72f)<.00001f,"Рисунок подменил физические bounds/anchor.");
            foreach(int fps in new[]{30,60,144})
            {
                double elapsed=10;for(int i=0;i<fps*3;i++) elapsed+=1d/fps;
                Check(Near(motion.Evaluate(state,elapsed).Anchor,middle.Anchor),"Позиция зависит от FPS="+fps);
                var copied=StateJson.Copy(state);
                Check(Near(new MovingPlatformMotion(StateJson.Copy(type)).Evaluate(copied,elapsed).Anchor,middle.Anchor),"Midsegment save/reload зависит от FPS="+fps);
            }
            var reverse=StateJson.Copy(state);reverse.endpoint=1;reverse.segment.fromEndpoint=1;reverse.segment.toEndpoint=0;
            Check(motion.Evaluate(reverse,10).Anchor==state.End && motion.Evaluate(reverse,16).Anchor==state.Start &&
                Near(motion.Evaluate(reverse,13).Anchor,middle.Anchor),"Обратный одиночный переход неверен.");
            var stationary=StateJson.Copy(state);stationary.segment=null;stationary.endpoint=1;
            Check(motion.Evaluate(stationary,1000).Anchor==state.End && !motion.Evaluate(stationary,1000).Moving,"Стоящая платформа получила цикл.");
            foreach(double invalid in new[]{-1d,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                Reject(()=>motion.Evaluate(state,invalid),"Приняты неверные часы уровня.");
            var invalidState=StateJson.Copy(state);invalidState.endpoint=2;Reject(()=>invalidState.Validate(),"Принят endpoint2.");
            invalidState=StateJson.Copy(state);invalidState.segment.toEndpoint=0;Reject(()=>invalidState.Validate(),"Принят segment в тот же endpoint.");
            invalidState=StateJson.Copy(state);invalidState.segment.startElapsedSeconds=-1;Reject(()=>invalidState.Validate(),"Принято отрицательное начало.");
            invalidState=StateJson.Copy(state);invalidState.segment.durationSeconds=0;Reject(()=>invalidState.Validate(),"Принята нулевая длительность.");
            invalidState=StateJson.Copy(state);invalidState.segment.startElapsedSeconds=double.MaxValue;Reject(()=>invalidState.Validate(),"Не представим конец перехода.");
            invalidState=StateJson.Copy(state);invalidState.segment.sequence=0;Reject(()=>invalidState.Validate(),"Принят segment без sequence.");
            invalidState=StateJson.Copy(state);invalidState.segment.durationSeconds=7;Reject(()=>motion.Evaluate(invalidState,13),"Версия определения потеряла длительность.");
            invalidState=StateJson.Copy(state);invalidState.definition="missing:platform";Reject(()=>motion.Evaluate(invalidState,13),"Неизвестный type подменён.");
            invalidState=StateJson.Copy(state);invalidState.definitionVersion=2;Reject(()=>motion.Evaluate(invalidState,13),"Смешаны версии type.");
            invalidState=StateJson.Copy(state);invalidState.schema=2;Reject(()=>invalidState.Validate(),"Неизвестная схема принята.");
            invalidState=StateJson.Copy(state);invalidState.startX=float.NaN;Reject(()=>invalidState.Validate(),"Неверные точки пути приняты.");
            var invalidType=Definition();invalidType.width=0;Reject(()=>invalidType.Validate(),"Неверная опора принята.");
            invalidType=Definition();invalidType.surfaceTexture=" ";Reject(()=>invalidType.Validate(),"Пустой surfaceTexture принят.");
            invalidType=Definition();invalidType.movementSound=null;Reject(()=>invalidType.Validate(),"Отсутствующий movementSound принят.");
            var policy=Definition();policy.oneWay=true;policy.shotsPassThrough=true;
            var copiedPolicy=StateJson.Copy(policy);
            Check(copiedPolicy.oneWay && copiedPolicy.shotsPassThrough && copiedPolicy.surfaceTexture=="floor" && copiedPolicy.movementSound=="metal",
                "Collision/audio data определения потеряны при reload.");
            invalidType=Definition();invalidType.graphicHeight=float.PositiveInfinity;Reject(()=>invalidType.Validate(),"Неверный рисунок принят.");
            var removed=StateJson.Copy(state);removed.removed=true;removed.Validate();removed=StateJson.Copy(removed);
            Check(removed.removed && removed.segment!=null,"Сериализация воскресила/стерла tombstone.");
            Reject(()=>motion.Evaluate(removed,13),"Removed создал живую опору.");
            CheckScopesAndButtons();CheckExtensions(type,state);
            if(log) Debug.Log("Платформа: PASS — чистый evaluator, 30/60/144 FPS, endpoints, midsegment save, clocks, scope, tombstone и unknown extensions. Gameplay/ремонт/сеть не проверяются.");
        }
        private static void CheckScopesAndButtons()
        {
            var ids=new HashSet<string>();
            foreach(string level in new[]{"factory:0","factory:6","factory:-27"})
            {
                var platform=State(level);var button=Button(platform);button.ValidateLink(platform);
                Check(ids.Add(platform.id) && ids.Add(button.id),"Индексы фабрик разделяют ID.");
                var elsewhere=State(level=="factory:0"?"factory:6":"factory:0");
                Reject(()=>button.ValidateLink(elsewhere),"Связаны d2 разных фабрик.");
                var foreignRoom=State(level);foreignRoom.roomId="original:plant:19";
                Reject(()=>button.ValidateLink(foreignRoom),"Связаны d2 разных комнат.");
                var foreignGroup=State(level);foreignGroup.groupId="d3";
                Reject(()=>button.ValidateLink(foreignGroup),"Связаны разные группы.");
                var changed=StateJson.Copy(button);changed.level="hub";Reject(()=>changed.Validate(),"ID не принадлежит level.");
                changed=StateJson.Copy(button);changed.repairState="mod:unregistered";
                Reject(()=>changed.Validate(),"Неизвестный repair ID заменён готовой кнопкой.");
                changed.ValidateLink(platform,id=>MechanismRepairStates.BuiltIn(id) || id=="mod:unregistered");
                Check(changed.repairState=="mod:unregistered","Модовое состояние переписано.");
                var removedButton=StateJson.Copy(button);removedButton.removed=true;
                Check(StateJson.Copy(removedButton).removed,"Кнопка воскресла после roundtrip.");
                foreach(string repair in new[]{MechanismRepairStates.NeedsRepair,MechanismRepairStates.Ready,MechanismRepairStates.Broken})
                {button.repairState=repair;button.ValidateLink(platform);Check(StateJson.Copy(button).repairState==repair,"Потеряно состояние ремонта.");}
            }
        }
        private static void CheckExtensions(MovingPlatformDefinition type,MovingPlatformState state)
        {
            var button=Button(state);
            IDictionary<string,JToken> Unknown(string key)
            {
                using var reader=new JsonTextReader(new StringReader("{\"nested\":[17,\"данные мода\",{\"precise\":1234567890123456789.125}]}"))
                    {FloatParseHandling=FloatParseHandling.Decimal};
                return new Dictionary<string,JToken>{{key,JToken.ReadFrom(reader)}};
            }
            type.extensions=Unknown("modType");state.extensions=Unknown("modPlacement");
            state.segment.extensions=Unknown("modSegment");button.extensions=Unknown("modButton");
            var copiedType=StateJson.Copy(type);var copiedState=StateJson.Copy(state);var copiedButton=StateJson.Copy(button);
            Check(JToken.DeepEquals(type.extensions["modType"],copiedType.extensions["modType"]) &&
                JToken.DeepEquals(state.extensions["modPlacement"],copiedState.extensions["modPlacement"]) &&
                JToken.DeepEquals(state.segment.extensions["modSegment"],copiedState.segment.extensions["modSegment"]) &&
                JToken.DeepEquals(button.extensions["modButton"],copiedButton.extensions["modButton"]),"Неизвестные данные потеряны на одном из уровней.");
            string before=StateJson.ToJson(copiedState);
            new MovingPlatformMotion(copiedType).Evaluate(copiedState,13);
            Check(StateJson.ToJson(copiedState)==before,"Sampling изменил unknown поля.");
            type.extensions=new Dictionary<string,JToken>{{"width",new JValue(99)}};
            Reject(()=>StateJson.ToJson(type),"Конфликт unknown с известным полем принят.");
        }
        private static MovingPlatformDefinition Definition()=>new() {id="platform2",version=1,width=2.4f,height=.78f,
            graphicOffsetX=-1.2f,graphicOffsetY=.1f,graphicWidth=2.4f,graphicHeight=.7f,durationSeconds=6,surfaceTexture="floor",movementSound="metal"};
        private static MovingPlatformState State(string level="factory:0")=>new() {
            id=level+":original:plant:18:TFAERLMCSehXRcAt",definition="platform2",definitionVersion=1,level=level,roomId="original:plant:18",groupId="d2",
            startX=49.2f,startY=9.62f,endX=66,endY=9.62f,endpoint=0,revision=1,
            segment=new MovingPlatformSegment {sequence=1,fromEndpoint=0,toEndpoint=1,startElapsedSeconds=10,durationSeconds=6}};
        private static MechanismButtonState Button(MovingPlatformState platform)=>new() {
            id=platform.level+":original:plant:18:p7r4AoLcL0QxKFy7",definition="knop3",level=platform.level,roomId=platform.roomId,groupId=platform.groupId,
            platformId=platform.id,repairState=MechanismRepairStates.NeedsRepair,x=46,y=11.22f};
        private static bool Near(Vector2 left,Vector2 right)=>Math.Abs(left.x-right.x)<.00001f && Math.Abs(left.y-right.y)<.00001f;
        private static void Check(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Проверка платформы: "+reason);}
        private static void Reject(Action action,string reason)
        {try {action();} catch(Exception error) when(ValidationError(error)) {return;} throw new InvalidOperationException(reason);}
        private static bool ValidationError(Exception error)=>error is InvalidOperationException || error is InvalidDataException || error is JsonException ||
            error is System.Reflection.TargetInvocationException && error.InnerException!=null && ValidationError(error.InnerException);
    }
}
