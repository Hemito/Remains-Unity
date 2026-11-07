using System;
using System.IO;
using Remains.Content;
using UnityEngine;

namespace Remains.Runtime
{
    // Runs inside the built Player because Null Editor and Null Player expose different material metadata.
    internal static class PonyRenderValidationProbe
    {
        public static void Run(string report)
        {
            if(!Application.isBatchMode || string.IsNullOrWhiteSpace(report))
                throw new ArgumentException("Проверке материалов пони нужны batchmode и --report.");
            var result=new Result {graphicsApi=SystemInfo.graphicsDeviceType.ToString()};
            try
            {
                var asset=GameAssets.Load<PonyRenderAsset>("Original/Pony/render");
                asset.Validate();WearableRenderCatalog.Prepare();
                var part=asset.Clips[0].frames[0].parts[0];
                var points=part.points;var original=part.material;
                var shader=GameAssets.Load<Shader>("Presentation/WorldTerrain")??throw new InvalidOperationException("Нет шейдера для отрицательной проверки.");
                var wrongMaterial=new Material(shader);
                try
                {
                    part.points=new Vector3[3];
                    result.rejectedGeometry=Rejects(part,"points-count");
                    part.points=points;part.material=wrongMaterial;
                    result.rejectedShader=Rejects(part,"shader-emission");
                    result.success=true;
                }
                finally
                {
                    part.points=points;part.material=original;
                    UnityEngine.Object.Destroy(wrongMaterial);
                }
            }
            catch(Exception error) {result.error=error.ToString();Debug.LogException(error);}
            File.WriteAllText(report,JsonUtility.ToJson(result,true));
            Application.Quit(result.success?0:1);
        }
        private static bool Rejects(PonyRenderPart part,string reason)
        {
            try {PonyRenderAsset.ValidatePart(part,"negative-player-probe");}
            catch(InvalidOperationException error) when(error.Message.Contains("причина="+reason+".",StringComparison.Ordinal)) {return true;}
            throw new InvalidOperationException("Проверка приняла повреждённую часть пони: "+reason+".");
        }
        [Serializable] private sealed class Result
        {public bool success,rejectedGeometry,rejectedShader;public string graphicsApi,error;}
    }
}
