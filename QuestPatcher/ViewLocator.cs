using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using QuestPatcher.ViewModels;

namespace QuestPatcher
{
    public class ViewLocator : IDataTemplate
    {
        public bool SupportsRecycling => false;

        public Control? Build(object? data)
        {
            if (data == null)
            {
                return null;
            }

            string name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control) Activator.CreateInstance(type)!;
            }
            else
            {
                return new TextBlock { Text = "Not Found: " + name };
            }
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
