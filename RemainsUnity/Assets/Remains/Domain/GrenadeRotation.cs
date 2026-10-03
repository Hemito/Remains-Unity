using System;
using UnityEngine;

namespace Remains.Domain
{
    // Presentation motion shares the flight clock, but never changes contacts or damage.
    public static class GrenadeRotation
    {
        public static void Prepare(GrenadeState state)
        {
            if(state.rotationVersion==0)
            {
                Debug.LogWarning($"Граната {state.id}: старое состояние без вращения. Сохранены прежний нулевой угол и отсутствие вращения; позиция и запал не изменены.");
                state.angle=0;state.angularVelocity=0;state.mirrored=false;state.rotationVersion=1;
            }
            if(state.rotationVersion!=1 || !float.IsFinite(state.angle) || !float.IsFinite(state.angularVelocity))
                throw new InvalidOperationException($"Некорректное вращение гранаты {state.id}: версия={state.rotationVersion}, угол={state.angle}, скорость={state.angularVelocity}.");
        }
        public static void Advance(GrenadeState state,float dt)
        {
            double angle=((double)state.angle+(double)state.angularVelocity*dt)%360;
            state.angle=(float)(angle<0?angle+360:angle);
        }
    }
}
