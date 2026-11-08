using System;
using System.Linq;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomEmptyLayerGeometryChecks
    {
        public static void Run()
        {
            var room=new RoomFile{Template="explicit-empty-layers",Width=8,Height=8};
            foreach(string layer in RoomLayers.All)room.Layers.Add(layer,Enumerable.Repeat(new string('.',8),8).ToArray());
            if(RoomValidator.Validate(room,"empty").Count!=0)throw new Exception("Empty layers must be format-valid without unused palettes.");
            var geometry=RoomGeometryBuilder.Build(room,new RoomDefinitionCatalog());
            if(geometry.Surfaces.Count!=0||geometry.Ladders.Count!=0||geometry.Issues.Count!=0)throw new Exception("Empty layers created geometry or warnings.");
            foreach(string layer in new[]{RoomLayers.Solid,RoomLayers.Platform,RoomLayers.Stairs,RoomLayers.Ladder})
            {
                room.Layers[layer][0]="#.......";
                try{RoomGeometryBuilder.Build(room,new RoomDefinitionCatalog());throw new Exception("Nonempty layer without palette accepted: "+layer);}
                catch(FormatException){}
                room.Layers[layer][0]="........";
            }
            Debug.Log("Explicit empty room layers: PASS — no unused palette required; nonempty undefined keys rejected.");
        }
    }
}
