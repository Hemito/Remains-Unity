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
            if(string.IsNullOrEmpty(id)) return Remains.Content.GameLocalization.UiText("source.WorldCopyLabels.db4dbb4b9f6aabbd", "нет данных", "RemainsUnity/Assets/Remains/Presentation/WorldCopyLabels.cs");
            if(labels.TryGetValue(id,out string label)) return label;
            throw new InvalidOperationException($"Подписи копий мира: {kind} {id} отсутствует в прочитанном каталоге.");
        }
        public static string ChoiceText(string name,string branch,string snapshot,long revision,string relation,bool selected)=>
            Remains.Content.GameLocalization.Format("source.WorldCopyLabels.a5713227261c08ec", $"{name}\nВетвь {branch} · снимок {snapshot}\nСостояние №{revision} · {(selected?Remains.Content.GameLocalization.UiText("source.WorldCopyLabels.5b0bbe7cc14b0946", "Выбрана для запуска", "RemainsUnity/Assets/Remains/Presentation/WorldCopyLabels.cs"):relation)}", "RemainsUnity/Assets/Remains/Presentation/WorldCopyLabels.cs");
        public static string SelectedText(string name,string branch,long revision,string snapshot)=>
            Remains.Content.GameLocalization.Format("source.WorldCopyLabels.7f718e4a3a940bb5", $"Выбран: {name}\nВетвь {WorldLobbyView.IdentityLabel(branch,new[]{branch})} · состояние №{revision}\nСнимок {(string.IsNullOrEmpty(snapshot)?Remains.Content.GameLocalization.UiText("source.WorldCopyLabels.db4dbb4b9f6aabbd", "нет данных", "RemainsUnity/Assets/Remains/Presentation/WorldCopyLabels.cs"):snapshot)}", "RemainsUnity/Assets/Remains/Presentation/WorldCopyLabels.cs");
    }
}
