using System;
using System.Collections.Generic;
using Remains.Content.VectorArt;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // RvgRasterizer on synthetic RVG with known answers (GPU): even-odd hole, orientation, gradient, color transform,
    // mask, stroke width, nested sprite offset, bitmap fill and an antialiased edge.
    public static class RvgRasterizerChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private const int T=20;   // twips per pixel at zoom 1
        public static void Run()
        {
            var checker=new Texture2D(2,2,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point};
            // Rows bottom-up in the texture: bitmap pixel (0,0) top-left = red, (1,0) top-right = green.
            checker.SetPixels32(new[]{new Color32(0,0,255,255),new Color32(255,255,0,255),new Color32(255,0,0,255),new Color32(0,255,0,255)});checker.Apply();
            var document=Document();
            using var rasterizer=new RvgRasterizer(_=>checker) {Msaa=8};
            Color32 At(Texture2D t,int x,int y)=>t.GetPixel(x,t.height-1-y);   // image coordinates, Y down
            void Near(int actual,int expected,int tolerance,string what) {if(Math.Abs(actual-expected)>tolerance) throw new InvalidOperationException($"RVG растеризатор: {what}: {actual}, ожидается {expected}±{tolerance}.");}

            // 1. Square 0..40 with a hole 10..30 (one fill, two contours, even-odd), placed at the frame's top-left.
            var hole=rasterizer.Render(document,Resource(document,"hole"),Frame(document,"hole"),1);
            Near(At(hole,5,5).a,255,0,"кольцо (альфа)");Near(At(hole,20,20).a,0,0,"дырка even-odd (альфа)");Near(At(hole,50,50).a,0,0,"снаружи (альфа)");
            Near(At(hole,5,5).r,255,0,"цвет заливки (R)");
            // Orientation: the shape occupies the top-left corner (Y down in the image).
            Near(At(hole,5,hole.height-5).a,0,0,"ориентация: низ холста пуст");
            // 2. Linear gradient black → white over 0..100 px.
            var gradient=rasterizer.Render(document,Resource(document,"gradient"),Frame(document,"gradient"),1);
            Near(At(gradient,25,5).r,64,8,"линейный градиент в 1/4");Near(At(gradient,75,5).r,191,8,"линейный градиент в 3/4");
            // 3. Color transform: red ×0.5.
            var tinted=rasterizer.Render(document,Resource(document,"tinted"),Frame(document,"tinted"),1);
            Near(At(tinted,10,10).r,128,2,"цветовое преобразование ×0,5");
            // 4. Mask: a 40×40 square masked by 0..20 × 0..40.
            var masked=rasterizer.Render(document,Resource(document,"masked"),Frame(document,"masked"),1);
            Near(At(masked,10,10).a,255,0,"внутри маски");Near(At(masked,30,10).a,0,0,"вне маски");
            // 5. Stroke: a horizontal line at y=20, width 4 px (80 twips).
            var stroke=rasterizer.Render(document,Resource(document,"stroke"),Frame(document,"stroke"),1);
            Near(At(stroke,20,20).a,255,0,"середина линии");Near(At(stroke,20,23).a,0,40,"край линии +3 пикселя");Near(At(stroke,20,15).a,0,0,"выше линии");
            // 6. Nested sprite moved by (50, 10) px.
            var nested=rasterizer.Render(document,Resource(document,"nested"),Frame(document,"nested"),1);
            Near(At(nested,55,15).a,255,0,"вложенный спрайт со сдвигом");Near(At(nested,5,5).a,0,0,"вложенный спрайт не на месте");
            // 7. Bitmap fill: the 2×2 checker scaled to 40×40, not smoothed: top-left quadrant red, top-right green.
            var bitmap=rasterizer.Render(document,Resource(document,"bitmap"),Frame(document,"bitmap"),1);
            Near(At(bitmap,10,10).r,255,0,"растр: левый верх красный");Near(At(bitmap,30,10).g,255,0,"растр: правый верх зелёный");
            Near(At(bitmap,10,30).b,255,0,"растр: левый низ синий");
            // 8. Antialiasing: a square edge at x=20.5 gives partial coverage.
            var edge=rasterizer.Render(document,Resource(document,"edge"),Frame(document,"edge"),1);
            int a=At(edge,20,10).a;
            if(a<64 || a>192) throw new InvalidOperationException($"RVG растеризатор: сглаживание края: альфа {a}, ожидается частичное покрытие.");
            // 9. Zoom: the hole frame at ×4 keeps proportions.
            var zoomed=rasterizer.Render(document,Resource(document,"hole"),Frame(document,"hole"),4);
            if(zoomed.width!=Mathf.CeilToInt(Frame(document,"hole").size[0]*4)) throw new InvalidOperationException("RVG растеризатор: размер холста при увеличении.");
            Near(At(zoomed,80,80).a,0,0,"дырка при ×4");Near(At(zoomed,20,20).a,255,0,"кольцо при ×4");
            if(rasterizer.Approximations.Count>0) throw new InvalidOperationException("RVG растеризатор: неожиданные приближения в простых случаях: "+string.Join(", ",rasterizer.Approximations.Keys));
            Debug.Log("RVG растеризатор: PASS — even-odd, ориентация, градиент, цветовое преобразование, маска, линия, вложенный спрайт, растровая заливка, сглаживание, увеличение.");
        }

        private static RvgResource Resource(RvgDocument d,string id)=>d.TryResource(id,out var r)?r:throw new InvalidOperationException(id);
        private static RvgResourceFrame Frame(RvgDocument d,string id)=>Resource(d,id).frames[0];
        private static int[] Rect(int x0,int y0,int x1,int y1)=>new[]{0,x0*T,y0*T,1,x1*T,y0*T,1,x1*T,y1*T,1,x0*T,y1*T,3};
        private static int[] Concat(params int[][] parts) {var list=new List<int>();foreach(var p in parts) list.AddRange(p);return list.ToArray();}
        private static RvgDocument Document()
        {
            var shapes=new List<object>
            {
                Shape(1,Concat(Rect(0,0,40,40),Rect(10,10,30,30)),Solid(255,0,0,255)),
                Shape(2,Rect(0,0,100,10),new {type="linear",matrix=new[]{100*T/32768f,0,0,1f,50*T,0},spread="pad",interpolation="rgb",stops=new[]{new float[]{0,0,0,0,255},new float[]{255,255,255,255,255}}}),
                Shape(3,Rect(0,0,20,20),Solid(255,0,0,255)),
                Shape(4,Rect(0,0,40,40),Solid(0,255,0,255)),
                Shape(5,Rect(0,0,20,40),Solid(0,0,0,255)),
                Shape(7,Rect(0,0,10,10),Solid(255,255,255,255)),
                Shape(8,Rect(0,0,40,40),new {type="bitmap",bitmap=1,matrix=new[]{20f*T,0,0,20f*T,0,0},repeat=false,smooth=false}),
                Shape(9,new[]{0,0,0,1,(int)(20.5f*T),0,1,(int)(20.5f*T),20*T,1,0,20*T,3},Solid(255,255,255,255)),
                new {id=6,bounds=new[]{0,0,40*T,40*T},fills=new object[0],lines=new[]{new {width=4f*T,color=new[]{255,255,255,255},startCap="none",endCap="none",join="miter"}},
                    paths=new[]{new {line=0,commands=new[]{0,0,20*T,1,40*T,20*T}}}},
            };
            object Place(int depth,string kind,int character,float tx=0,float ty=0,object color=null,int clipDepth=0,int spriteFrame=0)
                =>new {depth,character,kind,matrix=new[]{1f,0,0,1f,tx*T,ty*T},color,clipDepth,spriteFrame};
            var sprites=new List<object>
            {
                Sprite(101,Place(1,"shape",1)),Sprite(102,Place(1,"shape",2)),
                Sprite(103,Place(1,"shape",3,color:new {mul=new[]{.5f,1,1,1},add=new[]{0f,0,0,0}})),
                Sprite(104,Place(1,"shape",5,clipDepth:2),Place(2,"shape",4)),
                Sprite(105,Place(1,"shape",6)),
                Sprite(106,Place(1,"shape",7)),Sprite(107,Place(1,"sprite",106,50,10)),
                Sprite(108,Place(1,"shape",8)),Sprite(109,Place(1,"shape",9)),
            };
            object Res(string id,int sprite,float w,float h)=>new {id,kind="vector",sprite,frames=new[]{new {frame=1,origin=new[]{0f,0f},size=new[]{w,h}}}};
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                format="remains-rvg",version=1,source=new {archive="checks"},
                bitmaps=new[]{new {id=1,width=2,height=2,file="checker.png",sha256=""}},
                shapes,sprites,
                resources=new[]{Res("hole",101,60,60),Res("gradient",102,100,10),Res("tinted",103,20,20),Res("masked",104,40,40),Res("stroke",105,40,40),
                    Res("nested",107,70,30),Res("bitmap",108,40,40),Res("edge",109,40,20)},
                unsupported=new object[0]
            });
            return RvgDocument.Parse(json,"проверка растеризатора");
        }
        private static object Solid(int r,int g,int b,int a)=>new {type="solid",color=new[]{r,g,b,a}};
        private static object Shape(int id,int[] commands,object fill)=>new {id,bounds=new[]{0,0,100*T,100*T},fills=new[]{fill},lines=new object[0],paths=new[]{new {fill=0,commands}}};
        private static object Sprite(int id,params object[] objects)=>new {id,frameCount=1,frames=new[]{objects}};
    }
}
