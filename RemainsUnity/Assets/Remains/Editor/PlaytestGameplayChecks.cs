using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Focused subset for the 2026-10-18 playtest. Does not replace full ContractChecks or a Player build.
    public static class PlaytestGameplayChecks
    {
        public static void RunBatch()
        {
            try
            {
                Remains.Domain.Anatomy.Load();Remains.Content.WeaponCatalog.Load();
                FactoryEnemyScalingChecks.Run();FirearmChecks.Run();DroppedItemShapeChecks.Run();
                ProjectileTrailJoinChecks.Run();LadderHoldChecks.Run();MovementChecks.CheckSmallGaps();
                FactoryToolStandChecks.Run();HeldPropColorChecks.Run();
                CreatureChecks.Run();FactoryCrawlerChecks.Run();ContainerLootChecks.Run();
                RoomLoadingViewChecks.Run();StaticLightShadowChecks.Run();
                Debug.Log("Целевые проверки playtest 2026-10-18: PASS; полный ContractChecks и Windows build проверяются отдельно.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
