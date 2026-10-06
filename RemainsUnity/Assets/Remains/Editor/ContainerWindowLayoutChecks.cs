using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Remains.Editor
{
    public static class ContainerWindowLayoutChecks
    {
        public static void Run()
        {
            RunPure();
            var parent=new GameObject("Проверка размещения контейнера",typeof(RectTransform),typeof(Canvas));
            var foreign=new GameObject("Чужой Canvas",typeof(RectTransform),typeof(Canvas));
            parent.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            foreign.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            try
            {
                var view=ContainerWindowView.Create(parent.transform);view.Validate();
                Require(view.Rect.rect.size==new Vector2(228,214),"Изменился размер настоящей сетки");
                var widget=new GameObject("Защищённый HUD",typeof(RectTransform));widget.transform.SetParent(parent.transform,false);
                var rect=(RectTransform)widget.transform;
                var refs=new[] {rect};view.ConfigureLayout(refs);refs[0]=null;
                CheckPointerCapture(view);CheckUnavailable(view,(RectTransform)parent.transform);
                Reject(()=>view.ConfigureLayout(new[]{rect,rect}),"дубликаты HUD");
                Reject(()=>view.ConfigureLayout(new[]{view.Rect}),"само окно как HUD");
                Reject(()=>view.ConfigureLayout(new[]{(RectTransform)foreign.transform}),"чужой Canvas");
                Reject(()=>view.ConfigureLayout(new RectTransform[]{null}),"потерянный HUD");
                parent.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                Reject(()=>view.ConfigureLayout(Array.Empty<RectTransform>()),"WorldSpace Canvas");
            }
            finally {UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(foreign);}
            Debug.Log("Размещение контейнера: PASS — реальные сетка/Canvas, исходные bookcase/trash проекции, HUD/тело, края, малая область и явный отказ.");
        }
        private static void CheckUnavailable(ContainerWindowView view,RectTransform canvas)
        {
            var cameraObject=new GameObject("Камера проверки контейнера",typeof(Camera));int warnings=0;
            Application.LogCallback observe=(message,stack,type)=>
            {if(type==LogType.Warning && message.Contains("сетка скрыта — нет свободного места")) warnings++;};
            try
            {
                var camera=cameraObject.GetComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
                camera.orthographic=true;camera.orthographicSize=5;camera.pixelRect=new Rect(0,0,1280,720);
                canvas.sizeDelta=new Vector2(1280,720);
                view.ConfigureLayout(new[]{canvas});Application.logMessageReceived+=observe;
                int evaluations=view.LayoutEvaluationCount;
                view.Follow(camera,new Rect(-.2f,-.2f,.4f,.4f));view.Follow(camera,new Rect(-.2f,-.2f,.4f,.4f));
                Require(!view.PlacementAvailable && !view.gameObject.activeSelf && warnings==1,
                    "Реальное окно без места не скрыто или не дало единственную диагностику");
                Require(view.LayoutEvaluationCount==evaluations+1,"Неизменные inputs повторили packing");
                view.ConfigureLayout(Array.Empty<RectTransform>());view.Follow(camera,new Rect(-.2f,-.2f,.4f,.4f));
                Require(view.PlacementAvailable && view.gameObject.activeSelf,"Освобождение места не вернуло сетку");
            }
            finally {Application.logMessageReceived-=observe;UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
        private static void CheckPointerCapture(ContainerWindowView view)
        {
            var left=new PointerEventData(null) {button=PointerEventData.InputButton.Left,pointerId=11};
            var other=new PointerEventData(null) {button=PointerEventData.InputButton.Left,pointerId=12};
            var right=new PointerEventData(null) {button=PointerEventData.InputButton.Right,pointerId=13};
            var cell=view.Slots[0].GetComponent<ContainerPointerCapture>();
            var rail=view.Scrollbar.GetComponent<ContainerPointerCapture>();
            var background=view.GetComponent<ContainerPointerCapture>();
            Require(cell!=null && rail!=null && background!=null,"Нет захвата фона/ячейки/прокрутки");
            background.OnPointerDown(right);Require(!view.PointerCaptured,"ПКМ создала лишний захват");
            cell.OnPointerDown(left);rail.OnPointerDown(other);
            Require(view.PointerCaptured && view.ContainsPointer(new Vector2(-10000,-10000)),"Удержание ЛКМ вне окна утекло в gameplay");
            cell.OnPointerUp(left);Require(view.PointerCaptured,"Один release потерял второй указатель");
            rail.OnPointerUp(other);Require(!view.PointerCaptured,"Release сохранил устаревший pointer ID");
            string submitted=null;var intent=view.Intents[0];intent.Initialize(view.Slots[0],id=>submitted=id);intent.Bind("pressed-item");
            cell.OnPointerDown(left);intent.OnPointerDown(left);intent.Bind("replacement-item");
            cell.OnPointerUp(left);intent.OnPointerClick(left);
            Require(submitted=="pressed-item" && !view.PointerCaptured,"PointerUp доClick подменил стабильный предмет");
            cell.OnPointerDown(left);view.Scrollbar.gameObject.SetActive(false);
            // Normal MonoBehaviour lifecycle messages are not dispatched by
            // SetActive in Edit Mode. Invoke the production callbacks explicitly;
            // the runtime probe covers actual player activation/event delivery.
            InvokeDisable(rail);
            Require(view.PointerCaptured,"Скрытие чужого scrollbar прервало удержание ячейки");
            view.Slots[0].gameObject.SetActive(false);InvokeDisable(cell);
            Require(!view.PointerCaptured,"Скрытая ячейка сохранила pointer ID");
            view.Slots[0].gameObject.SetActive(true);view.Scrollbar.gameObject.SetActive(true);
            background.OnPointerDown(left);background.OnCancel(new BaseEventData(null));Require(!view.PointerCaptured,"Cancel сохранил capture");
            rail.OnPointerDown(other);view.gameObject.SetActive(false);InvokeDisable(background);
            Require(!view.PointerCaptured,"Закрытие сохранило capture");
            view.gameObject.SetActive(true);Require(!view.PointerCaptured,"Повторное открытие восстановило устаревший pointer ID");
        }
        private static void InvokeDisable(ContainerPointerCapture target)=>
            typeof(ContainerPointerCapture).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(target,null);
        // Uses only managed Rect/Vector operations; can run outside Unity.
        public static void RunPure()
        {
            var canvas=new Rect(-640,-360,1280,720);var size=new Vector2(228,214);
            var none=Array.Empty<Rect>();var source=new Rect(-20,-80,40,50);
            Require(Place(canvas,size,source,none,out var natural),"Нет места над обычным контейнером");
            Require(natural.center.x==source.center.x && natural.yMin==source.yMax+12,"Утрачена привязка над источником");
            var shifted=source;shifted.x+=80;shifted.y+=20;
            Require(Place(canvas,size,shifted,none,out var follows) && follows.position-natural.position==new Vector2(80,20),"Окно не следует за источником");
            // Actual world/camera coordinates from host furniture evidence, 720/10=72 canvas units/metre.
            var bookcase=Project(new Rect(112.800006f,11.2f,1.6f,2.4f),new Vector2(106.311111f,12.2f));
            var topRight=new Rect(394,281,230,63);
            var ownBookcase=Project(new Rect(111.7f,11.2f,1,1.4f),new Vector2(106.311111f,12.2f));
            var bookObstacles=new[]{topRight,ownBookcase};
            var oldBook=Legacy(canvas,size,bookcase);
            Require(oldBook.Overlaps(topRight),"Bookcase fixture не воспроизводит исходное перекрытие HUD");
            Require(Place(canvas,size,bookcase,bookObstacles,out var book) && book.yMin>=bookcase.yMax+12,
                "Bookcase не осталось над источником при свободном месте слева");
            Clear(canvas,book,bookcase,bookObstacles);
            var trash=Project(new Rect(92.15f,11.2f,.5f,.8f),new Vector2(90.6f,12.2f));
            var ownTrash=Project(new Rect(90.1f,11.2f,1,1.4f),new Vector2(90.6f,12.2f));
            var notice=new Rect(-122,177,400,43);
            var trashObstacles=new[]{notice,ownTrash};
            Require(Legacy(canvas,size,trash).Overlaps(notice),"Trash fixture не воспроизводит уведомление поверх сетки");
            Require(Place(canvas,size,trash,trashObstacles,out var trashWindow),"Нет места рядом с trash");
            Clear(canvas,trashWindow,trash,trashObstacles);
            var aboveActor=new Rect(-20,-110,40,50);var body=new Rect(-35,-120,70,135);
            Require(Place(canvas,size,aboveActor,new[]{body},out var clearBody),"Нет обхода тела над низким контейнером");
            Clear(canvas,clearBody,aboveActor,new[]{body});
            for(int x=-600;x<=600;x+=100) for(int y=-300;y<=300;y+=60)
            {
                var edge=new Rect(x-12,y-20,24,40);
                Require(Place(canvas,size,edge,new[]{topRight,notice},out var placed),"Не найдено место у края "+edge);
                Clear(canvas,placed,edge,new[]{topRight,notice});
                Require(Place(canvas,size,edge,new[]{topRight,notice},out var repeated) && repeated==placed,"Недетерминированное размещение");
            }
            // A top source cannot fit an above window: explicit free-side placement.
            var topSource=new Rect(-30,320,60,30);
            Require(Place(canvas,size,topSource,none,out var side) && side.yMax<=352,"Не сработала граница экрана");
            Clear(canvas,side,topSource,none);
            Require(!Place(new Rect(0,0,220,200),size,source,none,out _),"Слишком малый экран не отказал явно");
            Require(!Place(canvas,size,source,new[]{canvas},out _),"Полностью занятая область должна отказать");
            var movedCanvas=canvas; movedCanvas.position+=new Vector2(700,400);var movedSource=source;movedSource.position+=new Vector2(700,400);
            Require(Place(movedCanvas,size,movedSource,none,out var translated) && translated.position-natural.position==new Vector2(700,400),"Расчёт зависит от pivot Canvas");
            Reject(()=>Place(canvas,size,new Rect(float.NaN,0,1,1),none,out _),"NaN источника");
            Reject(()=>Place(canvas,new Vector2(-1,200),source,none,out _),"отрицательный размер");
            Reject(()=>ContainerWindowLayout.TryPlace(canvas,size,source,none,1,12,8,6,out _),"неверное число препятствий");
        }
        private static Rect Project(Rect world,Vector2 camera)=>new((world.x-camera.x)*72,(world.y-camera.y)*72,world.width*72,world.height*72);
        private static bool Place(Rect canvas,Vector2 size,Rect source,Rect[] reserved,out Rect result)=>
            ContainerWindowLayout.TryPlace(canvas,size,source,reserved,reserved.Length,12,8,6,out result);
        private static Rect Legacy(Rect canvas,Vector2 size,Rect source)=>new(
            Mathf.Clamp(source.center.x,canvas.xMin+8+size.x*.5f,canvas.xMax-8-size.x*.5f)-size.x*.5f,
            Mathf.Clamp(source.yMax+12,canvas.yMin+8,canvas.yMax-8-size.y),size.x,size.y);
        private static void Clear(Rect canvas,Rect window,Rect source,Rect[] reserved)
        {
            Require(window.xMin>=canvas.xMin+8 && window.xMax<=canvas.xMax-8 && window.yMin>=canvas.yMin+8 && window.yMax<=canvas.yMax-8,"Окно вне Canvas");
            Require(!ContainerWindowLayout.Blocked(window,source,6),"Окно закрывает источник");
            foreach(var obstacle in reserved) Require(!ContainerWindowLayout.Blocked(window,obstacle,6),"Окно закрывает HUD/тело");
        }
        private static void Reject(Action action,string context)
        {bool rejected=false;try {action();} catch(ArgumentException) {rejected=true;} catch(InvalidOperationException) {rejected=true;}Require(rejected,"Не отвергнуто: "+context);}
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Размещение контейнера: "+reason);}
    }
}
