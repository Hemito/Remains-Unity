using System;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonySymbolAcceptance
    {
        public static void BuildCore()
        {
            try
            {
                PonySymbolAuthoring.Build();
                PonyRenderChecks.Run();PonyRigRuntimeChecks.Run();PonyAppearanceChecks.Run();WearablePresentationChecks.Run();
                var banks=new[]{"Assets/Remains/Resources/Original/Pony/render.asset","Assets/Remains/Resources/Original/Pony/Wearables/pip-render.asset","Assets/Remains/Resources/Original/Pony/Wearables/antirad-render.asset"};
                foreach(string path in banks)Debug.Log($"Символы: {path}, meshes={AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count()}, bytes={new FileInfo(path).Length}.");
                CompareCaptures();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void CompareCaptures()
        {
            string before=Path.GetFullPath("../.reference/rig-symbols/before"),after=Path.GetFullPath("../.reference/rig-pilot/game");
            long changed=0,total=0;int max=0,count=0;
            foreach(string path in Directory.GetFiles(before,"*.png"))
            {
                var a=new Texture2D(2,2);var b=new Texture2D(2,2);
                try
                {
                    if(!a.LoadImage(File.ReadAllBytes(path)) || !b.LoadImage(File.ReadAllBytes(Path.Combine(after,Path.GetFileName(path)))) || a.width!=b.width || a.height!=b.height)throw new InvalidOperationException("Нет сопоставимых кадров "+path);
                    var left=a.GetPixels32();var right=b.GetPixels32();
                    for(int p=0;p<left.Length;p++)
                    {
                        int difference=Math.Max(Math.Max(Math.Abs(left[p].r-right[p].r),Math.Abs(left[p].g-right[p].g)),Math.Max(Math.Abs(left[p].b-right[p].b),Math.Abs(left[p].a-right[p].a)));
                        if(difference>8)changed++;max=Math.Max(max,difference);total++;
                    }
                    count++;
                }
                finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
            }
            if(count==0 || changed/(double)total>.002)throw new InvalidOperationException($"Символы изменили рисунок: кадров={count}, pixels>8={changed}/{total}, max={max}.");
            Debug.Log($"Символы, изображения до/после: PASS — кадров={count}, pixels>8={changed}/{total}, max channel difference={max}.");
        }
    }
}
