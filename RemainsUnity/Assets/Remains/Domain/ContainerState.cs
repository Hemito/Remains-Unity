using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ContainerState
    {
        public const string PrototypeId="container:prototype";
        public string id=PrototypeId,level="factory:0";
        public float x=-1.5f,y;
        public List<ItemStack> items=new();
        public Vector2 Position=>new(x,y);
    }
    public static class ContainerAccess
    {
        public static string Rejection(ActorState actor,ContainerState container)
        {
            if(actor==null || !actor.connected || actor.down) return "Подбор недоступен: персонаж не может действовать.";
            if(container==null) return "Контейнер больше недоступен.";
            if(actor.level!=container.level) return "Контейнер находится в другой локации.";
            if(Vector2.Distance(actor.Position,container.Position)>2) return "Подойдите ближе к контейнеру.";
            return null;
        }
        public static ContainerState Nearest(WorldState world,ActorState actor)
        {
            if(world==null || actor==null) return null;
            ContainerState nearest=null;float distance=2;
            foreach(var container in world.containers)
            {
                if(container.level!=actor.level) continue;
                float next=Vector2.Distance(actor.Position,container.Position);
                if(next>distance) continue;
                distance=next;nearest=container;
            }
            return nearest;
        }
    }
}
