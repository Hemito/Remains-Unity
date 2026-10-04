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
        public const string Patch="base:hub-training-3";
        public static bool NeedsInstall(WorldState world)=>!world.contentPatches.Contains(Patch);
        public static void Install(WorldState world)
        {
            if(!NeedsInstall(world)) return;
            var asset=Resources.Load<TextAsset>("Content/hub-training");
            if(asset==null) throw new InvalidOperationException("Нет расстановки Content/hub-training.");
            var layout=JsonUtility.FromJson<TrainingLayout>(asset.text);
            if(layout==null || layout.patch!=Patch || layout.level!="hub" || layout.actors==null || layout.containers==null)
                throw new InvalidOperationException("Некорректная расстановка мишеней хаба.");
            TrainingContentInstaller.Apply(world,layout);
        }
    }
}
