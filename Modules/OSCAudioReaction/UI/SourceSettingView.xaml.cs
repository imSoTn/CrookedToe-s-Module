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

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string input = InputBox.Text.ToLower();

            // Filter displays based on user input
            var filteredDisplays = availableAudioSources
                .Where(display => display.ToLower().Contains(input))
                .ToList();

            // Show or hide suggestions
            if (filteredDisplays.Any())
            {
                SuggestionList.ItemsSource = availableAudioSources;
                SuggestionList.Visibility = Visibility.Visible;
            }
            else
            {
                SuggestionList.Visibility = Visibility.Collapsed;
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                // Focus on the list and select the first item
                SuggestionList.Focus();
                SuggestionList.SelectedIndex = 0;
            }
        }

        private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SuggestionList.SelectedItem is string selectedAudioSource)
            {
                // Update the TextBox and hide the suggestions
                InputBox.Text = selectedAudioSource;
                SuggestionList.Visibility = Visibility.Collapsed;
                InputBox.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                // Hide suggestions on Escape key
                SuggestionList.Visibility = Visibility.Collapsed;
                InputBox.Focus();
            }
        }

        private void SuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is string selectedAudioSource)
            {
                // Update the TextBox and hide the suggestions
                InputBox.Text = selectedAudioSource;
                SuggestionList.Visibility = Visibility.Collapsed;
            }
        }
    }
}