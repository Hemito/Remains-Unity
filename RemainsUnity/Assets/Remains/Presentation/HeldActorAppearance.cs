using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    // Confirmed hold/color shared by all body renderers; never a source of physical hold state.
    public sealed class HeldActorAppearance
    {
        public bool Active {get;private set;}
        public Color Color {get;private set;}
        private string rejection;
        public bool Apply(ActorState actor,HeldPropColorSource colors,Object context)
        {
            bool active=false;Color color=default;string reason=null;
            if(actor!=null && ActorHoldRules.Active(actor))
            {
                active=colors!=null && colors.TryResolve(actor,out color,out reason);
                if(!active)
                {
                    reason=colors==null?"Нет источника цвета держателя.":reason;
                    if(rejection!=reason)Debug.LogWarning($"Свечение тела пропущено: actor={actor.id}, type={actor.actorType}, holder={actor.hold.holder}; {reason}",context);
                }
            }
            rejection=reason;bool changed=active!=Active || active && color!=Color;Active=active;Color=color;return changed;
        }
        public Color Tint(Color original)=>Active?new Color(Mathf.Lerp(original.r,Color.r,.6f),Mathf.Lerp(original.g,Color.g,.6f),Mathf.Lerp(original.b,Color.b,.6f),original.a):original;
    }
}
