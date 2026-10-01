using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    public static class TelekinesisRuleChecks
    {
        public static void Run()
        {
            var settings=TelekinesisSettings.Load();var rule=new MassLimitedTelekinesisRule(settings);
            var actor=new ActorState();var prop=new PropState {id="mass-check",mass=30,x=12,y=0};
            Require(rule.Rejection(actor,prop,false)==null,"Граница 30 кг / 12 м должна разрешать захват");
            prop.mass=30.01f;Require(rule.Rejection(actor,prop,false)?.Contains("тяжело")==true,"Масса выше предела должна объяснять отказ");
            prop.mass=25;prop.x=12.01f;
            Require(rule.Rejection(actor,prop,false)!=null && rule.Rejection(actor,prop,true)==null,"Удержание должно иметь запас дальности");
            prop.x=13.15f;Require(rule.Rejection(actor,prop,true)!=null,"Выход за дальность удержания отпускает предмет");
            prop.x=0;prop.movable=false;Require(rule.Rejection(actor,prop,false)?.Contains("закреплён")==true,"Закреплённый контейнер не переносится");
            prop.movable=true;actor.mana=0;Require(rule.Rejection(actor,prop,true)!=null,"Исчерпание маны прекращает удержание");
            actor.mana=100;actor.down=true;Require(rule.Rejection(actor,prop,true)!=null,"Потеря сознания прекращает удержание");actor.down=false;
            prop.mass=5;Require(rule.ManaPerSecond(actor,prop)==0,"До 5 кг включительно удержание бесплатно");
            prop.mass=25;Require(Mathf.Abs(rule.ManaPerSecond(actor,prop)-21.213203f)<.0001f,"Расход 25 кг должен быть 21,2132 маны в секунду");
            foreach(int fps in new[]{30,60,144})
            {
                float mana=100;
                for(int i=0;i<fps*2;i++) mana=Mathf.Max(0,mana-rule.ManaPerSecond(actor,prop)/fps);
                Require(Mathf.Abs(mana-57.5736f)<.002f,"Расход маны не должен зависеть от частоты кадров");
            }
            Require(Mathf.Abs(Vector2.Distance(rule.Target(actor,new Vector2(30,40)),actor.Position)-12)<.0001f,"Цель ограничена радиусом");
            bool rejected=false;settings.freeMass=31;
            try {settings.Validate();} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Некорректный профиль не должен загружаться");
            Debug.Log("Правила телекинеза: PASS — массы, радиусы, отпускание, бесплатный порог и независимый от частоты кадров расход.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException("Правила телекинеза: "+message);}
    }
}
