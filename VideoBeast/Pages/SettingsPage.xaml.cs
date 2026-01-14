using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VideoBeast.Ai;

namespace VideoBeast.Pages;

public sealed partial class SettingsPage : Page
{
    private Stretch _selectedStretch;
    private OllamaSettings _ollamaSettings;
    private OllamaClient? _ollamaClient;
    private List<string> _availableModels = new();

    public SettingsPage()
    {
        InitializeComponent();

        _selectedStretch = MainWindow.Instance?.GetPlayerSettings().Stretch
                           ?? MainWindow.DefaultPlayerStretch;

        ApplySelectionToUI(_selectedStretch);

        _ollamaSettings = OllamaSettingsStore.Load();
        ApplyOllamaSettingsToUI();

        _ = LoadOllamaModelsAsync();
    }

    private void ApplySelectionToUI(Stretch stretch)
    {
        RbFit.IsChecked = stretch == Stretch.Uniform;
        RbFill.IsChecked = stretch == Stretch.UniformToFill;
        RbStretch.IsChecked = stretch == Stretch.Fill;
    }

    private void ApplyOllamaSettingsToUI()
    {
        OllamaEnabledToggle.IsOn = _ollamaSettings.Enabled;
        OllamaBaseUrlBox.Text = _ollamaSettings.BaseUrl;
    }

    private async Task LoadOllamaModelsAsync()
    {
        try
        {
            OllamaStatusText.Text = "Loading models...";
            RefreshModelsButton.IsEnabled = false;

            _ollamaClient?.Dispose();
            _ollamaClient = new OllamaClient(_ollamaSettings.BaseUrl);

            _availableModels = await _ollamaClient.GetModelsAsync();

            OllamaModelCombo.Items.Clear();
            foreach (var model in _availableModels)
            {
                OllamaModelCombo.Items.Add(model);
            }

            if (_availableModels.Count == 0)
            {
                OllamaStatusText.Text = "No models found. Install models with 'ollama pull <model-name>'";
            }
            else
            {
                OllamaStatusText.Text = $"Found {_availableModels.Count} model(s)";

                if (!string.IsNullOrEmpty(_ollamaSettings.DefaultModel))
                {
                    var index = _availableModels.IndexOf(_ollamaSettings.DefaultModel);
                    if (index >= 0)
                    {
                        OllamaModelCombo.SelectedIndex = index;
                    }
                }
                else if (_availableModels.Count > 0)
                {
                    OllamaModelCombo.SelectedIndex = 0;
                    _ollamaSettings.DefaultModel = _availableModels[0];
                }
            }
        }
        catch (Exception ex)
        {
            OllamaStatusText.Text = $"Failed to connect: {ex.Message}. Ensure Ollama is running.";
            OllamaModelCombo.Items.Clear();
        }
        finally
        {
            RefreshModelsButton.IsEnabled = true;
        }
    }

    private void Scaling_Checked(object sender,RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, RbFit)) _selectedStretch = Stretch.Uniform;
        else if (ReferenceEquals(sender, RbFill)) _selectedStretch = Stretch.UniformToFill;
        else if (ReferenceEquals(sender, RbStretch)) _selectedStretch = Stretch.Fill;
    }

    private void OllamaEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        _ollamaSettings.Enabled = OllamaEnabledToggle.IsOn;
    }

    private void OllamaBaseUrl_Changed(object sender, TextChangedEventArgs e)
    {
        _ollamaSettings.BaseUrl = OllamaBaseUrlBox.Text?.Trim() ?? "http://localhost:11434";
    }

    private void OllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OllamaModelCombo.SelectedItem is string model)
        {
            _ollamaSettings.DefaultModel = model;
        }
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        await LoadOllamaModelsAsync();
    }

    private void Reset_Click(object sender,RoutedEventArgs e)
    {
        _selectedStretch = MainWindow.DefaultPlayerStretch;
        ApplySelectionToUI(_selectedStretch);

        _ollamaSettings = new OllamaSettings();
        ApplyOllamaSettingsToUI();
        OllamaModelCombo.SelectedIndex = -1;
    }

    private void Save_Click(object sender,RoutedEventArgs e)
    {
        MainWindow.Instance?.SavePlayerStretch(_selectedStretch);

        OllamaSettingsStore.Save(_ollamaSettings);

        MainWindow.Instance?.RefreshSearchCoordinatorAiSettings();

        if (Frame?.CanGoBack == true)
            Frame.GoBack();
    }
}
