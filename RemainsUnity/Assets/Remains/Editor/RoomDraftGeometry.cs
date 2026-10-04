using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // Authoring operation only; each surface and ladder remains editable.
    internal static class RoomDraftGeometry
    {
        public static RoomAuthoring Create(GameObject root,RoomLayout layout)
        {
            var authoring=root.AddComponent<RoomAuthoring>();authoring.Configure(layout);
            int index=0;
            foreach(var surface in layout.platforms)
            {
                var part=new GameObject("Поверхность "+index++);part.transform.SetParent(root.transform,false);
                part.AddComponent<RoomSurfaceAuthoring>().Configure(surface);
            }
            index=0;
            foreach(var ladder in layout.ladders)
            {
                var part=new GameObject("Лестница "+index++);part.transform.SetParent(root.transform,false);
                part.AddComponent<RoomLadderAuthoring>().Configure(ladder);
            }
            return authoring;
        }
    }
}
