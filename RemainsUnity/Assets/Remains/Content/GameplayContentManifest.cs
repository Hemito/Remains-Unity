using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Remains.Serialization;

namespace Remains.Content
{
    public static class GameplayContentManifest
    {
        public static ContentManifest Compose(ContentManifest weapons,IEnumerable<KeyValuePair<string,string>> assets)
        {
            var result=weapons.Copy();result.scope="prototype-gameplay";
            result.gameplayAssets.Clear();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var asset in assets.OrderBy(a=>a.Key,StringComparer.Ordinal))
            {
                if(string.IsNullOrWhiteSpace(asset.Key) || asset.Value==null || !ids.Add(asset.Key))
                    throw new InvalidDataException($"Некорректный или повторный ресурс игрового манифеста: {asset.Key}.");
                result.gameplayAssets.Add(new ContentAssetRecord {id=asset.Key,fingerprint=Hash(asset.Value)});
            }
            if(ids.Count==0) throw new InvalidDataException("Игровой манифест не содержит ресурсов.");
            result.fingerprint=Hash(weapons.fingerprint+"\n"+StateJson.ToJson(result.gameplayAssets));
            return result;
        }
        private static string Hash(string value)
        {using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
    }
}
