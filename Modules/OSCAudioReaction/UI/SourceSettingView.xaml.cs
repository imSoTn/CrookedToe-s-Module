using System.Linq;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows;
using VRCOSC.App.SDK.Modules;
using System.Windows.Input;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using CrookedToe.Modules.OSCAudioReaction.AudioProcessing;
using CrookedToe.Modules.OSCAudioReaction;
using System.Diagnostics;
using System.Drawing.Text;

namespace CrookedToe.Modules.OSCAudioReaction.UI
{
    public partial class SourceSettingView : UserControl
    {
        private List<string> availableAudioSources;
        private StringModuleSetting? _setting;
        public SourceSettingView(Module module, ModuleSetting setting)
        {
            InitializeComponent(); // Долбаеб, получай говно до загрузки модуля НЕ ТУТ!
            
            if (setting is StringModuleSetting stringSetting)
            {
                _setting = stringSetting;
                DataContext = stringSetting;
            }
            else
            {
                throw new InvalidOperationException("SourceSettingView requires a StringModuleSetting.");
            }

            if( module is OSCAudioDirectionModule oscarmodule){
                availableAudioSources = oscarmodule.getAudioSources();
            }

            if (availableAudioSources.Any())
            {
                SuggestionList.ItemsSource = availableAudioSources;
            }
        }
        private void SuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e){
            if (SuggestionList.SelectedItem is string availableAudioSources){

            }
        }
    }
}