using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Gameplay
{
    public static class RangedShotPreparation
    {
        public static void ValidateOwner(AttackEvent shot,ActorState actor,string worldId,WeaponDefinition definition,IWeaponRecoilRule curve)
        {
            FactoryEnemyScaling.ValidateCaptured(shot,actor);
            string context=$"world={worldId}, actor={actor.id}, shot={shot.id}, weapon={shot.weapon}";
            var profile=WeaponRecoilProfile.ValidateForWeapon(definition,context);
            if(profile?.enabled==true) WeaponRecoilSampling.ValidateSample(curve,profile,0,context);
            var capture=shot.rangedPose??throw new InvalidOperationException("Новый выстрел не содержит позу действительного оружия: "+context);
            if(capture.worldId==null) capture.worldId=worldId; // NPC's domain producer does not own the session/world binding.
            capture.Validate(context);
            if(capture.worldId!=worldId || capture.actorGeneration!=actor.generation || capture.actorLifecycle!=actor.lifecycleSequence ||
                capture.weaponItem!=actor.equippedWeaponItem || capture.selectionRevision!=actor.weaponSelectionRevision || actor.weapon!=shot.weapon)
                throw new InvalidOperationException("Изменился контекст оружия до подготовки выстрела: "+context);
            var pose=WeaponPose.SampleRanged(actor);
            if(Vector2.Distance(pose.Root,new Vector2(capture.rootX,capture.rootY))>.0001f ||
                Mathf.Abs(Mathf.DeltaAngle(pose.Angle,capture.angleDegrees))>.001f || pose.Mirrored!=capture.mirrored ||
                Vector2.Distance(pose.Muzzle,shot.Origin)>.0001f || Vector2.Distance(pose.Direction,(shot.End-shot.Origin).normalized)>.0001f)
                throw new InvalidOperationException("Физический выстрел расходится с захваченной позой ствола: "+context);
            capture.hasCasing=CasingEffectCatalog.TryGet(shot.weapon,out var casing);
            if(capture.hasCasing)
            {
                casing.Validate(context);var socket=pose.Socket(casing.Socket);
                capture.casingX=socket.x;capture.casingY=socket.y;
            }
        }
        // The receiver checks historical data, never its current cursor or another owner's curve.
        public static void ValidateReceived(AttackEvent shot,string worldId)
        {
            string context=$"world={worldId}, shot={shot.id}, actor={shot.attacker}";
            var captured=shot.rangedPose??throw new InvalidOperationException("Запуск не содержит историческую позу оружия: "+context);
            captured.Validate(context);
            if(captured.worldId!=worldId) throw new InvalidOperationException("Выстрел относится к другому миру: "+context);
            var rig=WeaponRigs.Get(WeaponRigs.PresentationId(shot.weapon));
            var pose=new RangedWeaponPose(new Vector2(captured.rootX,captured.rootY),captured.angleDegrees,captured.mirrored,rig);
            if(Vector2.Distance(pose.Muzzle,shot.Origin)>.0001f)
                throw new InvalidOperationException("Историческая поза не совпадает с исходным дулом: "+context);
        }
    }
}
