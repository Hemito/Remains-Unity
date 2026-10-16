using System;
using System.IO;
using UnityEngine;

namespace Remains.Editor
{
    // The preserved original content export (Tools/ContentExport) that the Unity importers read. One place for its path
    // and for the packages it may replace: v8 = v7 + unit appearances by the class constructors' Res.getVis("vis"+id)
    // (traps, charges, triggers); every file imported from v7 keeps its SHA256.
    public static class ContentPackage
    {
        public const string Name="content-catalog-v8";
        // catalog.json SHA256 of packages whose installed content this package may extend in place.
        public static readonly string[] Replaces={"b186f515d6bac733193988af9ba354ebba8e5999f0dc20072531b51c972814e1"};   // content-catalog-v7
        public static string Directory=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference",Name));
        public static string Catalog=>Path.Combine(Directory,"catalog.json");
        public static bool CanReplace(string installedHash)=>Array.IndexOf(Replaces,installedHash)>=0;
    }
}
