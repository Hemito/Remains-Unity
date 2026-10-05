using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable] public sealed class ActorHoldState
    {
        public string holder,token;
        public bool pendingAttack;
        public uint sequence;
        public float targetX,targetY;
        public Vector2 Target=>new(targetX,targetY);
    }
    public static class ActorHoldRules
    {
        public static bool Active(ActorState body)=>body?.hold!=null && !body.hold.pendingAttack;
        public static string Rejection(WorldState world,ActorState holder,ActorState body)
        {
            if(holder==null || !holder.connected || holder.down) return "Держатель не может использовать телекинез.";
            if(body==null || !ActorPresence.IsPresent(body)) return "Тело больше недоступно.";
            if(body.id==holder.id) return "Нельзя поднять себя телекинезом.";
            if(body.level!=holder.level) return "Тело находится в другой локации.";
            if(holder.hold!=null) return "Нельзя захватывать тело, пока удерживают вас.";
            foreach(var actor in world.actors)
            {
                if(actor.hold?.holder==body.id) return "Нельзя захватывать другого держателя.";
                if(actor.hold?.holder==holder.id) return "Вы уже удерживаете другое тело.";
            }
            foreach(var prop in WorldProps.All(world))
                if(prop.hasPhysicalBody && prop.movable && prop.held && prop.owner==holder.owner) return "Сначала отпустите удерживаемый предмет.";
            if(body.hold!=null) return "Тело уже удерживает другой игрок.";
            return null;
        }
        public static void Begin(ActorState holder,ActorState body,string token)
        {
            if(string.IsNullOrEmpty(token)) throw new ArgumentException("Не задан ID захвата тела.",nameof(token));
            var center=new TelekinesisTarget(body).Center;
            body.hold=new ActorHoldState {holder=holder.id,token=token,targetX=center.x,targetY=center.y,
                pendingAttack=body.attackBusy};
            body.holdEpoch++;body.revision++;
            if(!body.player && !body.down) body.target=holder.id;
        }
        public static bool Release(ActorState body,string holder=null,string token=null)
        {
            if(body?.hold==null || holder!=null && body.hold.holder!=holder || token!=null && body.hold.token!=token) return false;
            body.hold=null;body.holdEpoch++;body.revision++;return true;
        }
        public static bool SetTarget(ActorState body,string holder,int epoch,uint sequence,Vector2 target)
        {
            if(body?.hold==null || body.holdEpoch!=epoch || body.hold.holder!=holder ||
                unchecked((int)(sequence-body.hold.sequence))<=0 || !float.IsFinite(target.x) || !float.IsFinite(target.y)) return false;
            body.hold.targetX=target.x;body.hold.targetY=target.y;body.hold.sequence=sequence;return true;
        }
        public static void PreserveNewerTarget(ActorState current,ActorState incoming)
        {
            if(current?.hold==null || incoming.hold==null || current.holdEpoch!=incoming.holdEpoch ||
                current.hold.token!=incoming.hold.token || unchecked((int)(current.hold.sequence-incoming.hold.sequence))<=0) return;
            incoming.hold.targetX=current.hold.targetX;incoming.hold.targetY=current.hold.targetY;
            incoming.hold.sequence=current.hold.sequence;
        }
        public static bool Reconcile(WorldState world,IReadOnlyCollection<string> owners)
        {
            bool changed=false;
            foreach(var body in world.actors)
            {
                if(body.hold==null) continue;
                var holder=world.Find(body.hold.holder);
                if(body.hold.pendingAttack && body.down && body.attackBusy) NpcAttackRules.Cancel(body);
                if(holder==null || !holder.connected || holder.down || holder.mana<=0 || !owners.Contains(holder.owner) ||
                    holder.level!=body.level || !ActorPresence.IsPresent(body)) changed|=Release(body);
                else if(body.hold.pendingAttack && !body.attackBusy)
                {body.hold.pendingAttack=false;body.revision++;changed=true;}
            }
            return changed;
        }
    }
}
