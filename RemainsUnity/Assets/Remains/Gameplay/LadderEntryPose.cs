using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Gameplay
{
    // An explicit Unity presentation policy for a grip acquired near a solid floor.
    // Terrain motion owns the body; this service only proposes the shared pose ID.
    public sealed class LadderEntryPose
    {
        private readonly string entryPose,heldPose;
        private readonly ActorState sample=new();

        public LadderEntryPose(string entryPose="jump",string heldPose="climb")
        {
            if(string.IsNullOrWhiteSpace(entryPose) || string.IsNullOrWhiteSpace(heldPose) || entryPose==heldPose)
                throw new ArgumentException("Вход и удержание лестницы должны иметь разные явные позы.");
            this.entryPose=entryPose;this.heldPose=heldPose;
        }

        public string Select(ActorState actor,RoomLayout room)
        {
            if(actor==null || room==null || !actor.climbing || ActorSessionRules.IsPassive(actor))
                throw new InvalidOperationException($"Выбор входа требует активного хвата: actor={actor?.id}, room={room?.id}.");
            sample.id=actor.id;sample.anatomy=actor.anatomy;sample.level=actor.level;
            sample.player=actor.player;sample.connected=actor.connected;sample.down=actor.down;
            sample.Position=actor.Position;sample.Velocity=actor.Velocity;
            sample.facing=actor.facing;sample.climbing=true;
            // This is the exact phase that ActorPose.Select will publish. A climb
            // cycle never advances privately while another pose is being drawn.
            sample.pose=heldPose;sample.poseTime=actor.pose==heldPose?actor.poseTime:0;
            if(ClearContacts(room)) return heldPose;
            sample.pose=entryPose;sample.poseTime=actor.pose==entryPose?actor.poseTime:0;
            if(ClearContacts(room)) return entryPose;
            throw new InvalidOperationException($"Нет безопасной позы входа лестницы: actor={actor.id}, anatomy={actor.anatomy}, room={room.id}, position={actor.Position}, entry={entryPose}, held={heldPose}.");
        }

        private bool ClearContacts(RoomLayout room)
        {
            try
            {
                if(!Anatomy.TrySupportContacts(sample,out _))
                    throw new InvalidOperationException("Не объявлены контакты видимых ног.");
                // Share the existing solid-landing contact rule with descent.
                // One-way surfaces intentionally remain passable while held.
                return !LadderPoseContact.ShouldRelease(sample,room,sample.Position);
            }
            catch(Exception error) when(error is InvalidOperationException || error is KeyNotFoundException)
            {
                throw new InvalidOperationException($"Некорректные данные входа лестницы: actor={sample.id}, anatomy={sample.anatomy}, room={room.id}, pose={sample.pose}, phase={sample.poseTime:R}. {error.Message}",error);
            }
        }
    }
}
