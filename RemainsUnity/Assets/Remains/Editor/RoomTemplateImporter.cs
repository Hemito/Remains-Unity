using System.IO;
using System.Text;
using Remains.Content;
using Remains.RoomFormat;
using UnityEditor.AssetImporters;

namespace Remains.Editor
{
    // *.room.json -> RoomTemplateAsset. Invalid files fail the import with the validator's
    // messages (file, layer, cell); nothing is substituted.
    [ScriptedImporter(1,"room.json")]
    public sealed class RoomTemplateImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text=File.ReadAllText(ctx.assetPath,Encoding.UTF8);
            RoomFile room;
            try {room=RoomSerializer.Read(text,ctx.assetPath);}
            catch(System.FormatException error) {ctx.LogImportError(error.Message);return;}
            foreach(string error in RoomValidator.Validate(room,ctx.assetPath)) ctx.LogImportError(error);
            var asset=UnityEngine.ScriptableObject.CreateInstance<RoomTemplateAsset>();
            asset.name=Path.GetFileName(ctx.assetPath).Replace(".room.json","");
            asset.Configure(room.Template,text);
            ctx.AddObjectToAsset("room",asset);
            ctx.SetMainObject(asset);
        }
    }
}
