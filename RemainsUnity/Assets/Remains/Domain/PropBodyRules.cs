using System;

namespace Remains.Domain
{
    // Mobility, physical participation and an inventory's interaction rectangle are separate contracts.
    public static class PropBodyRules
    {
        public static void Validate(bool movable,bool hasPhysicalBody,float mass,string context)
        {
            if(!float.IsFinite(mass) || (hasPhysicalBody?mass<=0:mass<0))
                throw new InvalidOperationException($"Некорректная масса объекта: {context}, mass={mass}, hasPhysicalBody={hasPhysicalBody}.");
            if(!hasPhysicalBody && movable)
                throw new InvalidOperationException($"Объект без физического тела нельзя перемещать: {context}.");
        }
        public static void Validate(PropState prop)
        {
            if(prop==null) throw new InvalidOperationException("Отсутствует состояние объекта при проверке физического тела.");
            string context=$"id={prop.id}, type={prop.definition}, level={prop.level}";
            Validate(prop.movable,prop.hasPhysicalBody,prop.mass,context);
            if(!prop.movable && prop.held)
                throw new InvalidOperationException($"Закреплённый объект не может удерживаться: {context}, held={prop.held}.");
            if(!prop.hasPhysicalBody && (prop.vx!=0 || prop.vy!=0))
                throw new InvalidOperationException($"У объекта без физического тела не может быть скорости: {context}, velocity=({prop.vx};{prop.vy}).");
        }
        public static void ValidateWorld(WorldState world)
        {
            if(world==null || world.props==null || world.containers==null)
                throw new InvalidOperationException("Нет мира или списков его объектов при проверке физических тел.");
            foreach(var prop in WorldProps.All(world)) Validate(prop);
        }
    }
}
