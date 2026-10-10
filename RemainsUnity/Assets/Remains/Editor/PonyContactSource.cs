using System;
using System.IO;
using UnityEngine;

namespace Remains.Editor
{
    // Physical contact of the pony (foot support, ladder and slope contacts) is measured on the foot rasters
    // it was authored from (foot-support.bytes stores their sizes and hashes). The display textures in
    // Resources/Original/Pony may be re-exported at another density or with widened lines (2026-10-10), so
    // the contact rasters are frozen separately, outside Resources: they never ship and never change with
    // the art. Changing physical contact is a separate, explicit migration with body/ladder/slope checks.
    public static class PonyContactSource
    {
        public const string Folder="Tools/Research/PonyContactSource";

        public static string Png(string texture)
        {
            string file=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",Folder,texture+".png"));
            if(!File.Exists(file)) throw new InvalidOperationException($"Нет замороженного растра опоры пони: texture={texture}, ожидался {file}. Опора меряется только по объявленным копытам.");
            return file;
        }
    }
}
