using System;
using System.IO;
using System.Threading.Tasks;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class SelectedWorldPanel : MonoBehaviour
    {
        private Text description;
        private Button host;
        private int generation;
        public bool Busy {get;private set;}
        public void Initialize(Text text,Button button) {description=text;host=button;}
        public void SetPending(string text)
        {
            // Invalidate an older read before a mutating operation begins.
            // Its completion must not enable Host against an unfinished selection.
            generation++;Busy=true;host.interactable=false;description.text=text;
        }
        public async void Refresh(SaveStore store)
        {
            int request=++generation;Busy=true;host.interactable=false;description.text="Чтение выбранного мира…";
            try
            {
                var result=await Task.Run(()=>
                {
                    string path=Path.Combine(store.DirectoryPath,"world.json");
                    var summary=File.Exists(path)?WorldFileSummary.Read(path):null;
                    return (summary,summary==null?null:new WorldLibrary(store).Name(summary.worldId));
                });
                if(this==null || request!=generation) return;
                host.interactable=true;
                host.GetComponentInChildren<Text>().text=result.Item1==null?"Начать в новом мире":"Продолжить выбранный мир";
                description.text=result.Item1==null?"Сохранённый мир не выбран.\n«Миры и сохранения»: выбор, имя и удаление.":
                    MainMenuPanel.DescribeWorld(result.Item2,result.Item1.branchId??"",result.Item1.revision);
            }
            catch(Exception error)
            {
                if(this==null || request!=generation) return;
                host.interactable=false;
                description.text="Выбранное сохранение повреждено или имеет неподдерживаемый формат.\nОткройте «Миры и сохранения» и выберите другую копию.";
                Debug.LogWarning($"Не удалось прочитать выбранный мир {store.DirectoryPath}: {error}");
            }
            finally {if(this!=null && request==generation) Busy=false;}
        }
    }
}
