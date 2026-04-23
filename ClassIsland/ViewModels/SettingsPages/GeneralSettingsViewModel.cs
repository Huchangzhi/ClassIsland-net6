using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;

namespace ClassIsland.ViewModels.SettingsPages;

public class GeneralSettingsViewModel : ObservableRecipient
{
    private bool _isWeekOffsetSettingsOpen = false;
    private ObservableCollection<string> _microphoneDevices = new();

    public bool IsWeekOffsetSettingsOpen
    {
        get => _isWeekOffsetSettingsOpen;
        set
        {
            if (value == _isWeekOffsetSettingsOpen) return;
            _isWeekOffsetSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> MicrophoneDevices
    {
        get => _microphoneDevices;
        set
        {
            if (Equals(value, _microphoneDevices)) return;
            _microphoneDevices = value;
            OnPropertyChanged();
        }
    }

    public void RefreshMicrophoneDevices()
    {
        MicrophoneDevices.Clear();
        try
        {
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                if (capabilities.SupportsRecording)
                {
                    MicrophoneDevices.Add(capabilities.ProductName);
                }
            }
        }
        catch
        {
        }
        if (MicrophoneDevices.Count == 0)
        {
            MicrophoneDevices.Add("无可用麦克风");
        }
    }
}