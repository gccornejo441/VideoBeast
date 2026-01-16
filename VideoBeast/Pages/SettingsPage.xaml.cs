using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VideoBeast.Ai;
using VideoBeast.Services;

namespace VideoBeast.Pages;

public sealed partial class SettingsPage : Page
{
    private Stretch _selectedStretch;
    private OllamaSettings _ollamaSettings;
    private OllamaClient? _ollamaClient;
    private List<string> _availableModels = new();
    private bool _isOllamaUrlLocal;
    private DispatcherTimer? _ollamaStatusTimer;
    private bool _isCheckingStatus;
    private CancellationTokenSource? _bootstrapCts;

    public SettingsPage()
    {
        InitializeComponent();

        _selectedStretch = MainWindow.Instance?.GetPlayerSettings().Stretch
                           ?? MainWindow.DefaultPlayerStretch;

        ApplySelectionToUI(_selectedStretch);

        _ollamaSettings = OllamaSettingsStore.Load();
        ApplyOllamaSettingsToUI();
        UpdateStartButtonState();

        _ = LoadOllamaModelsAsync();

        // Wire up page lifecycle events
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize and start status monitoring timer
        _ollamaStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _ollamaStatusTimer.Tick += OllamaStatusTimer_Tick;
        _ollamaStatusTimer.Start();

        // Perform initial status check
        UpdateOllamaStatusDisplay();
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Stop and dispose timer
        if (_ollamaStatusTimer != null)
        {
            _ollamaStatusTimer.Stop();
            _ollamaStatusTimer.Tick -= OllamaStatusTimer_Tick;
            _ollamaStatusTimer = null;
        }
    }

    private async void OllamaStatusTimer_Tick(object? sender, object e)
    {
        // Prevent concurrent status checks
        if (_isCheckingStatus)
        {
            return;
        }

        _isCheckingStatus = true;
        try
        {
            await UpdateOllamaStatusAsync();
        }
        finally
        {
            _isCheckingStatus = false;
        }
    }

