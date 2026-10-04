using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;
namespace Remains.Content
{
    [Serializable] public sealed class TrainingPlacement {public string id,type;public float x,y;public int facing;public bool relocateExisting;}
    [Serializable] public sealed class TrainingLayout {public string patch,level;public TrainingPlacement[] actors,containers;}
    public static class HubTrainingContent
    {
        public const string Patch="base:hub-training-4";
        public static bool NeedsInstall(WorldState world)=>!world.contentPatches.Contains(Patch);
        public static TrainingLayout ReadLayout()=>RoomTrainingContent.Read(RoomAuthoring.Load("hub"),Patch,"hub");
        public static void Install(WorldState world)
        {
            if(!NeedsInstall(world)) return;
            var layout=ReadLayout();
            TrainingContentInstaller.Apply(world,layout);
        }
    }
}
