using System;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Install the single color source before the first world presentation. The
    // view coordinator rebuilds/applies it before drawing each new prop epoch.
    public sealed class HeldPropGlowPresentation : MonoBehaviour
    {
        private bool initialized;
        private readonly HeldPropColorSource colors=new();
        public HeldPropColorSource Colors=>colors;
        public void Initialize(CoopSession value,InteractionVisuals views)
            =>Initialize(value,views,null);
        public void Initialize(CoopSession value,InteractionVisuals views,PonyMagicActivitySource magic)
        {
            if(initialized) throw new InvalidOperationException("Цвета удержания уже подключены.");
            if(value==null) throw new ArgumentNullException(nameof(value));
            if(views==null) throw new ArgumentNullException(nameof(views));
            views.ConfigureHeldAppearance(colors,magic);initialized=true;
        }
    }
}
