using System;
using System.IO;
using Remains.Persistence;

namespace Remains.Networking
{
    public enum SessionStartupMode { ChooseCopies, ContinueSelected, NewWorld }

    // Startup is an explicit host decision. Peer count, arrival order, and later
    // offers cannot reinterpret it or replace its immutable complete payload.
    public sealed class SessionStartupIntent
    {
        public SessionStartupMode Mode {get;}
        public RecoverySnapshot Selected {get;}
        public bool ChoosesCopies=>Mode==SessionStartupMode.ChooseCopies;
        private SessionStartupIntent(SessionStartupMode mode,RecoverySnapshot selected=null) {Mode=mode;Selected=selected;}
        public static SessionStartupIntent ChooseCopies()=>new(SessionStartupMode.ChooseCopies);
        public static SessionStartupIntent NewWorld()=>new(SessionStartupMode.NewWorld);
        public static SessionStartupIntent Continue(RecoverySnapshot selected)
            =>new(SessionStartupMode.ContinueSelected,selected??throw new ArgumentNullException(nameof(selected),"Не указана выбранная полная копия мира."));
        public bool StartsOnJoin(bool localHost)=>!ChoosesCopies && localHost;
        public bool ReservesHostCharacter(bool hasLocalHost,string hostCharacter,string incomingCharacter)
            =>!ChoosesCopies && hasLocalHost && hostCharacter!=null && incomingCharacter==hostCharacter;
        public void RequireLoad(RecoverySnapshot snapshot,bool createNew)
        {
            if(Mode==SessionStartupMode.ContinueSelected)
            {
                if(createNew || snapshot==null || snapshot.Id!=Selected.Id || snapshot.Json!=Selected.Json)
                    throw new InvalidDataException($"Продолжение подменило выбранную полную копию: expected={Selected.Id}, actual={snapshot?.Id}, createNew={createNew}.");
            }
            else if(Mode==SessionStartupMode.NewWorld)
            {
                if(!createNew || snapshot!=null) throw new InvalidDataException("Новый мир нельзя заменить сохранённой копией.");
            }
            else if(createNew!=(snapshot==null)) throw new InvalidDataException("Выбор полной копии не согласуется с созданием нового мира.");
        }
    }
}
