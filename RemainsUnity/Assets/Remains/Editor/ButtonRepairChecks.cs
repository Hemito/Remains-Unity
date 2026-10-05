using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class ButtonRepairChecks
    {
        public static void Run()=>Run(true);
        public static void Run(bool log)
        {
            var actor=Actor();var button=Button();
            actor.inventory.Add(new ItemStack {id="owned",definition="rep",count=2});
            button.extensions=new Dictionary<string,JToken>{{"mod:button-note",JObject.Parse("{\"kept\":[1,2,3]}")}};
            string beforeActor=StateJson.ToJson(actor),beforeButton=StateJson.ToJson(button);
            var progress=new ButtonRepairProgress();
            Require(progress.Begin("attempt:complete",actor,button,.75,1.5f).Outcome==ButtonRepairOutcome.InProgress && progress.Progress==0,
                "Begin не начинает таймер с нулевого прогресса");
            Require(progress.Step(actor,button,true,false,.25).Outcome==ButtonRepairOutcome.InProgress,"Ремонт завершился раньше времени");
            Require(progress.Step(actor,button,true,false,.25).Candidate==null,"Частичный прогресс выдал кандидат");
            var completed=progress.Step(actor,button,true,false,.25);
            Require(completed.Outcome==ButtonRepairOutcome.Completed && completed.Progress==1 && !progress.Active,"Не выдано единственное завершение");
            var candidate=completed.Candidate;
            Require(candidate!=null && candidate.AttemptId=="attempt:complete" && candidate.ActorId==actor.id && candidate.ButtonId==button.id &&
                candidate.ExpectedRevision==button.revision && candidate.ExpectedRepairState==MechanismRepairStates.NeedsRepair &&
                candidate.RepairState==MechanismRepairStates.Ready && candidate.MatchesActor(actor) && candidate.MatchesTarget(button),"Кандидат потерял захваченную идентичность");
            Require(StateJson.ToJson(actor)==beforeActor && StateJson.ToJson(button)==beforeButton,"Попытка изменила персонажа, предметы или сохранённую кнопку/данные мода");
            Require(progress.Step(actor,button,true,false,10).Outcome==ButtonRepairOutcome.Idle && progress.Step(actor,button,true,false,0).Candidate==null,
                "Повторный Step выдал второе завершение");
            button.revision++;Require(!candidate.MatchesTarget(button),"Кандидат принимает изменённую ревизию перед фиксацией");button.revision--;
            actor.damageSequence++;Require(!candidate.MatchesActor(actor),"Кандидат принимает повреждение перед фиксацией");actor.damageSequence--;

            CancelCase("Отпускание E",(_,_)=>{},held:false);
            CancelCase("Выход за радиус",(a,b)=>a.x=b.x+1.51f);
            CancelCase("Повреждение",(a,_)=>a.damageSequence++);
            CancelCase("Потеря сознания",(a,_)=>a.down=true);
            CancelCase("Нулевое здоровье",(a,_)=>a.health=0);
            CancelCase("Отключение",(a,_)=>a.connected=false);
            CancelCase("Другой персонаж",(a,_)=>a.id="replacement");
            CancelCase("Поколение персонажа",(a,_)=>a.generation++);
            CancelCase("Жизненный цикл",(a,_)=>a.lifecycleSequence++);
            CancelCase("Выход из уровня",(a,_)=>a.level="hub");
            CancelCase("Другой target",(_,b)=>b.id=b.level+":another-button");
            CancelCase("Уход target",(_,b)=>b.removed=true);
            CancelCase("Ревизия target",(_,b)=>b.revision++);
            CancelCase("Тип target",(_,b)=>b.definition="other-button");
            CancelCase("Группа target",(_,b)=>b.groupId="other-group");
            CancelCase("Комната target",(_,b)=>b.roomId="other-room");
            CancelCase("Связь target",(_,b)=>b.platformId=b.level+":other-platform");
            CancelCase("Положение target",(_,b)=>b.x+=.01f);
            CancelCase("Другая попытка уже починила кнопку",(_,b)=>b.repairState=MechanismRepairStates.Ready);
            CancelCase("Состояние нажатия target",(_,b)=>b.pressed=true);
            CheckPause();CheckFps();CheckDenials();CheckInvalidInputs();
            if(log) Debug.Log("Ремонт кнопки: удержание E, отмена по жизненному циклу/повреждению/радиусу, пауза, 30/60/144 FPS, единственный кандидат без изменения сохранённых данных — PASS.");
        }

        private static void CancelCase(string label,Action<ActorState,MechanismButtonState> interrupt,bool held=true)
        {
            var actor=Actor();var button=Button();var progress=new ButtonRepairProgress();
            progress.Begin("attempt:cancel",actor,button,1,1.5f);progress.Step(actor,button,true,false,.75);
            interrupt(actor,button);
            // A pause must not defer cancellation, even on the would-be completion sample.
            var result=progress.Step(actor,button,held,true,10);
            Require(result.Outcome==ButtonRepairOutcome.Cancelled && !string.IsNullOrWhiteSpace(result.Reason) && result.Candidate==null && !progress.Active,label);
            Require(progress.Step(actor,button,true,false,10).Outcome==ButtonRepairOutcome.Idle,label+": продолжение без Begin");
        }
        private static void CheckPause()
        {
            var actor=Actor();var button=Button();var progress=new ButtonRepairProgress();
            progress.Begin("attempt:pause",actor,button,2,1.5f);progress.Step(actor,button,true,false,.5);
            for(int n=0;n<100;n++) Require(progress.Step(actor,button,true,true,100).Outcome==ButtonRepairOutcome.InProgress,"Пауза завершила ремонт");
            Require(progress.ElapsedSeconds==.5 && progress.Progress==.25,"Пауза добавила время");
            Require(progress.Step(actor,button,true,false,0).ElapsedSeconds==.5,"Нулевой delta добавил время");
            Require(progress.Step(actor,button,true,false,1).Outcome==ButtonRepairOutcome.InProgress,"Возобновление догнало стенное время");
            Require(progress.Step(actor,button,true,false,.5).Outcome==ButtonRepairOutcome.Completed,"Возобновление потеряло накопленный прогресс");
            progress.Begin("attempt:released",actor,button,2,1.5f);progress.Step(actor,button,true,false,1.9);
            Require(progress.Step(actor,button,false,false,1).Outcome==ButtonRepairOutcome.Cancelled,"Отпускание в кадре завершения опоздало");
            progress.Begin("attempt:restart",actor,button,2,1.5f);
            Require(progress.Step(actor,button,true,false,.1).Outcome==ButtonRepairOutcome.InProgress && progress.ElapsedSeconds==.1,"Отменённое время перенеслось в новую попытку");
            Require(progress.Cancel("Выгрузка локации.").Outcome==ButtonRepairOutcome.Cancelled && !progress.Active,"Явная отмена жизненного цикла не очищает попытку");
        }
        private static void CheckFps()
        {
            foreach(int fps in new[]{30,60,144})
            {
                var actor=Actor();var button=Button();var progress=new ButtonRepairProgress();
                const double duration=2.137;
                progress.Begin("attempt:fps:"+fps,actor,button,duration,1.5f);
                double sampled=0;int completions=0;
                for(int n=0;n<fps*4;n++)
                {
                    sampled+=1d/fps;
                    var result=progress.Step(actor,button,true,false,1d/fps);
                    if(result.Outcome!=ButtonRepairOutcome.Completed) continue;
                    completions++;
                    Require(sampled>=duration && sampled<=duration+1d/fps+1e-10 && result.ElapsedSeconds==duration,
                        "Время завершения зависит от FPS="+fps);
                }
                Require(completions==1,"Количество завершений зависит от FPS="+fps);
            }
        }
        private static void CheckDenials()
        {
            var actor=Actor();var button=Button();var progress=new ButtonRepairProgress();
            foreach(string state in new[]{MechanismRepairStates.Ready,MechanismRepairStates.Broken,"mod:unknown"})
            {
                button.repairState=state;
                var result=progress.Begin("attempt:deny",actor,button,1,1.5f);
                Require(result.Outcome==ButtonRepairOutcome.Rejected && result.Reason!=null && !progress.Active && result.Candidate==null,"Состояние принято без правила: "+state);
            }
            button.repairState=MechanismRepairStates.NeedsRepair;
            actor.x=button.x+1.5f;Require(progress.Begin("attempt:boundary",actor,button,1,1.5f).Outcome==ButtonRepairOutcome.InProgress,"Точный радиус отклонён");
            progress.Cancel("Проверка завершена.");
            Require(progress.Begin("attempt:missing",actor,null,1,1.5f).Outcome==ButtonRepairOutcome.Rejected,"Отсутствующая кнопка принята");
            Require(progress.Begin("attempt:missing",null,button,1,1.5f).Outcome==ButtonRepairOutcome.Rejected,"Отсутствующий персонаж принят");
            progress=new ButtonRepairProgress(new RejectRepairRule());
            Require(progress.Begin("attempt:policy",actor,button,1,1.5f).Reason=="Отказ выбранного правила.","Выбранное правило подменено встроенным");
        }
        private static void CheckInvalidInputs()
        {
            var actor=Actor();var button=Button();var progress=new ButtonRepairProgress();
            foreach(double value in new[]{0d,-1d,double.NaN,double.PositiveInfinity})
                Reject(()=>progress.Begin("attempt:invalid",actor,button,value,1.5f),"Неверная длительность принята");
            foreach(float value in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
                Reject(()=>progress.Begin("attempt:invalid",actor,button,1,value),"Неверный радиус принят");
            Reject(()=>progress.Begin("",actor,button,1,1.5f),"Пустой ID попытки принят");
            progress.Begin("attempt:valid",actor,button,1,1.5f);progress.Step(actor,button,true,false,.25);
            Reject(()=>progress.Begin("attempt:other",actor,button,1,1.5f),"Активная попытка заменена незаметно");
            foreach(double value in new[]{-1d,double.NaN,double.PositiveInfinity})
            {
                Reject(()=>progress.Step(actor,button,true,false,value),"Неверный шаг принят");
                Require(progress.Active && progress.ElapsedSeconds==.25,"Неверный вход изменил активную попытку");
            }
            Require(progress.Step(actor,button,true,false,double.MaxValue).Outcome==ButtonRepairOutcome.Completed,"Большой конечный delta переполнил прогресс");
        }
        private sealed class RejectRepairRule : IButtonRepairRule
        {public string Rejection(ActorState actor,MechanismButtonState button,float accessRadius)=>"Отказ выбранного правила.";}
        private static ActorState Actor()=>new() {id="repairer",player=true,connected=true,health=100,x=3,y=2,generation=2,lifecycleSequence=4,damageSequence=7};
        private static MechanismButtonState Button()=>new() {id="factory:0:button",definition="knop3",level="factory:0",roomId="original:plant:18",groupId="d2",
            platformId="factory:0:platform",repairState=MechanismRepairStates.NeedsRepair,revision=5,x=3,y=2};
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка ремонта кнопки: "+reason);}
        private static void Reject(Action action,string reason)
        {
            try {action();}catch(Exception error) when(error is ArgumentException || error is InvalidOperationException) {return;}
            throw new InvalidOperationException("Проверка ремонта кнопки: "+reason);
        }
    }
}
