using System;
using System.IO;
using UnityEngine;

namespace Remains.Editor
{
    // The preserved original content export (Tools/ContentExport) that the Unity importers read. One place for its path
    // and for the packages it may replace: v8 = v7 + unit appearances by the class constructors' Res.getVis("vis"+id)
    // (traps, charges, triggers); v9 = v8 + named moving parts of vclass units (turret gun and lamp, "part:*") with
    // the turret appearances rendered without them. Every file imported before keeps its SHA256.
    public static class ContentPackage
    {
        public const string Name="content-catalog-v9";
        // catalog.json SHA256 of packages whose installed content this package may extend in place.
        public static readonly string[] Replaces={"b186f515d6bac733193988af9ba354ebba8e5999f0dc20072531b51c972814e1",   // content-catalog-v7
            "69a568dc78a640d74e547a236c3ed98b80c654fa9d7e087c436f2de9d3821588",   // v8 before the state clips of vector units
            "6ce5a6d710d21bc211fd2ea327bf7dbddbb6c90e6cf656870945ef2a662282fe"};  // content-catalog-v8
        public static string Directory=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference",Name));
        public static string Catalog=>Path.Combine(Directory,"catalog.json");
        public static bool CanReplace(string installedHash)=>Array.IndexOf(Replaces,installedHash)>=0;
    }
}
