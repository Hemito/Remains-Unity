using System;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class CameraFramingChecks
    {
        public static void Run()
        {
            var root=new GameObject("Проверка авторской камеры");
            try
            {
                var camera=root.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=7.25f;
                camera.backgroundColor=Color.magenta;camera.transform.position=new Vector3(1,4,-10);
                _=new CameraFollower(camera,new Remains.Domain.RoomLayout());
                if(camera.orthographicSize!=7.25f || camera.backgroundColor!=Color.magenta || camera.transform.position!=new Vector3(1,4,-10))
                    throw new InvalidOperationException("Запуск камеры затирает авторские параметры сцены.");
                camera.orthographic=false;bool rejected=false;
                try {CameraFollower.RequireCamera(camera);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Перспективная камера молча принята для 2D-комнаты.");
                rejected=false;
                try {CameraFollower.RequireCamera(null);} catch(InvalidOperationException) {rejected=true;}
                if(!rejected) throw new InvalidOperationException("Отсутствующая камера скрыта runtime-подстановкой.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            foreach(var sample in new[] {
                new[]{8f,-.7f,2.5f,8f},new[]{-8f,-2.5f,.7f,8f},
                new[]{5f,-.4f,1.8f,5f},new[]{-5f,-1.8f,.4f,5f}})
            {
                float center=CameraFollower.FrameAxis(sample[0],sample[1],sample[2],sample[3]);
                if(center-sample[3]>sample[1]-.149f || center+sample[3]<sample[2]+.149f)
                    throw new InvalidOperationException("Камера обрезает границы позы у края.");
            }
            if(CameraFollower.FrameAxis(2,0,4,5)!=2)
                throw new InvalidOperationException("Камера сдвигает уже видимую позу.");
        }
    }
}
