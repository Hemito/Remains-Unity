using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Actual presentation ownership, never a gameplay category inferred from an ID.
    public readonly struct WorldOutlineSource
    {
        public readonly SortingGroup Group;
        public readonly Renderer Artwork;
        private readonly bool requiresArtwork;
        private readonly bool outlineArtwork;
        private WorldOutlineSource(SortingGroup group,Renderer artwork,bool requiresArtwork,bool outlineArtwork) {Group=group;Artwork=artwork;this.requiresArtwork=requiresArtwork;this.outlineArtwork=outlineArtwork;}
        public static WorldOutlineSource ForRenderer(Renderer artwork,bool outlineArtwork=false)=>artwork==null?default:new(RenderSorting.FindOwner(artwork),artwork,true,outlineArtwork);
        public static WorldOutlineSource ForGroup(SortingGroup group)=>new(group,null,false,false);
        // A visible frame may differ from the deliberately authored access volume.
        public Rect Bounds(Rect interactionBounds)
        {
            if(!outlineArtwork || Artwork==null) return interactionBounds;
            var bounds=Artwork.bounds;return new Rect(bounds.min.x,bounds.min.y,bounds.size.x,bounds.size.y);
        }
        public bool Visible=>Group!=null && Group.enabled && Group.gameObject.activeInHierarchy &&
            (!requiresArtwork || Artwork!=null && Artwork.enabled && !Artwork.forceRenderingOff && Artwork.gameObject.activeInHierarchy);
        public float Depth=>requiresArtwork?Artwork.bounds.center.z:Group.transform.position.z;
        public int GameLayer=>requiresArtwork?Artwork.gameObject.layer:Group.gameObject.layer;
    }
}