    private async Task UpdateOllamaStatusAsync()
    {
        try
        {
            var normalizedUrl = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);

            // Update endpoint text
            OllamaEndpointText.Text = normalizedUrl;

            if (_ollamaClient == null || _ollamaClient.BaseUrl != normalizedUrl)
            {
                _ollamaClient?.Dispose();
                _ollamaClient = new OllamaClient(normalizedUrl);
            }

            // Check if Ollama is available
            var isAvailable = await _ollamaClient.IsAvailableAsync();

            if (isAvailable)
            {
                // Measure latency
                var latency = await _ollamaClient.PingAsync();

                // Update UI - connected state (green)
                OllamaStatusIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80));
                OllamaConnectionStatus.Text = "Running";
                OllamaLatencyText.Text = latency.HasValue ? $"{latency.Value.TotalMilliseconds:F0} ms" : "—";
            }
            else
            {
                // Update UI - disconnected state (red)
                OllamaStatusIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54));
                OllamaConnectionStatus.Text = "Not running";
                OllamaLatencyText.Text = "—";
            }
        }
        catch
        {
            // Update UI - error state (red)
            OllamaStatusIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54));
            OllamaConnectionStatus.Text = "Not running";
            OllamaLatencyText.Text = "—";
            OllamaEndpointText.Text = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);
        }
    }

    private async void UpdateOllamaStatusDisplay()
    {
        if (_isCheckingStatus)
        {
            return;
        }

        _isCheckingStatus = true;
        try
        {
            await UpdateOllamaStatusAsync();
        }
        finally
        {
            _isCheckingStatus = false;
        }
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
        UpdateStartButtonState();
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

    private async void StartOllama_Click(object sender, RoutedEventArgs e)
    {
        // Create cancellation token source for bootstrap
        _bootstrapCts = new CancellationTokenSource();

        // Update button visibility
        StartOllamaButton.Visibility = Visibility.Collapsed;
        CancelOllamaButton.Visibility = Visibility.Visible;
        CancelOllamaButton.IsEnabled = true;

        RefreshModelsButton.IsEnabled = false;
        OllamaStatusText.Text = "Starting Ollama...";

        // Set status indicator to yellow (starting)
        OllamaStatusIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7));
        OllamaConnectionStatus.Text = "Starting…";

        try
        {
            var normalizedUrl = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);

            if (_ollamaClient != null)
            {
                _ollamaClient.Dispose();
                _ollamaClient = null;
            }

            _ollamaClient = new OllamaClient(normalizedUrl);

            var result = await OllamaBootstrapper.TryStartAndWaitAsync(
                normalizedUrl,
                _ollamaClient,
                _bootstrapCts.Token);

            if (result.IsSuccess)
            {
                OllamaStatusText.Text = result.Status == OllamaBootstrapper.BootstrapStatus.AlreadyRunning
                    ? "Ollama is already running."
                    : "Ollama started successfully.";

                await LoadOllamaModelsAsync();
            }
            else
            {
                OllamaStatusText.Text = GetUserFriendlyMessage(result);

                if (result.Status == OllamaBootstrapper.BootstrapStatus.OllamaNotFound)
                {
                    MainWindow.Instance?.ShowStatus(
                        "Ollama not found. Install from https://ollama.ai and ensure it's on your PATH.",
                        InfoBarSeverity.Warning);
                }
            }
        }
        catch (OperationCanceledException)
        {
            OllamaStatusText.Text = "Startup cancelled.";
        }
        catch (Exception ex)
        {
            OllamaStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            // Dispose and null out cancellation token source
            _bootstrapCts?.Dispose();
            _bootstrapCts = null;

            // Restore button visibility
            StartOllamaButton.Visibility = Visibility.Visible;
            CancelOllamaButton.Visibility = Visibility.Collapsed;

            UpdateStartButtonState();
            RefreshModelsButton.IsEnabled = true;

            // Refresh status display
            UpdateOllamaStatusDisplay();
        }
    }

    private void CancelOllama_Click(object sender, RoutedEventArgs e)
    {
        _bootstrapCts?.Cancel();
        CancelOllamaButton.IsEnabled = false; // Disable to prevent double-cancel
    }

    private async void OpenOllamaInBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var normalizedUrl = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);
            await Windows.System.Launcher.LaunchUriAsync(new Uri(normalizedUrl));
        }
        catch (Exception ex)
        {
            OllamaStatusText.Text = $"Failed to open browser: {ex.Message}";
        }
    }

    private void CopyOllamaEndpoint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var normalizedUrl = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(normalizedUrl);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            OllamaStatusText.Text = "Endpoint copied to clipboard";
        }
        catch (Exception ex)
        {
            OllamaStatusText.Text = $"Failed to copy to clipboard: {ex.Message}";
        }
    }

    private string GetUserFriendlyMessage(OllamaBootstrapper.BootstrapResult result)
    {
        return result.Status switch
        {
            OllamaBootstrapper.BootstrapStatus.Success => "Ollama started successfully.",
            OllamaBootstrapper.BootstrapStatus.AlreadyRunning => "Ollama is already running.",
            OllamaBootstrapper.BootstrapStatus.NotLocalUrl => "Cannot auto-start Ollama for remote URLs. Ensure Ollama is running at the configured address.",
            OllamaBootstrapper.BootstrapStatus.OllamaNotFound => "Ollama not found. Install from https://ollama.ai and ensure it's on your PATH.",
            OllamaBootstrapper.BootstrapStatus.ProcessStartFailed => $"Failed to start Ollama: {result.Message}",
            OllamaBootstrapper.BootstrapStatus.Timeout => "Ollama is taking longer than expected to start. It may still be initializing.",
            OllamaBootstrapper.BootstrapStatus.Cancelled => "Startup cancelled.",
            _ => "Unknown error occurred."
        };
    }

    private void UpdateStartButtonState()
    {
        var normalizedUrl = OllamaClient.NormalizeBaseUrl(_ollamaSettings.BaseUrl);
        _isOllamaUrlLocal = OllamaBootstrapper.IsLocalUrl(normalizedUrl);
        StartOllamaButton.IsEnabled = _isOllamaUrlLocal;
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
