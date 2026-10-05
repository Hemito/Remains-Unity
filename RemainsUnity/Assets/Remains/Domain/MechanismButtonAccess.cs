using System;

namespace Remains.Domain
{
    public static class MechanismButtonAccess
    {
        public static string Rejection(ActorState actor,MechanismButtonState button,float accessRadius)
        {
            if(!float.IsFinite(accessRadius) || accessRadius<=0)
                throw new ArgumentOutOfRangeException(nameof(accessRadius),"Радиус кнопки должен быть конечным положительным числом.");
            if(actor==null) return "Взаимодействие недоступно: персонаж отсутствует.";
            if(button==null || button.removed) return "Кнопка больше недоступна.";
            if(!actor.connected) return "Взаимодействие отменено: персонаж отключён.";
            if(actor.down || actor.health<=0) return "Без сознания нельзя пользоваться кнопкой.";
            if(actor.level!=button.level) return "Кнопка находится в другой локации.";
            if(!float.IsFinite(actor.x) || !float.IsFinite(actor.y) || !float.IsFinite(actor.health) ||
                !float.IsFinite(button.x) || !float.IsFinite(button.y))
                return $"Взаимодействие недоступно: неверные координаты/здоровье actor={actor.id}, button={button.id}.";
            double dx=(double)actor.x-button.x,dy=(double)actor.y-button.y;
            return dx*dx+dy*dy>(double)accessRadius*accessRadius?"Кнопка вне досягаемости.":null;
        }
    }
}
