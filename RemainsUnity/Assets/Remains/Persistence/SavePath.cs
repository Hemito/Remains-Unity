using System;
using System.IO;

namespace Remains.Persistence
{
    public static class SavePath
    {
        public static string Absolute(string path)
        {
            if(Environment.OSVersion.Platform==PlatformID.Win32NT && path.StartsWith(@"\\?\",StringComparison.Ordinal)) return path;
            string full=Path.GetFullPath(path);
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT) return full;
            return full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full.Substring(2):@"\\?\"+full;
        }
    }
}
