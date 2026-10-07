using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    // UI-only portrait: saved parts, no gameplay actor or runtime geometry analysis.
    public sealed class InventoryPortrait
    {
        private readonly GameObject root;
        private readonly PortraitPart[] parts;
        public PortraitPart[] Parts=>parts;
        private Content.PonyAppearanceKey? appearance;
        private Content.WearableDrawPlan plan;
        public Content.WearableDrawPlan CurrentDrawPlan=>plan;
        public InventoryPortrait(GameObject root,PortraitPart[] parts)
        {
            Content.PonyRenderSelection.Prepare();
            if(root==null || parts==null || parts.Length==0) throw new System.InvalidOperationException("Нет сохранённого пула портрета.");
            var seen=new System.Collections.Generic.HashSet<PortraitPart>();
            foreach(var part in parts)
            {
                if(part==null || !part.transform.IsChildOf(root.transform) || !seen.Add(part))
                    throw new System.InvalidOperationException("Повреждена сохранённая ссылка портрета: "+root.name+".");
                part.Validate();
            }
            this.root=root;
            int capacity=System.Math.Max(parts.Length,Content.PonyRenderSelection.Capacity);
            if(capacity==parts.Length) {this.parts=parts;return;}
            // Frozen catalogue maximum is provisioned once at construction,
            // including mod slots. Refresh only assigns already prepared quads.
            this.parts=new PortraitPart[capacity];parts.CopyTo(this.parts,0);
            for(int i=parts.Length;i<capacity;i++)
            {
                var child=Object.Instantiate(parts[0],root.transform,false);
                child.name="Часть портрета "+i;child.gameObject.SetActive(false);this.parts[i]=child;
            }
        }
        public void Refresh(ActorState actor)
        {
            root.SetActive(actor!=null);
            if(actor==null) return;
            var value=Content.PonyAppearance.Snapshot(actor);
            var next=Content.PonyRenderSelection.PrepareEquipment(actor);
            if(appearance.HasValue && appearance.Value.Equals(value) && ReferenceEquals(plan,next)) return;
            if(next.PortraitFrame.Length>parts.Length) throw new System.InvalidOperationException(
                $"Портрет {root.name}: actor={actor.id}, нужно частей={next.PortraitFrame.Length}, сохранено={parts.Length}. Подготовьте структуру портретов.");
            appearance=value;plan=next;
            var frame=plan.PortraitFrame;var bounds=plan.FrameBounds("idle",0);
            for(int i=0;i<parts.Length;i++)
            {
                if(i<frame.Length) parts[i].ConfigurePrepared(frame[i],value,bounds);
                else parts[i].gameObject.SetActive(false);
            }
        }
    }
}
