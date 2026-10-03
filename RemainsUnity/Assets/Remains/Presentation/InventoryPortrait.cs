using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    // UI-only portrait: saved parts, no gameplay actor or runtime geometry analysis.
    public sealed class InventoryPortrait
    {
        private readonly GameObject root;
        private readonly PortraitPart[] parts;
        private int? hair;
        public InventoryPortrait(GameObject root,PortraitPart[] parts) {Content.PonyAppearance.Prepare();this.root=root;this.parts=parts;}
        public void Refresh(ActorState actor)
        {
            root.SetActive(actor!=null);
            if(actor==null || hair==actor.hair) return;
            hair=actor.hair;
            foreach(var part in parts) part.ApplyAppearance(actor.hair);
        }
    }
}
