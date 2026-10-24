using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using Remains.Content;

namespace Remains.Presentation
{
    public sealed class ExplosionEffects : MonoBehaviour
    {
        private CoopSession session;
        private ExplosionVisual prefab;
        private WorldVisualOrderTable orders;
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        public int Shown {get;private set;}
        public event Action<ExplosionVisual> Created;
        public void Initialize(CoopSession session,WorldVisualOrderTable orderTable)
        {this.session=session;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));prefab=GameAssets.Load<ExplosionVisual>("Presentation/Explosion");if(prefab==null) throw new InvalidOperationException("Нет префаба взрыва Presentation/Explosion.");prefab.Validate();session.AttackReceived+=Attack;}
        private void Attack(AttackEvent attack)
        {
            if(!Remains.Content.WeaponCatalog.IsExplosive(attack.weapon) || attack.level!=session.LocalActor?.level || !seen.Add(attack.id)) return;
            recent.Enqueue(attack.id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            var effect=Instantiate(prefab,transform,false);effect.name="Взрыв "+attack.id;
            var renderer=effect.GetComponentInChildren<SpriteRenderer>(true);
            effect.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,"explosion:"+attack.id,null,renderer,attack.level);
            effect.Initialize(session,attack.level,attack.Origin);Shown++;Created?.Invoke(effect);
        }
        // A blast picture without damage (a destroyed turret's "miniexpl"), scaled down; nothing is recorded in the world.
        public void PlayCosmetic(string id,string level,Vector2 position,float scale)
        {
            if(level!=session.LocalActor?.level || !float.IsFinite(scale) || scale<=0 || !seen.Add(id)) return;
            recent.Enqueue(id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            var effect=Instantiate(prefab,transform,false);effect.name="Взрыв (без урона) "+id;effect.transform.localScale=Vector3.one*scale;
            var renderer=effect.GetComponentInChildren<SpriteRenderer>(true);
            effect.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,"explosion:"+id,null,renderer,level);
            effect.Initialize(session,level,position);Shown++;Created?.Invoke(effect);
        }
        private void OnDestroy() {if(session!=null) session.AttackReceived-=Attack;}
    }
}
