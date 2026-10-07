using System;
using System.Collections.Generic;
using System.Linq;

namespace Remains.Presentation
{
    // One event-scoped label set includes every catalog page and the selected copy.
    // The existing manual-lobby formatter remains the sole abbreviation rule.
    public sealed class WorldCopyLabels
    {
        private readonly Dictionary<string,string> branches;
        private readonly Dictionary<string,string> snapshots;
        public WorldCopyLabels(IEnumerable<string> branchIds,IEnumerable<string> snapshotIds)
        {
            branches=Build(branchIds);snapshots=Build(snapshotIds);
        }
        private static Dictionary<string,string> Build(IEnumerable<string> ids)
        {
            if(ids==null) throw new ArgumentNullException(nameof(ids));
            var all=ids.Where(id=>!string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            var labels=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string id in all) labels.Add(id,WorldLobbyView.IdentityLabel(id,all));
            return labels;
        }
        public string Branch(string id)=>Read(branches,id,"ветвь");
        public string Snapshot(string id)=>Read(snapshots,id,"снимок");
        private static string Read(Dictionary<string,string> labels,string id,string kind)
        {
            if(string.IsNullOrEmpty(id)) return "нет данных";
            if(labels.TryGetValue(id,out string label)) return label;
            throw new InvalidOperationException($"Подписи копий мира: {kind} {id} отсутствует в прочитанном каталоге.");
        }
        public static string ChoiceText(string name,string branch,string snapshot,long revision,string relation,bool selected)=>
            $"{name}\nВетвь {branch} · снимок {snapshot}\nСостояние №{revision} · {(selected?"Выбрана для запуска":relation)}";
        public static string SelectedText(string name,string branch,long revision,string snapshot)=>
            $"Выбран: {name}\nВетвь {WorldLobbyView.IdentityLabel(branch,new[]{branch})} · состояние №{revision}\nСнимок {(string.IsNullOrEmpty(snapshot)?"нет данных":snapshot)}";
    }
}
