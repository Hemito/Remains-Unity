using System;
using System.IO;
using System.Text;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // A portable, already-loaded image source per character. Playback never changes an <img> URL.
    public static class NativePonyPreviewPage
    {
        private const int Frames=31,Columns=8,Rows=4;
        public static void Write(string outputFolder)
        {
            if(string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
                throw new InvalidOperationException("Предпросмотр рига: не найдена папка кадров "+outputFolder);
            string left=Atlas(outputFolder,"Bare",out int width,out int height);
            string right=Atlas(outputFolder,"AntiradCreator",out int rightWidth,out int rightHeight);
            if(width!=rightWidth || height!=rightHeight)
                throw new InvalidOperationException("Предпросмотр рига: размеры кадров двух персонажей не совпадают.");
            string leftCurl=ImageData(Path.Combine(outputFolder,"Bare-curl.png"),width,height);
            string rightCurl=ImageData(Path.Combine(outputFolder,"AntiradCreator-curl.png"),width,height);
            var html=new StringBuilder(Template);
            html.Replace("@WIDTH@",width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            html.Replace("@HEIGHT@",height.ToString(System.Globalization.CultureInfo.InvariantCulture));
            html.Replace("@LEFT_ATLAS@",left).Replace("@RIGHT_ATLAS@",right);
            html.Replace("@LEFT_CURL@",leftCurl).Replace("@RIGHT_CURL@",rightCurl);
            File.WriteAllText(Path.Combine(outputFolder,"index.html"),html.ToString(),new UTF8Encoding(false));
        }
        private static string Atlas(string folder,string variant,out int width,out int height)
        {
            Texture2D atlas=null,frame=null;width=height=0;
            try
            {
                for(int f=0;f<Frames;f++)
                {
                    string path=Path.Combine(folder,variant+"-walk-"+f.ToString("D2")+".png");
                    frame=LoadPng(path);
                    if(atlas==null)
                    {
                        width=frame.width;height=frame.height;
                        if(width*Columns>SystemInfo.maxTextureSize || height*Rows>SystemInfo.maxTextureSize)
                            throw new InvalidOperationException($"Предпросмотр рига: атлас {variant} {width*Columns}×{height*Rows} превышает предел текстуры {SystemInfo.maxTextureSize}.");
                        atlas=new Texture2D(width*Columns,height*Rows,TextureFormat.RGBA32,false);
                        atlas.SetPixels32(new Color32[atlas.width*atlas.height]);
                    }
                    if(frame.width!=width || frame.height!=height)
                        throw new InvalidOperationException($"Предпросмотр рига: размер {path} {frame.width}×{frame.height}, ожидался {width}×{height}.");
                    // Unity pixels start at the bottom; Canvas source rectangles start at the top.
                    atlas.SetPixels32(f%Columns*width,(Rows-1-f/Columns)*height,width,height,frame.GetPixels32());
                    Object.DestroyImmediate(frame);frame=null;
                }
                atlas.Apply();return "data:image/png;base64,"+Convert.ToBase64String(atlas.EncodeToPNG());
            }
            finally {if(frame!=null)Object.DestroyImmediate(frame);if(atlas!=null)Object.DestroyImmediate(atlas);}
        }
        private static string ImageData(string path,int width,int height)
        {
            var image=LoadPng(path);
            try
            {
                if(image.width!=width || image.height!=height)
                    throw new InvalidOperationException($"Предпросмотр рига: размер {path} {image.width}×{image.height}, ожидался {width}×{height}.");
                return "data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
            }
            finally {Object.DestroyImmediate(image);}
        }
        private static Texture2D LoadPng(string path)
        {
            if(!File.Exists(path))throw new InvalidOperationException("Предпросмотр рига: отсутствует кадр "+path);
            var bytes=File.ReadAllBytes(path);byte[] signature={137,80,78,71,13,10,26,10};
            if(bytes.Length<24)throw new InvalidOperationException("Предпросмотр рига: файл кадра обрезан: "+path);
            for(int i=0;i<signature.Length;i++)if(bytes[i]!=signature[i])
                throw new InvalidOperationException("Предпросмотр рига: файл кадра не является PNG: "+path);
            var image=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!image.LoadImage(bytes))throw new InvalidOperationException("Предпросмотр рига: PNG не декодируется: "+path);
                return image;
            }
            catch {Object.DestroyImmediate(image);throw;}
        }
        private const string Template=@"<!doctype html>
<html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Штатный 2D-риг Unity</title>
<style>
body{background:#252a32;color:#fff;font:18px/1.4 system-ui,sans-serif;margin:24px}
main{max-width:1320px;margin:auto}h1{font-size:1.7rem}.pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
figure{margin:0}canvas,img{display:block;width:100%;height:auto;aspect-ratio:@WIDTH@ / @HEIGHT@;background:#252a32}
figcaption{font-size:15px;margin-top:4px}button,input{margin:8px 12px 8px 0;font:inherit}input{width:min(45vw,450px)}
#status{min-height:1.5em;font-size:15px}#status[data-error=true]{color:#ffb3aa}#frameLabel{white-space:nowrap}
</style>
<main><h1>Новый риг: штатные SpriteSkin и Animation Clips</h1>
<p>Слева исходная внешность; справа антирад с другой формой головы. Рисунки оригинала сохранены, ноги — сплошные деформируемые сетки. Художественная обработка стыков ещё впереди.</p>
<div><button id='playButton' type='button' disabled>Загрузка…</button><input id='frameSlider' type='range' min='0' max='30' step='1' value='0' disabled aria-label='Кадр анимации'><span id='frameLabel'>Кадр 1 из 31</span></div>
<p id='status' role='status'>Подготовка кадров…</p>
<div class='pair'><figure><canvas id='leftCanvas' width='@WIDTH@' height='@HEIGHT@'>Для просмотра нужен браузер с поддержкой Canvas.</canvas><figcaption>Исходная внешность</figcaption></figure>
<figure><canvas id='rightCanvas' width='@WIDTH@' height='@HEIGHT@'>Для просмотра нужен браузер с поддержкой Canvas.</canvas><figcaption>Антирад и другая форма головы</figcaption></figure></div>
<h2>Сильный сгиб</h2><div class='pair'><img alt='Сгиб ноги, исходная внешность' width='@WIDTH@' height='@HEIGHT@' src='@LEFT_CURL@'><img alt='Сгиб ноги, антирад и другая форма головы' width='@WIDTH@' height='@HEIGHT@' src='@RIGHT_CURL@'></div>
</main>
<script>
(() => {
  'use strict';
  const leftCanvas=document.getElementById('leftCanvas');
  const rightCanvas=document.getElementById('rightCanvas');
  const button=document.getElementById('playButton');
  const slider=document.getElementById('frameSlider');
  const label=document.getElementById('frameLabel');
  const status=document.getElementById('status');
  const width=@WIDTH@,height=@HEIGHT@,count=31,columns=8;
  let frame=0,playing=true,ready=false,lastTime=0,accumulator=0;
  const leftContext=leftCanvas.getContext('2d');
  const rightContext=rightCanvas.getContext('2d');
  function load(source){const image=new Image();image.src=source;return image.decode().then(()=>image);}
  function fail(error){
    playing=false;ready=false;button.disabled=true;slider.disabled=true;button.textContent='Просмотр недоступен';
    status.dataset.error='true';status.textContent='Не удалось подготовить изображения: '+String(error.message||error);
    document.documentElement.dataset.previewReady='false';
  }
  Promise.all([load('@LEFT_ATLAS@'),load('@RIGHT_ATLAS@')]).then(([left,right])=>{
    if(!leftContext||!rightContext)throw new Error('браузер не поддерживает Canvas');
    if(left.naturalWidth!==width*columns||right.naturalWidth!==width*columns||left.naturalHeight!==height*4||right.naturalHeight!==height*4)
      throw new Error('неверный размер атласа кадров');
    function draw(){
      const x=(frame%columns)*width,y=Math.floor(frame/columns)*height;
      leftContext.clearRect(0,0,width,height);rightContext.clearRect(0,0,width,height);
      leftContext.drawImage(left,x,y,width,height,0,0,width,height);
      rightContext.drawImage(right,x,y,width,height,0,0,width,height);
      slider.value=String(frame);label.textContent='Кадр '+(frame+1)+' из '+count;
      leftCanvas.dataset.frame=String(frame);rightCanvas.dataset.frame=String(frame);
    }
    ready=true;draw();button.disabled=false;slider.disabled=false;button.textContent='Пауза';
    status.textContent='Все кадры загружены. Можно поставить просмотр на паузу или выбрать кадр ползунком.';
    document.documentElement.dataset.previewReady='true';
    button.addEventListener('click',()=>{playing=!playing;button.textContent=playing?'Пауза':'Продолжить';accumulator=0;lastTime=performance.now();});
    slider.addEventListener('input',()=>{playing=false;frame=Math.max(0,Math.min(count-1,Math.round(Number(slider.value))));button.textContent='Продолжить';accumulator=0;draw();});
    function tick(now){
      if(!ready)return;
      try{
        if(!lastTime)lastTime=now;
        if(playing){
          accumulator+=Math.min(250,Math.max(0,now-lastTime));
          const advance=Math.floor(accumulator/(1000/30));
          if(advance>0){accumulator-=advance*(1000/30);frame=(frame+advance)%count;draw();}
        }
        lastTime=now;requestAnimationFrame(tick);
      }catch(error){fail(error);}
    }
    requestAnimationFrame(tick);
  }).catch(fail);
})();
</script></html>";
    }
}
