using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class InteractionVisuals : MonoBehaviour
    {
        private CoopSession session;
        private GrenadeController grenades;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,SpriteRenderer> props=new();
        private readonly Dictionary<string,SpriteRenderer> weapons=new();
        private readonly Dictionary<string,SpriteRenderer> flights=new();
        private readonly HashSet<string> missingGraphics=new();
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades)
        {
            this.session=session;this.grenades=grenades;
            foreach(var key in new[]{"crate","grenade"}) sprites.Add(key,Load("Original/Props/"+key));
            foreach(var key in new[]{"spear","crowbar","pistol","energy"}) sprites.Add(key,Load("Original/Weapons/"+key));
            session.Changed+=Refresh;
            adapter.GrenadeReceived+=message=>flights.Add(message.id,Create("Граната "+message.id,sprites["grenade"],0.35f));
            session.AttackReceived+=attack=>
            {
                if(attack.weapon is "spear" or "crowbar" or "grenade" || attack.level!=session.LocalActor?.level) return;
                var shot=new GameObject("Выстрел "+attack.id).AddComponent<ShotVisual>();shot.Initialize(attack);
            };
        }
        private static Sprite Load(string path)
        {
            var texture=Resources.Load<Texture2D>(path);
            if(texture==null) throw new InvalidOperationException($"Отсутствует визуальный ресурс: {path}.");
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100);
        }
        private static SpriteRenderer Create(string name,Sprite sprite,float width)
        {
            var renderer=new GameObject(name).AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sortingOrder=100;
            renderer.transform.localScale=Vector3.one*(width/sprite.bounds.size.x);
            return renderer;
        }
        private void Refresh()
        {
            if(session.World==null)
            {
                foreach(var renderer in props.Values) renderer.enabled=false;
                foreach(var renderer in weapons.Values) renderer.enabled=false;
                foreach(var renderer in flights.Values) renderer.enabled=false;
                return;
            }
            foreach(var pair in weapons) if(session.World.Find(pair.Key)==null) pair.Value.enabled=false;
            foreach(var prop in session.World.props) if(!props.ContainsKey(prop.id)) props.Add(prop.id,Create(prop.id,sprites["crate"],0.8f));
            foreach(var actor in session.World.actors) if(!weapons.ContainsKey(actor.id)) weapons.Add(actor.id,Create("Оружие "+actor.id,sprites["pistol"],0.8f));
        }
        private void Update()
        {
            if(session?.World==null) return;
            foreach(var prop in session.World.props)
                if(props.TryGetValue(prop.id,out var view)) {view.transform.position=prop.Position;view.enabled=prop.level==session.LocalActor?.level;}
            foreach(var actor in session.World.actors)
            {
                if(!weapons.TryGetValue(actor.id,out var view)) continue;
                view.enabled=!actor.down && actor.level==session.LocalActor?.level;
                if(!sprites.TryGetValue(actor.weapon,out var sprite))
                { if(missingGraphics.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Отсутствует графика оружия {actor.weapon} у {actor.id}.",this);view.enabled=false;continue; }
                float width=actor.weapon=="spear"?1.6f:actor.weapon=="crowbar"?1.2f:0.8f;
                if(view.sprite!=sprite) { view.sprite=sprite;view.transform.localScale=Vector3.one*(width/sprite.bounds.size.x); }
                var direction=new Vector2(Mathf.Cos(actor.weaponAngle*Mathf.Deg2Rad),Mathf.Sin(actor.weaponAngle*Mathf.Deg2Rad));
                view.transform.position=actor.Position+new Vector2(0,0.8f)+direction*(actor.weapon is "spear" or "crowbar"?actor.weaponReach-width/2:0.6f);
                view.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);
            }
            foreach(var pair in flights) pair.Value.enabled=false;
            foreach(var flight in grenades.Flights) if(flights.TryGetValue(flight.message.id,out var view))
            { view.enabled=flight.message.level==session.LocalActor?.level;view.transform.position=flight.position; }
        }
    }

    public sealed class ShotVisual : MonoBehaviour
    {
        private float expires;
        public void Initialize(AttackEvent attack)
        {
            var line=gameObject.AddComponent<LineRenderer>();line.positionCount=2;
            line.SetPosition(0,attack.Origin);line.SetPosition(1,attack.End);
            line.startWidth=line.endWidth=0.025f;
            line.material=new Material(Shader.Find("Sprites/Default"));
            line.startColor=line.endColor=attack.weapon=="energy"?Color.green:new Color(1,0.8f,0.3f);
            line.sortingOrder=120;expires=Time.time+0.08f;
        }
        private void Update() { if(Time.time>=expires) Destroy(gameObject); }
        private void OnDestroy() { var line=GetComponent<LineRenderer>();if(line!=null) Destroy(line.material); }
    }
}
