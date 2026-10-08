using System;
using System.Collections.Generic;
using Remains.Content;

namespace Remains.Presentation
{
    // Tint slots describe RGB, not activity. Mods explicitly add/disable a role
    // by source identity; arbitrary magic-colored clothing stays unaffected.
    public static class PonyMagicRenderRoles
    {
        private static readonly Dictionary<(string shape,string path),bool> roles=new()
        {{("238","head/morda/magic/2"),true},
         {("238","head/morda/wear:273//magic/2"),true}};
        public static int Version {get;private set;}
        public static bool Contains(PonyRenderPart part)=>part!=null && roles.TryGetValue((part.sourceShape,part.sourcePath),out bool enabled) && enabled;
        public static void Register(string sourceShape,string sourcePath,bool enabled=true,bool replace=false)
        {
            if(string.IsNullOrWhiteSpace(sourceShape) || string.IsNullOrWhiteSpace(sourcePath) ||
                !replace && roles.ContainsKey((sourceShape,sourcePath)))
                throw new InvalidOperationException("Некорректная регистрация роли свечения рога: shape="+sourceShape+", path="+sourcePath);
            roles[(sourceShape,sourcePath)]=enabled;Version++;
        }
    }
}
