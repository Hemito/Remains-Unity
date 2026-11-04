using Remains.Presentation;
using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Isolated GPU regressions. No image export, motion rerun, Player changes or timings.
    public static class GridFillReconstructionChecks
    {
        public static void RunBatch()
        {
            try {Debug.Log(Run());EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static string Run()
        {
            int contourChecks=0,stripeChecks=0;
            foreach(var scene in PolarShadowCase.Create())
            {
                using var field=new PolarShadowField(scene);
                contourChecks+=field.ValidateFillReconstruction();
                field.RenderExact();var direct=PolarShadowField.Read(field.Light);field.Compose();field.ValidateFillComposition(direct);
            }
            foreach(bool analytic in new[]{false,true})
            {
                stripeChecks+=CheckStripe(1.57f,1.58f,analytic);
                stripeChecks+=CheckStripe(1.2f,1.8f,analytic);
            }
            CheckCorner();
            return $"PASS GridFillReconstruction: сохранение постоянного поля на 4 точных сценах — {contourChecks} открытых пикселей; тонкая/толстая преграда в analytic/fine-DDA — {stripeChecks} пикселей без ореола и переноса цвета сквозь стену; закрытый диагональный угол и композиция заполнения PASS.";
        }
        private static void CheckCorner()
        {
            var fill=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);
            var mask=new Texture2D(2,2,TextureFormat.RFloat,false,true);
            try
            {
                fill.SetPixels(new[]{Color.red,Color.black,Color.black,Color.blue});fill.Apply(false,false);
                mask.SetPixelData(new[]{0f,1f,1f,0f},0);mask.Apply(false,false);
                using var reconstruction=new GridFillReconstruction(2,2);
                var rect=new Rect(-5,7,2,2);reconstruction.Build(fill,mask,rect,mask,rect,rect);
                var pixels=reconstruction.ReadDiagnostics("закрытый диагональный угол");
                if(Mathf.Abs(pixels[0].r-1)>1e-5f || pixels[0].b>1e-5f || Mathf.Abs(pixels[3].b-1)>1e-5f || pixels[3].r>1e-5f)
                    throw new InvalidOperationException($"Заполнение протекает через закрытый диагональный угол: {pixels[0]}, {pixels[3]}.");
            }
            finally {Object.DestroyImmediate(fill);Object.DestroyImmediate(mask);}
        }
        private static int CheckStripe(float left,float right,bool analytic)
        {
            const int cw=16,ch=8,fw=128,fh=64,ow=320,oh=160;
            var rect=new Rect(0,0,3.2f,1.6f);
            var coarse=new Texture2D(cw,ch,TextureFormat.RGBAFloat,false,true);
            var blocked=new Texture2D(cw,ch,TextureFormat.RFloat,false,true);
            var fine=new Texture2D(fw,fh,TextureFormat.RFloat,false,true);
            var contours=new ComputeBuffer(4,16);
            try
            {
                var colors=new Color[cw*ch];var coarseMask=new float[cw*ch];var fineMask=new float[fw*fh];
                for(int y=0;y<ch;y++) for(int x=0;x<cw;x++)
                {
                    bool wall=x*.2f<right && (x+1)*.2f>left;
                    coarseMask[y*cw+x]=wall?1:0;
                    colors[y*cw+x]=wall?Color.black:(x*.2f<left?Color.red:Color.blue);
                }
                for(int y=0;y<fh;y++) for(int x=0;x<fw;x++)
                    fineMask[y*fw+x]=x*.025f<right && (x+1)*.025f>left?1:0;
                coarse.SetPixels(colors);coarse.Apply(false,false);blocked.SetPixelData(coarseMask,0);blocked.Apply(false,false);
                fine.SetPixelData(fineMask,0);fine.Apply(false,false);
                contours.SetData(new[]{new Vector4(left,-1,right,-1),new Vector4(right,-1,right,3),
                    new Vector4(right,3,left,3),new Vector4(left,3,left,-1)});
                using var reconstruction=new GridFillReconstruction(ow,oh);
                reconstruction.Build(coarse,blocked,rect,fine,rect,rect,analytic?contours:null,analytic?4:0);
                var pixels=reconstruction.ReadDiagnostics($"полоса {left}..{right}, analytic={analytic}");int checks=0;
                for(int y=0;y<oh;y++) for(int x=0;x<ow;x++)
                {
                    float wx=(x+.5f)/ow*rect.width;
                    bool wall=analytic?wx>left && wx<right:fineMask[Mathf.FloorToInt((y+.5f)/oh*fh)*fw+Mathf.FloorToInt((x+.5f)/ow*fw)]>=.99f;
                    var pixel=pixels[y*ow+x];
                    if(wall)
                    {
                        if(pixel.maxColorComponent>1e-5f || pixel.a!=-1) throw new InvalidOperationException($"Заполнение внутри стены: analytic={analytic}, ({x},{y}), {pixel}.");
                    }
                    else
                    {
                        var expected=wx<left?Color.red:Color.blue;
                        if(pixel.a<=0 || Mathf.Abs(pixel.r-expected.r)>1e-5f || Mathf.Abs(pixel.g-expected.g)>1e-5f || Mathf.Abs(pixel.b-expected.b)>1e-5f)
                            throw new InvalidOperationException($"Ореол/протечка: analytic={analytic}, полоса={left}..{right}, пиксель=({x},{y}), worldX={wx}, RGB={pixel}, ожидается={expected}, support={pixel.a}.");
                    }
                    checks++;
                }
                return checks;
            }
            finally {contours.Dispose();Object.DestroyImmediate(coarse);Object.DestroyImmediate(blocked);Object.DestroyImmediate(fine);}
        }
    }
}
