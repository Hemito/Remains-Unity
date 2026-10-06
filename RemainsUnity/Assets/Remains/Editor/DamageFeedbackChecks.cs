using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class DamageFeedbackChecks
    {
        private sealed class HeavyHit : IDamageRule {public float Calculate(string weapon,float speed)=>100;}
        public static void Run()
        {
            var actor=new ActorState {id="target",health=7,level="hub",generation=3};
            var attack=new AttackEvent {id="overkill",level="hub",weapon="pistol",attacker="other"};
            var receiver=new DamageReceiver(new HeavyHit());int count=0;DamageResult result=default;
            receiver.Applied+=value=>{count++;result=value;};
            if(receiver.Apply(actor,attack,false) || count!=0) throw new Exception("Промах породил число урона.");
            if(!receiver.Apply(actor,attack,true) || count!=1 || result.amount!=7 || result.actor!=actor.id || result.generation!=3 || !DamageFeedback.Valid(result))
                throw new Exception("Число урона не соответствует фактически снятым 7 HP при ударе на 100.");
            receiver.Apply(actor,attack,true);
            if(count!=1) throw new Exception("Повтор урона породил второе число.");
            result.amount=float.NaN;if(DamageFeedback.Valid(result)) throw new Exception("Принят NaN урона.");
            var burst=CreateBurst();foreach(var number in burst) UnityEngine.Object.DestroyImmediate(number.gameObject);
            Debug.Log("Числа урона: PASS — фактическая потеря HP, промах, повтор, неверное значение.");
        }
        private static List<FloatingDamageNumber> CreateBurst()
        {
            var prefab=Resources.Load<FloatingDamageNumber>("Presentation/DamageNumber");
            if(prefab==null) throw new Exception("Нет префаба числа урона.");prefab.Validate();
            var result=new List<FloatingDamageNumber>();
            foreach(float amount in new[]{11f,11f,1f,70f,.5f,1000f})
            {
                var number=UnityEngine.Object.Instantiate(prefab);
                number.Initialize(new DamageResult {amount=amount,x=0,y=0});
                number.PlaceAbove(result.Select(x=>x.Bounds).ToArray());
                foreach(var previous in result)
                    if(number.Bounds.Overlaps(previous.Bounds)) throw new Exception("Числа последовательных попаданий перекрываются.");
                result.Add(number);
            }
            foreach(var number in result) number.Draw(.3f);
            for(int i=0;i<result.Count;i++)
                for(int j=i+1;j<result.Count;j++)
                    if(result[i].Bounds.Overlaps(result[j].Bounds)) throw new Exception("Движение чисел нарушило раздельное размещение.");
            return result;
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            CreateBurst();
            Canvas.ForceUpdateCanvases();
            var camera=new GameObject("Камера чисел").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=3;
            camera.transform.position=new Vector3(0,3,-10);camera.backgroundColor=new Color(.12f,.13f,.14f);
            var target=new RenderTexture(1200,600,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1200,600,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1200,600),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/damage-numbers.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Кадр чисел урона: "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
        }
        public static void RenderLayered()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();
            PonyVisual.Prepare();
            var layers=new ActorVisualLayers();
            var world=new WorldState();world.actors.Add(new ActorState {id="rear",hair=0,x=-.18f});world.actors.Add(new ActorState {id="front",hair=1,x=.18f});
            layers.Orders.Reconcile(world,world.actors[0].level);
            foreach(var actor in world.actors)
            {
                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();visual.Bind(actor);visual.DrawPose(.12f);
                layers.Attach(actor.id,visual.transform);
            }
            layers.Synchronize(world,world.actors[0].level);
            var explosion=UnityEngine.Object.Instantiate(Resources.Load<ExplosionVisual>("Presentation/Explosion"));
            explosion.Initialize(null,"hub",new Vector2(0,.8f));explosion.Draw(.1f);
            var numbers=CreateBurst();
            int effectOrder=explosion.GetComponentInChildren<SpriteRenderer>().sortingOrder;
            foreach(var number in numbers)
                if(number.GetComponent<Canvas>().sortingOrder<=effectOrder)
                    throw new InvalidOperationException("Число урона находится под взрывом по порядку отрисовки.");
            Canvas.ForceUpdateCanvases();UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Камера боевых слоёв").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.5f;
            camera.transform.position=new Vector3(0,2,-10);camera.backgroundColor=new Color(.12f,.13f,.14f);
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/damage-layered.png"));
            Remains.Runtime.NetworkFrameCapture.Save(camera,path,width:1920,height:1080);
        }
    }
}
