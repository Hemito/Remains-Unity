using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class OriginalAudioImport : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if(!assetPath.StartsWith("Assets/Remains/Resources/Original/Audio/Music/",System.StringComparison.Ordinal)) return;
            var importer=(AudioImporter)assetImporter;var settings=importer.defaultSampleSettings;
            settings.loadType=AudioClipLoadType.Streaming;
            importer.defaultSampleSettings=settings;
        }
    }
}
