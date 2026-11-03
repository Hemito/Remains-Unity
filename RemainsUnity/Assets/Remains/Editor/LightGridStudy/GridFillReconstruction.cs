using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Cached Editor-only reconstruction. Rebuild only on changed fill, geometry, or output rectangle.
    // A moving direct-only light does not invalidate this texture. No readback in Build().
    public sealed class GridFillReconstruction : IDisposable
    {
        private readonly ComputeShader compute;
        private readonly int kernel;
        private readonly ComputeBuffer emptyContours;
        public RenderTexture Texture {get;private set;}
        public int UnresolvedCount {get;private set;}=-1;
        public GridFillReconstruction(int width,int height)
        {
            if(width<=0 || height<=0) throw new ArgumentOutOfRangeException(nameof(width));
            try
            {
                var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Remains/Editor/LightGridStudy/GridFillReconstruction.compute");
                if(asset==null) throw new InvalidOperationException("Не найден GridFillReconstruction.compute.");
                compute=Object.Instantiate(asset);kernel=compute.FindKernel("Reconstruct");
                emptyContours=new ComputeBuffer(1,16);emptyContours.SetData(new[]{Vector4.zero});
                Texture=new RenderTexture(width,height,0,GraphicsFormat.R32G32B32A32_SFloat)
                    {name="Заполнение: нормализованная реконструкция по открытым клеткам",enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                if(!Texture.Create()) throw new InvalidOperationException("Не создана текстура реконструкции заполнения.");
            }
            catch {Dispose();throw;}
        }
        // coarseBlocked and fineAbsorption: red channel >=.99 is opaque. Rectangles map all texels.
        // With analyticContours, exact closed disjoint contours replace the fine raster visibility test.
        public void Build(Texture coarseFill,Texture coarseBlocked,Rect coarseWorldRect,Texture fineAbsorption,
            Rect fineWorldRect,Rect outputWorldRect,ComputeBuffer analyticContours=null,int contourCount=0)
            =>BuildRegion(coarseFill,coarseBlocked,coarseWorldRect,fineAbsorption,fineWorldRect,outputWorldRect,
                new RectInt(0,0,Texture.width,Texture.height),analyticContours,contourCount);

        // The caller expands dirty coarse cells by the reconstruction support (2 cells plus rounding).
        // Unchanged output texels keep their previous values. Initial preparation must use Build().
        public void BuildRegion(Texture coarseFill,Texture coarseBlocked,Rect coarseWorldRect,Texture fineAbsorption,
            Rect fineWorldRect,Rect outputWorldRect,RectInt outputPixels,ComputeBuffer analyticContours=null,int contourCount=0)
        {
            if(coarseFill==null || coarseBlocked==null || coarseFill.width!=coarseBlocked.width || coarseFill.height!=coarseBlocked.height)
                throw new ArgumentException("Реконструкция: заполнение и маска закрытых клеток должны иметь одинаковый размер.");
            bool analytic=analyticContours!=null;
            if(analytic?contourCount<=0 || contourCount>analyticContours.count:fineAbsorption==null)
                throw new ArgumentException("Реконструкция: нужны точные контуры либо карта поглощения.");
            ValidateRect(coarseWorldRect);ValidateRect(outputWorldRect);if(!analytic) ValidateRect(fineWorldRect);
            if(outputPixels.xMin<0 || outputPixels.yMin<0 || outputPixels.xMax>Texture.width || outputPixels.yMax>Texture.height)
                throw new ArgumentException("Реконструкция: область обновления вне выходной текстуры: "+outputPixels);
            if(outputPixels.width<=0 || outputPixels.height<=0)return;
            var fine=analytic?coarseBlocked:fineAbsorption;
            compute.SetTexture(kernel,"_CoarseFill",coarseFill);compute.SetTexture(kernel,"_CoarseBlocked",coarseBlocked);
            compute.SetTexture(kernel,"_FineAbsorption",fine);compute.SetTexture(kernel,"_Output",Texture);
            compute.SetBuffer(kernel,"_Contours",analyticContours??emptyContours);
            compute.SetInt("_Analytic",analytic?1:0);compute.SetInt("_ContourCount",analytic?contourCount:0);
            compute.SetInts("_CoarseSize",coarseFill.width,coarseFill.height);compute.SetInts("_FineSize",fine.width,fine.height);
            compute.SetInts("_OutputSize",Texture.width,Texture.height);
            compute.SetVector("_CoarseRect",Vector(coarseWorldRect));compute.SetVector("_FineRect",Vector(analytic?coarseWorldRect:fineWorldRect));
            compute.SetVector("_OutputRect",Vector(outputWorldRect));
            compute.SetInts("_Clip",outputPixels.xMin,outputPixels.yMin,outputPixels.xMax,outputPixels.yMax);
            compute.Dispatch(kernel,(outputPixels.width+7)/8,(outputPixels.height+7)/8,1);UnresolvedCount=-1;
        }
        // Explicit diagnostic readback, kept outside all measured/frame work.
        public Color[] ReadDiagnostics(string context,bool warn=true)
        {
            var pixels=PolarShadowField.Read(Texture);UnresolvedCount=0;
            foreach(var pixel in pixels) if(pixel.a==0) UnresolvedCount++;
            if(warn && UnresolvedCount>0) Debug.LogWarning($"Реконструкция заполнения {context}: открытых пикселей без видимой открытой coarse-клетки {UnresolvedCount}/{pixels.Length}; support=2 клетки, текстура {Texture.width}×{Texture.height}. Вывод пропущен (чёрный), подстановка света не применялась.",Texture);
            return pixels;
        }
        private static Vector4 Vector(Rect r)=>new(r.x,r.y,r.width,r.height);
        private static void ValidateRect(Rect r)
        {
            if(!float.IsFinite(r.x) || !float.IsFinite(r.y) || !float.IsFinite(r.width) || !float.IsFinite(r.height) || r.width<=0 || r.height<=0)
                throw new ArgumentException("Реконструкция: неверная мировая область "+r);
        }
        public void Dispose()
        {
            emptyContours?.Dispose();
            if(Texture!=null) {Texture.Release();Object.DestroyImmediate(Texture);Texture=null;}
            if(compute!=null) Object.DestroyImmediate(compute);
        }
    }
}
