using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class RoomLightAuthoringChecks
    {
        public static void Run()
        {
            foreach(string level in new[]{"hub","factory:0"})
            {
                var source=RoomAuthoring.Load(level);var instance=UnityEngine.Object.Instantiate(source);
                try
                {
                    var lighting=instance.GetComponent<RoomLightingAuthoring>();var before=lighting.CreateProfile(instance);
                    if(before.lights.Length==0) throw new InvalidOperationException("Нет источников света: "+level);
                    var light=instance.GetComponentInChildren<RoomLightAuthoring>();var fixture=light.GetComponentInChildren<MeshRenderer>();
                    if(fixture==null) throw new InvalidOperationException("Светильник не привязан к источнику: "+light.name);
                    var fixturePosition=fixture.transform.position;light.transform.localPosition+=new Vector3(2,1,0);
                    var after=lighting.CreateProfile(instance);
                    if(Mathf.Abs(after.lights[0].x-before.lights[0].x-2)>.00001f || Mathf.Abs(after.lights[0].maskX-before.lights[0].maskX-2)>.00001f ||
                        Mathf.Abs(after.lights[0].maskY-before.lights[0].maskY-1)>.00001f || (fixture.transform.position-fixturePosition-new Vector3(2,1,0)).sqrMagnitude>.000001f)
                        throw new InvalidOperationException("Свет, маска и изображение расходятся после перемещения.");
                    if(before.lights[0].maskTexture==null) throw new InvalidOperationException("Маска не передана прямой ссылкой.");
                    light.gameObject.SetActive(false);
                    if(lighting.CreateProfile(instance).lights.Length!=before.lights.Length-1) throw new InvalidOperationException("Отключённый источник продолжает светить.");
                    if(source.GetComponent<RoomLightingAuthoring>().CreateProfile(source).lights[0].x!=before.lights[0].x) throw new InvalidOperationException("Правка экземпляра изменила префаб света.");
                    if(level=="hub")
                    {
                        int rangeCount=0;
                        foreach(var rangeLight in instance.GetComponentsInChildren<RoomLightAuthoring>())
                        {
                            var data=rangeLight.Read(instance);if(!data.id.StartsWith("training-lamp-",StringComparison.Ordinal)) continue;
                            var image=rangeLight.GetComponentInChildren<RoomArtworkAuthoring>();
                            if(image==null) throw new InvalidOperationException("Лампа тира отделена от света: "+data.id);
                            var position=image.transform.position;var offset=new Vector3(1,2,0);
                            rangeLight.transform.position+=offset;
                            if((image.transform.position-position-offset).sqrMagnitude>.000001f || Mathf.Abs(rangeLight.Read(instance).y-data.y-2)>.00001f)
                                throw new InvalidOperationException("Лампа тира и свет перемещаются раздельно: "+data.id);
                            rangeLight.gameObject.SetActive(false);
                            if(image.gameObject.activeInHierarchy || Array.Exists(lighting.CreateProfile(instance).lights,item=>item.id==data.id))
                                throw new InvalidOperationException("Отключённая лампа тира остаётся активной: "+data.id);
                            rangeCount++;
                        }
                        if(rangeCount!=7) throw new InvalidOperationException("Не проверены все семь ламп тира.");
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(instance.gameObject);}
            }
            Debug.Log("Авторинг света: PASS — положение света/маски/светильника, прямые ссылки, отключение и изоляция.");
        }
    }
}
