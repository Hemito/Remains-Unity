using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class MagicRecoveryChecks
    {
        public static void Run()
        {
            float acceleration=TelekinesisSettings.Load().recoveryAcceleration;
            foreach(int fps in new[]{30,60,144})
            {
                var actor=new ActorState {id="recovery",mana=0};var recovery=new MagicRecovery();
                for(int i=0;i<fps*2;i++) recovery.Step(actor,false,acceleration,1f/fps);
                Require(Mathf.Abs(actor.mana-4.5f)<.001f,"Восстановление за две секунды зависит от частоты кадров");
                float before=actor.mana;
                recovery.Step(actor,false,acceleration,0);
                Require(actor.mana==before,"Нулевое время изменило запас");
                recovery.Step(actor,true,acceleration,5);
                Require(actor.mana==before,"Удержание восстановило магию");
                recovery.Step(actor,false,acceleration,1);
                Require(Mathf.Abs(actor.mana-before-1.125f)<.001f,"Удержание не сбросило разгон восстановления");
                actor.generation++;before=actor.mana;
                recovery.Step(actor,false,acceleration,1);
                Require(Mathf.Abs(actor.mana-before-1.125f)<.001f,"Новый владелец унаследовал локальный разгон");
                recovery.Step(actor,false,acceleration,30);
                Require(actor.mana==100,"Запас не ограничен максимумом");
                actor.mana=90;recovery.Step(actor,false,acceleration,1);
                Require(Mathf.Abs(actor.mana-91.125f)<.001f,"Полный запас сохранил накопленный разгон");
            }
            Debug.Log("Восстановление магии: PASS — нормализованные единицы, 30/60/144 Гц, удержание, границы, смена владельца.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException(reason);}
    }
}
