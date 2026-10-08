using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit authoring command; the saved sprite remains the runtime source.
    public static class TrainingArtworkReimport
    {
        public static void Run()
        {
            // Retain the historical explicit build entry point, but never
            // downgrade the accepted density or regenerate different geometry.
            ItemHubVectorReserveAuthoring.Install();PrototypeBuild.Build();
        }
    }
}
