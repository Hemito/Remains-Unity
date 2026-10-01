using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class MotionAudioChecks
    {
        public static void Run()
        {
            foreach(int rate in new[]{30,60,144})
            {
                var track=new MotionContacts();int contacts=0;
                for(int frame=0;frame<=rate*2;frame++)
                    if(Sample(track,new Vector2(4.2f*frame/rate,0))==MotionContact.Step) contacts++;
                Require(contacts>=11 && contacts<=12,$"Частота кадров меняет число шагов: fps={rate}, count={contacts}.");
                for(int frame=0;frame<rate;frame++) Require(Sample(track,new Vector2(8.4f,0))==MotionContact.None,"Покой создаёт шаги.");
            }
            var jump=new MotionContacts();Sample(jump,Vector2.zero);
            Require(Sample(jump,new Vector2(.5f,1),false)==MotionContact.None,"Шаг в полёте.");
            Require(Sample(jump,new Vector2(1,0))==MotionContact.Landing,"Нет контакта приземления.");
            Require(Sample(jump,new Vector2(1,0))==MotionContact.None,"Повторное приземление в покое.");
            Require(Sample(jump,new Vector2(20,0))==MotionContact.None,"Телепорт создаёт шаги.");
            Require(Sample(jump,new Vector2(21,0),suppressed:true)==MotionContact.None,"Пауза/нокаут создаёт шаги.");
            Require(Sample(jump,new Vector2(21,0))==MotionContact.None,"Возобновление создаёт отложенный шаг.");
            Require(Sample(jump,new Vector2(22,0),level:"factory:0")==MotionContact.None,"Смена локации создаёт шаги.");
            Require(Sample(jump,new Vector2(23,0),level:"factory:0",generation:1)==MotionContact.None,"Смена владельца создаёт шаги.");
            var ladder=new MotionContacts();Sample(ladder,Vector2.zero,climbing:true);
            Require(Sample(ladder,new Vector2(0,.6f),climbing:true)==MotionContact.Ladder,"Нет контакта лестницы.");
            Require(Sample(ladder,new Vector2(0,.6f),climbing:true)==MotionContact.None,"Неподвижная лестница создаёт шаги.");
            Debug.Log("Звуки движения: PASS — частота кадров, покой, полёт, приземление, лестница, пауза, перенос.");
        }
        private static MotionContact Sample(MotionContacts track,Vector2 position,bool grounded=true,bool climbing=false,
            bool suppressed=false,string level="hub",int generation=0)=>
            track.Sample(position,level,generation,grounded,climbing,suppressed,.7f,.5f,3);
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
