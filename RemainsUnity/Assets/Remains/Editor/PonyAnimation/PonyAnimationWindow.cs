using System;
using System.IO;
using System.Linq;
using Remains.Content;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class PonyAnimationWindow : EditorWindow
    {
        [SerializeField] private PonyPoseDocument document,sourceAsset;
        [SerializeField] private string outfit="";
        [SerializeField] private float time,zoom=1;
        [SerializeField] private Vector2 center=new(0,.75f);
        [SerializeField] private int selected,tool;
        [SerializeField] private bool showBones=true,useIk=true,showMagic,calibrating;
        private PonyAnimationArtwork artwork;
        private PonyAnimationArtwork.Limb[] limbs;
        private PonyPoseValue[] pose;
        private Matrix4x4[] worlds;
        private string[] outfits,labels;
        private PonyAnimationPreview preview;
        private Rect viewport,worldRect;
        private Vector2 scroll,dragOffset;
        private float dragAngle;
        private bool refreshQueued,artDirty=true,playing,dragging,rotating,canCalibrate,poseValid;
        private int dragControl,dragBend,dragUndoGroup;
        private double lastTick;
        private string error,status="Перетаскивайте суставы и копыта. W — перенос, E — поворот, пробел — просмотр.";

        [MenuItem("Remains/Пони/Редактор анимаций")]
        public static void Open()
        {
            var window=GetWindow<PonyAnimationWindow>();window.titleContent=new GUIContent("Анимации пони");window.minSize=new Vector2(950,650);
        }
        public static void Edit(PonyPoseDocument source)
        {
            Open();var window=GetWindow<PonyAnimationWindow>();
            window.TryCommand(()=>{if(window.CanReplace())window.Replace(PonyPoseDocument.FromJson(source.ToJson()),source);});
        }
        private void OnEnable()
        {
            titleContent=new GUIContent("Анимации пони");minSize=new Vector2(950,650);
            Undo.undoRedoPerformed+=OnUndo;EditorApplication.update+=Tick;QueueRefresh(true);
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed-=OnUndo;EditorApplication.update-=Tick;EditorApplication.delayCall-=Refresh;
            refreshQueued=false;preview?.Dispose();preview=null;
        }
        private void OnDestroy(){if(document!=null && !EditorUtility.IsPersistent(document))DestroyImmediate(document);}
        private void OnUndo()
        {
            playing=false;
            if(document!=null)
            {
                try {hasUnsavedChanges=sourceAsset==null || document.ToJson()!=sourceAsset.ToJson();}
                catch {hasUnsavedChanges=true;} // Refresh publishes the validation error with context.
                EditorUtility.SetDirty(document);
            }
            QueueRefresh();
        }
        private void QueueRefresh(bool reloadArt=false)
        {
            artDirty|=reloadArt;if(refreshQueued)return;refreshQueued=true;EditorApplication.delayCall+=Refresh;
        }
        private void Refresh()
        {
            refreshQueued=false;if(this==null)return;
            try
            {
                if(document==null){document=PonyAnimationArtwork.CreateDocument();document.hideFlags=HideFlags.HideAndDontSave;}
                document.Validate();time=Mathf.Clamp(time,0,document.duration);
                if(artDirty || artwork==null)
                {
                    artwork=PonyAnimationArtwork.Prepare(outfit,document.sourceClip,document.sourceFrame);
                    outfits=new[]{""}.Concat(CosmeticCatalog.Current.Definitions.Where(d=>d.Slot=="armor.body").Select(d=>d.Id).OrderBy(s=>s,StringComparer.Ordinal)).ToArray();
                    labels=outfits.Select(s=>s.Length==0?"Без костюма":s).ToArray();artDirty=false;
                }
                pose=document.Sample(time);worlds=PonyPoseMath.Worlds(document,pose);limbs=PonyAnimationArtwork.Limbs(document);
                canCalibrate=PonyPoseMath.CanReanchor(document);if(!canCalibrate)calibrating=false;
                selected=Mathf.Clamp(selected,0,document.joints.Length-1);
                worldRect=WorldRect(viewport.width>0?viewport.size:new Vector2(650,500));
                preview??=new PonyAnimationPreview();
                preview.Render(artwork,artwork.Matrices(document,pose),worldRect,Mathf.RoundToInt(viewport.width>0?viewport.width:650),Mathf.RoundToInt(viewport.height>0?viewport.height:500),showMagic?1:0);
                error=null;poseValid=true;
            }
            catch(Exception problem){error=problem.Message;Debug.LogException(problem);playing=false;poseValid=false;}
            Repaint();
        }
        private Rect WorldRect(Vector2 size)
        {
            float half=1.05f/Mathf.Max(.1f,zoom),aspect=Mathf.Max(1,size.x)/Mathf.Max(1,size.y);
            return new Rect(center.x-half*aspect,center.y-half,2*half*aspect,2*half);
        }
        private void Tick()
        {
            if(!playing || document==null || EditorApplication.timeSinceStartup-lastTick<1d/document.fps)return;
            double now=EditorApplication.timeSinceStartup;time+=(float)(now-lastTick);lastTick=now;
            if(time>=document.duration){if(document.loop)time%=document.duration;else{time=document.duration;playing=false;}}
            QueueRefresh();
        }
        private void OnGUI()
        {
            Toolbar();if(document==null){EditorGUILayout.HelpBox("Подготовка куклы…",MessageType.Info);return;}
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(285)))Sidebar();
                using(new EditorGUILayout.VerticalScope())
                {
                    using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    {
                        using(new EditorGUI.DisabledScope(calibrating))tool=GUILayout.Toolbar(tool,new[]{"Перенос (W)","Поворот (E)"},EditorStyles.toolbarButton);
                        bool bones=GUILayout.Toggle(showBones,"Суставы",EditorStyles.toolbarButton);if(bones!=showBones){showBones=bones;Repaint();}
                        if(GUILayout.Button("Вписать",EditorStyles.toolbarButton))Fit();
                    }
                    var rect=GUILayoutUtility.GetRect(200,10000,300,10000,GUILayout.ExpandHeight(true),GUILayout.ExpandWidth(true));
                    if(rect.width>1 && rect.height>1 && (rect.size-viewport.size).sqrMagnitude>1){viewport=rect;QueueRefresh();}else viewport=rect;
                    if(Event.current.type==EventType.Repaint)
                    {
                        EditorGUI.DrawRect(rect,new Color(.17f,.19f,.22f));if(preview?.Texture!=null)GUI.DrawTexture(rect,preview.Texture,ScaleMode.StretchToFill,false);
                        if(showBones && worlds!=null)DrawSkeleton();
                    }
                    HandleInput();using(new EditorGUI.DisabledScope(calibrating))Timeline();
                    GUILayout.Label(error??status,error==null?EditorStyles.wordWrappedMiniLabel:EditorStyles.wordWrappedLabel,GUILayout.Height(37));
                }
            }
        }
        private void Toolbar()
        {
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if(GUILayout.Button("Новая",EditorStyles.toolbarButton,GUILayout.Width(55)) && CanReplace())Replace(PonyAnimationArtwork.CreateDocument(),null);
                if(GUILayout.Button("Пример",EditorStyles.toolbarButton,GUILayout.Width(60)))TryCommand(()=>
                {
                    if(!CanReplace())return;var example=AssetDatabase.LoadAssetAtPath<PonyPoseDocument>("Assets/Remains/Editor/PonyAnimation/HoofLift.asset");
                    if(example==null)throw new InvalidOperationException("Не найден пример анимации HoofLift.asset.");
                    Replace(PonyPoseDocument.FromJson(example.ToJson()),example);
                });
                var next=(PonyPoseDocument)EditorGUILayout.ObjectField(sourceAsset,typeof(PonyPoseDocument),false,GUILayout.MinWidth(150));
                if(next!=sourceAsset && next!=null)TryCommand(()=>{if(CanReplace())Replace(PonyPoseDocument.FromJson(next.ToJson()),next);});
                if(GUILayout.Button("Сохранить",EditorStyles.toolbarButton,GUILayout.Width(85)))TryCommand(()=>SaveDocument(false));
                if(GUILayout.Button("Копия…",EditorStyles.toolbarButton,GUILayout.Width(65)))TryCommand(()=>SaveDocument(true));
                if(GUILayout.Button("Импорт JSON…",EditorStyles.toolbarButton,GUILayout.Width(110)))TryCommand(Import);
                if(GUILayout.Button("Экспорт…",EditorStyles.toolbarDropDown,GUILayout.Width(85)))ExportMenu();
            }
        }
        private void Sidebar()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Движение",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string id=EditorGUILayout.TextField("ID",document.clipId);
            float duration=EditorGUILayout.FloatField("Длительность, с",document.duration);
            float fps=EditorGUILayout.FloatField("Кадров в секунду",document.fps);
            bool loop=EditorGUILayout.Toggle("Зациклить",document.loop);
            if(EditorGUI.EndChangeCheck())
            {
                // Editing metadata cannot silently delete or retime existing keys.
                float last=document.keys.Length==0?0:document.keys[document.keys.Length-1].time;
                if(string.IsNullOrWhiteSpace(id) || !float.IsFinite(duration) || duration<=0 || duration>600 || duration<last || !float.IsFinite(fps) || fps<1 || fps>120)
                    status="Нужны ID, 1–120 кадров/с и длительность до 600 с, не короче последнего ключа.";
                else {Record("Настроить движение");document.clipId=id;document.duration=duration;document.fps=fps;document.loop=loop;Changed();}
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("Предпросмотр",EditorStyles.boldLabel);
            if(outfits!=null)
            {
                int index=Array.IndexOf(outfits,outfit);int next=EditorGUILayout.Popup("Костюм",index,labels);
                if(next>=0 && next!=index){outfit=outfits[next];QueueRefresh(true);}
            }
            bool magic=EditorGUILayout.Toggle("Свечение рога",showMagic);if(magic!=showMagic){showMagic=magic;QueueRefresh();}
            useIk=EditorGUILayout.Toggle(new GUIContent("Тянуть копыта (IK)","Сгибает ногу, сохраняя длину двух нижних звеньев. Плечо передней ноги поворачивается отдельно."),useIk);
            using(new EditorGUI.DisabledScope(!canCalibrate))
            {
                bool next=EditorGUILayout.Toggle(new GUIContent("Настроить точки сгиба","Доступно до создания анимации: один ключ 0 в исходной стойке. Рисунок остаётся на месте, перемещаются только точки управления."),calibrating);
                if(next!=calibrating){calibrating=next;playing=false;tool=0;time=0;showBones=true;QueueRefresh();}
            }
            if(calibrating)EditorGUILayout.HelpBox("Перетаскивайте точки суставов. Рисунок и соседние точки остаются на месте. Затем выключите настройку и создавайте позы.",MessageType.Info);
            EditorGUILayout.Space();EditorGUILayout.LabelField("Суставы",EditorStyles.boldLabel);
            for(int i=0;i<document.joints.Length;i++)
            {
                var joint=document.joints[i];int depth=0,parent=joint.parent;while(parent>=0){depth++;parent=document.joints[parent].parent;}
                using(new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth*8);
                    if(GUILayout.Toggle(selected==i,joint.label,EditorStyles.miniButton))selected=i;
                }
            }
            if(pose!=null && selected<pose.Length)
            {
                EditorGUILayout.Space();EditorGUILayout.LabelField(document.joints[selected].label,EditorStyles.boldLabel);
                using(new EditorGUI.DisabledScope(calibrating))
                {
                var value=pose[selected];EditorGUI.BeginChangeCheck();
                value.position=EditorGUILayout.Vector2Field("Положение относительно родителя",value.position);
                value.angle=EditorGUILayout.FloatField("Поворот, °",value.angle);
                value.scale=EditorGUILayout.Vector2Field("Масштаб",value.scale);
                if(EditorGUI.EndChangeCheck())TryCommand(()=>{var changed=PonyPoseMath.ClonePose(pose);changed[selected]=value;Store(changed,"Изменить сустав");});
                using(new EditorGUI.DisabledScope(!useIk || limbs==null || !limbs.Any(l=>l.End==selected)))
                    if(GUILayout.Button("Перевернуть сгиб ноги"))TryCommand(FlipBend);
                if(GUILayout.Button("Сбросить этот сустав"))TryCommand(()=>{var changed=PonyPoseMath.ClonePose(pose);changed[selected]=document.joints[selected].rest;Store(changed,"Сбросить сустав");});
                }
            }
            EditorGUILayout.Space();EditorGUILayout.HelpBox("Изменения автоматически записывают ключ в текущем кадре. Средняя кнопка — перемещение камеры, колесо — масштаб. Ctrl+Z — отмена.",MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        private void Timeline()
        {
            if(document==null)return;
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button(playing?"Пауза":"▶",GUILayout.Width(65)))PlayPause();
                if(GUILayout.Button("◀",GUILayout.Width(30)))Seek(time-1/document.fps);
                float next=EditorGUILayout.Slider(time,0,document.duration);if(Math.Abs(next-time)>.00001f)Seek(next);
                if(GUILayout.Button("▶",GUILayout.Width(30)))Seek(time+1/document.fps);
                GUILayout.Label($"{Mathf.RoundToInt(time*document.fps)} / {Mathf.RoundToInt(document.duration*document.fps)}",GUILayout.Width(76));
            }
            var rail=GUILayoutUtility.GetRect(100,27,GUILayout.ExpandWidth(true));
            if(Event.current.type==EventType.Repaint)EditorGUI.DrawRect(rail,new Color(.12f,.13f,.16f));
            for(int k=0;k<document.keys.Length;k++)
            {
                float x=Mathf.Lerp(rail.x+10,rail.xMax-10,document.keys[k].time/document.duration);
                if(GUI.Button(new Rect(x-9,rail.y+3,18,21),"◆",EditorStyles.miniButton))Seek(document.keys[k].time);
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Записать ключ"))TryCommand(()=>Store(document.Sample(time),"Записать ключ"));
                int key=Array.FindIndex(document.keys,k=>Mathf.Abs(k.time-time)<.25f/document.fps);
                using(new EditorGUI.DisabledScope(key<0 || document.keys.Length<=1))if(GUILayout.Button("Удалить ключ"))TryCommand(()=>{Record("Удалить ключ");document.RemoveKey(key);Changed();});
                if(GUILayout.Button("Исходная стойка → ключ"))TryCommand(()=>Store(PonyPoseMath.Rest(document),"Вернуть исходную стойку"));
            }
        }
        private Vector2 ScreenPoint(Vector2 point)=>new(viewport.x+(point.x-worldRect.xMin)/worldRect.width*viewport.width,viewport.yMax-(point.y-worldRect.yMin)/worldRect.height*viewport.height);
        private Vector2 WorldPoint(Vector2 point)=>new(worldRect.xMin+(point.x-viewport.x)/viewport.width*worldRect.width,worldRect.yMin+(viewport.yMax-point.y)/viewport.height*worldRect.height);
        private Vector2 JointPoint(int index)=>worlds[index].MultiplyPoint3x4(Vector3.zero);
        private void DrawSkeleton()
        {
            GUI.BeginClip(viewport);Handles.BeginGUI();
            for(int i=0;i<document.joints.Length;i++)
            {
                int parent=document.joints[i].parent;var p=OverlayPoint(JointPoint(i));
                Handles.color=i==selected?new Color(1,.76f,.2f):new Color(.4f,.8f,1,.65f);
                if(parent>=0)Handles.DrawAAPolyLine(2,p,OverlayPoint(JointPoint(parent)));
                Handles.DrawSolidDisc(p,Vector3.forward,i==selected?6:4);
                if(i==selected){Handles.DrawWireDisc(p,Vector3.forward,32);GUI.Label(new Rect(p.x+10,p.y-23,200,20),document.joints[i].label,EditorStyles.whiteMiniLabel);}
            }
            Handles.color=new Color(.6f,.7f,.8f,.35f);Handles.DrawLine(OverlayPoint(new Vector2(worldRect.xMin,0)),OverlayPoint(new Vector2(worldRect.xMax,0)));Handles.EndGUI();GUI.EndClip();
        }
        private Vector2 OverlayPoint(Vector2 point)=>ScreenPoint(point)-viewport.position;
        private void HandleInput()
        {
            var e=Event.current;
            if((e.type==EventType.MouseUp || e.rawType==EventType.MouseUp) && dragging)
            {dragging=false;GUIUtility.hotControl=0;Undo.CollapseUndoOperations(dragUndoGroup);e.Use();return;}
            if(worlds==null || !poseValid)return;
            if(e.type==EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if(e.keyCode==KeyCode.W){tool=0;e.Use();}else if(e.keyCode==KeyCode.E && !calibrating){tool=1;e.Use();}
                else if(e.keyCode==KeyCode.Space && !calibrating){PlayPause();e.Use();}else if(e.keyCode==KeyCode.LeftArrow && !calibrating){Seek(time-1/document.fps);e.Use();}
                else if(e.keyCode==KeyCode.RightArrow && !calibrating){Seek(time+1/document.fps);e.Use();}
            }
            if(e.type==EventType.ScrollWheel && viewport.Contains(e.mousePosition))
            {zoom=Mathf.Clamp(zoom*Mathf.Exp(-e.delta.y*.07f),.15f,8);QueueRefresh();e.Use();return;}
            if(e.type==EventType.MouseDrag && e.button==2 && viewport.Contains(e.mousePosition))
            {center+=new Vector2(-e.delta.x/viewport.width*worldRect.width,e.delta.y/viewport.height*worldRect.height);QueueRefresh();e.Use();return;}
            int control=GUIUtility.GetControlID("PonyJointDrag".GetHashCode(),FocusType.Passive);
            if(e.type==EventType.MouseDown && e.button==0 && viewport.Contains(e.mousePosition) && showBones)
            {
                float distance=18;int nearest=-1;
                for(int i=0;i<worlds.Length;i++){float d=Vector2.Distance(ScreenPoint(JointPoint(i)),e.mousePosition);if(d<distance){nearest=i;distance=d;}}
                bool ring=Math.Abs(Vector2.Distance(ScreenPoint(JointPoint(selected)),e.mousePosition)-32)<7;
                if(nearest<0 && (!ring || calibrating))return;
                if(nearest>=0)selected=nearest;playing=false;time=document.SnapTime(time);dragging=true;rotating=!calibrating && (ring && nearest<0 || tool==1);
                dragOffset=JointPoint(selected)-WorldPoint(e.mousePosition);
                dragAngle=Angle(WorldPoint(e.mousePosition)-JointPoint(selected));
                dragBend=BendFor(selected);dragControl=control;GUIUtility.hotControl=control;Undo.IncrementCurrentGroup();dragUndoGroup=Undo.GetCurrentGroup();e.Use();Repaint();
            }
            if(e.type==EventType.MouseDrag && dragging && GUIUtility.hotControl==dragControl)
            {
                TryCommand(()=>
                {
                    Record(calibrating?"Переместить точку сгиба":"Изменить позу");
                    if(calibrating){PonyPoseMath.Reanchor(document,selected,WorldPoint(e.mousePosition)+dragOffset);pose=document.Sample(0);worlds=PonyPoseMath.Worlds(document,pose);Changed();return;}
                    var changed=PonyPoseMath.ClonePose(pose);
                    if(rotating)
                    {
                        float angle=Angle(WorldPoint(e.mousePosition)-JointPoint(selected));
                        float current=Angle(worlds[selected].MultiplyVector(Vector3.right));
                        PonyPoseMath.RotateJoint(document,changed,selected,current+Mathf.DeltaAngle(dragAngle,angle));dragAngle=angle;
                    }
                    else
                    {
                        var limb=useIk?limbs.FirstOrDefault(l=>l.End==selected):null;
                        if(limb!=null)PonyPoseMath.SolveTwoBone(document,changed,limb.Root,limb.Middle,limb.End,WorldPoint(e.mousePosition)+dragOffset,dragBend);
                        else PonyPoseMath.MoveJoint(document,changed,selected,WorldPoint(e.mousePosition)+dragOffset);
                    }
                    document.SetKey(time,changed);pose=changed;worlds=PonyPoseMath.Worlds(document,pose);Changed();
                });e.Use();
            }
        }
        private static float Angle(Vector2 direction)=>Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
        private int BendFor(int index)
        {
            var limb=limbs.FirstOrDefault(l=>l.End==index);if(limb==null)return 1;
            var a=JointPoint(limb.Middle)-JointPoint(limb.Root);var b=JointPoint(limb.End)-JointPoint(limb.Middle);
            return a.x*b.y-a.y*b.x<0?-1:1;
        }
        private void FlipBend()
        {
            var limb=limbs.First(l=>l.End==selected);var changed=PonyPoseMath.ClonePose(pose);
            PonyPoseMath.SolveTwoBone(document,changed,limb.Root,limb.Middle,limb.End,JointPoint(selected),-BendFor(selected));Store(changed,"Перевернуть сгиб");
        }
        private void Store(PonyPoseValue[] values,string label)
        {Record(label);time=document.SnapTime(time);document.SetKey(time,values);Changed();}
        private void Record(string label){Undo.RecordObject(document,label);}
        private void Changed(){hasUnsavedChanges=true;EditorUtility.SetDirty(document);QueueRefresh();}
        private void Seek(float value){playing=false;time=document.SnapTime(Mathf.Clamp(value,0,document.duration));QueueRefresh();}
        private void PlayPause(){if(document==null)return;playing=!playing;if(playing && time>=document.duration)time=0;lastTick=EditorApplication.timeSinceStartup;}
        private void Fit()
        {
            if(artwork==null || pose==null)return;
            var bounds=artwork.PoseBounds(document,pose);center=bounds.center;
            float aspect=Mathf.Max(1,viewport.width)/Mathf.Max(1,viewport.height);
            zoom=1.05f/Mathf.Max(.15f,Mathf.Max(bounds.extents.y,bounds.extents.x/aspect)*1.15f);QueueRefresh();
        }
        private void TryCommand(Action action)
        {
            try{action();error=null;}
            catch(Exception problem)
            {
                error=problem.Message;Debug.LogException(problem);playing=false;
                if(dragging){dragging=false;GUIUtility.hotControl=0;Undo.CollapseUndoOperations(dragUndoGroup);}
            }
            Repaint();
        }
        private bool CanReplace()
        {
            if(!hasUnsavedChanges)return true;
            int answer=EditorUtility.DisplayDialogComplex("Несохранённая анимация","Сохранить изменения перед сменой движения?","Сохранить","Отмена","Не сохранять");
            return answer==2 || answer==0 && SaveDocument(false);
        }
        private void Replace(PonyPoseDocument next,PonyPoseDocument asset)
        {
            next.Validate();if(document!=null && !EditorUtility.IsPersistent(document))DestroyImmediate(document);
            document=next;document.hideFlags=HideFlags.HideAndDontSave;sourceAsset=asset;time=0;selected=0;playing=false;hasUnsavedChanges=false;
            Undo.ClearUndo(document);QueueRefresh(true);
        }
        private bool SaveDocument(bool copy)
        {
            if(document==null)return false;document.Validate();
            if(sourceAsset==null || copy)
            {
                string path=EditorUtility.SaveFilePanelInProject("Сохранить анимацию пони",document.clipId,"asset","Выберите файл исходных ключей.");if(string.IsNullOrEmpty(path))return false;
                var saved=Instantiate(document);saved.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(saved,path);sourceAsset=saved;
            }
            else {Undo.RecordObject(sourceAsset,"Сохранить анимацию пони");EditorUtility.CopySerialized(document,sourceAsset);sourceAsset.hideFlags=HideFlags.None;EditorUtility.SetDirty(sourceAsset);}
            AssetDatabase.SaveAssetIfDirty(sourceAsset);hasUnsavedChanges=false;status="Анимация сохранена: "+AssetDatabase.GetAssetPath(sourceAsset);return true;
        }
        public override void SaveChanges(){if(SaveDocument(false))base.SaveChanges();}
        private void Import()
        {
            if(!CanReplace())return;string path=EditorUtility.OpenFilePanel("Открыть ключи анимации","","json");if(string.IsNullOrEmpty(path))return;
            Replace(PonyPoseDocument.FromJson(File.ReadAllText(path)),null);hasUnsavedChanges=true;
        }
        private void ExportMenu()
        {
            var menu=new GenericMenu();
            menu.AddItem(new GUIContent("Ключи редактора (JSON, можно открыть снова)"),false,()=>TryCommand(()=>WriteJson(false)));
            menu.AddItem(new GUIContent("Кадры куклы (PonyRigClip + ссылка на рисунок)"),false,()=>TryCommand(()=>WriteJson(true)));
            menu.AddItem(new GUIContent("Текущий рисунок (PNG)"),false,()=>TryCommand(()=>
            {string path=EditorUtility.SaveFilePanel("Сохранить рисунок","",document.clipId,"png");if(path.Length>0)preview.WritePng(path);}));menu.ShowAsContext();
        }
        private void WriteJson(bool compiled)
        {
            string path=EditorUtility.SaveFilePanel("Экспорт анимации","",document.clipId+(compiled?"-rig":""),"json");if(string.IsNullOrEmpty(path))return;
            File.WriteAllText(path,compiled?PonyAnimationExport.RigJson(document,artwork):document.ToJson());status="Экспорт сохранён: "+path;
        }
    }
}
