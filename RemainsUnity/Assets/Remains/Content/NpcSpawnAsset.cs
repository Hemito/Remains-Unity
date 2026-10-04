using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Начальное состояние NPC",fileName="NpcSpawn")]
    public sealed class NpcSpawnAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Тип персонажа")] private string actorType;
        [SerializeField,InspectorName("Анатомия")] private string anatomy;
        [SerializeField,InspectorName("Начальное здоровье")] private float health=100;
        [SerializeField,InspectorName("Оружие (пусто — без оружия)")] private string weapon;
        public void Configure(string type,string body,float hp,string weaponId)
        {actorType=type;anatomy=body;health=hp;weapon=weaponId;Validate();}
        private void Validate()
        {
            if(string.IsNullOrWhiteSpace(actorType) || string.IsNullOrWhiteSpace(anatomy) || !float.IsFinite(health) || health<=0)
                throw new InvalidOperationException($"Некорректное начальное состояние NPC: {name}, type={actorType}, anatomy={anatomy}, hp={health}.");
        }
        public ActorState Create(TrainingPlacement placement,string level)
        {
            Validate();
            if(placement.type!=actorType) throw new InvalidOperationException("Тип расстановки не совпадает с NPC: "+placement.id);
            return new ActorState {id=level+":"+placement.id,level=level,actorType=actorType,anatomy=anatomy,
                health=health,weapon=weapon,x=placement.x,y=placement.y,facing=placement.facing,owner=null};
        }
        private void OnValidate() {try {Validate();} catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}}
    }
}
