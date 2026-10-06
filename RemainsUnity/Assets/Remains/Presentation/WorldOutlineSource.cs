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
        private WorldOutlineSource(SortingGroup group,Renderer artwork,bool requiresArtwork) {Group=group;Artwork=artwork;this.requiresArtwork=requiresArtwork;}
        public static WorldOutlineSource ForRenderer(Renderer artwork)=>artwork==null?default:new(RenderSorting.FindOwner(artwork),artwork,true);
        public static WorldOutlineSource ForGroup(SortingGroup group)=>new(group,null,false);
        public bool Visible=>Group!=null && Group.enabled && Group.gameObject.activeInHierarchy &&
            (!requiresArtwork || Artwork!=null && Artwork.enabled && !Artwork.forceRenderingOff && Artwork.gameObject.activeInHierarchy);
        public float Depth=>requiresArtwork?Artwork.bounds.center.z:Group.transform.position.z;
        public int GameLayer=>requiresArtwork?Artwork.gameObject.layer:Group.gameObject.layer;
    }
}
