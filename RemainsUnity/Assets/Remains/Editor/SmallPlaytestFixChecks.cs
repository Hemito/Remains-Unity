using System;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class SmallPlaytestFixChecks
    {
        public static void RunBatch()
        {
            try
            {
                Remains.Content.ActorTypes.Load();
                FootSupportContentChecks.Run();ActorBodySupportChecks.Run();
                PlayerStrikeAnimationChecks.Run();SpikeArtworkChecks.Run();
                WorldVisualOrderChecks.Run();InteractionSelectionChecks.Run();
                Debug.Log("Малые правки playtest: PASS — удары, шипы, сортировка и взаимодействие.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
