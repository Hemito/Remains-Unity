using System;
using System.IO;
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
            Debug.Log("Числа урона: PASS — фактическая потеря HP, промах, повтор, неверное значение.");
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for(int i=0;i<3;i++)
            {
                var number=new GameObject("Пример урона",typeof(RectTransform)).AddComponent<FloatingDamageNumber>();
                number.Initialize(new DamageResult {amount=new[]{7f,11f,70f}[i],x=i*2-2,y=0},font,1);number.Draw(i*.45f);
            }
            Canvas.ForceUpdateCanvases();
            var camera=new GameObject("Камера чисел").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2;
            camera.transform.position=new Vector3(0,2,-10);camera.backgroundColor=new Color(.12f,.13f,.14f);
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
    }
}
