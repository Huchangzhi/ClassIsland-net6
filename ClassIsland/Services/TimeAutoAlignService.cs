using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using ClassIsland.Shared.Abstraction.Services;

namespace ClassIsland.Services;

public class TimeAutoAlignService : ObservableRecipient, IDisposable
{
    private readonly ILogger<TimeAutoAlignService> _logger;
    private readonly SettingsService _settingsService;
    private readonly IExactTimeService _exactTimeService;
    private readonly AudioCaptureService _audioCaptureService;
    private bool _isListening;
    private DateTime _lastClassEndTime;
    private readonly List<double> _todayRecordedOffsets = new();
    private readonly Dictionary<string, List<double>> _recordedOffsetsCache = new();

    public TimeAutoAlignService(
        ILogger<TimeAutoAlignService> logger,
        SettingsService settingsService,
        IExactTimeService exactTimeService,
        AudioCaptureService audioCaptureService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _exactTimeService = exactTimeService;
        _audioCaptureService = audioCaptureService;

        _settingsService.Settings.PropertyChanged += OnSettingsPropertyChanged;
        LoadTodayRecordedOffsets();
        
        // 如果启动时功能已启用，则开始监听
        if (_settingsService.Settings.IsTimeAutoAlignEnabled)
        {
            StartListening();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsService.Settings.IsTimeAutoAlignEnabled))
        {
            if (_settingsService.Settings.IsTimeAutoAlignEnabled && !_isListening)
            {
                StartListening();
            }
            else if (!_settingsService.Settings.IsTimeAutoAlignEnabled && _isListening)
            {
                StopListening();
            }
        }
    }

    public void StartListening()
    {
        if (_isListening) return;

        _isListening = true;
        _logger.LogInformation("自动时间对齐服务已启动");
    }

    public void StopListening()
    {
        _isListening = false;
        _logger.LogInformation("自动时间对齐服务已停止");
    }

    public void CheckAndStartRecording()
    {
        if (!_isListening || !_settingsService.Settings.IsTimeAutoAlignEnabled)
        {
            return;
        }

        if (_audioCaptureService.IsRecording)
        {
            return;
        }

        var currentTime = _exactTimeService.GetCurrentLocalDateTime();
        var lessonsService = App.GetService<ILessonsService>();
        if (lessonsService?.CurrentTimeLayoutItem == null)
        {
            return;
        }

        var currentItem = lessonsService.CurrentTimeLayoutItem;
        if (currentItem.TimeType != 0) return;

        var classEndTime = currentItem.EndSecond;
        var timeRemaining = classEndTime.TimeOfDay - currentTime.TimeOfDay;

        if (timeRemaining.TotalSeconds <= 10 && timeRemaining.TotalSeconds > 0)
        {
            _logger.LogInformation("检测到下课前{Seconds}秒，准备开始录音", timeRemaining.TotalSeconds);
            _lastClassEndTime = currentItem.EndSecond;

            var deviceIndex = _settingsService.Settings.SelectedMicrophoneDeviceIndex;
            _audioCaptureService.StartRecording(deviceIndex, 20);
        }
    }

    public void OnRecordingComplete(DateTime maxVolumeTime)
    {
        if (!_isListening) return;

        var expectedEndTime = _lastClassEndTime;
        if (expectedEndTime == DateTime.MinValue) return;

        var currentTime = _exactTimeService.GetCurrentLocalDateTime();
        var detectedOffset = (currentTime - maxVolumeTime).TotalSeconds;

        _logger.LogInformation("检测到铃声偏移: {Offset}秒", detectedOffset);

        ProcessRecordedOffset(detectedOffset);
    }

    private void ProcessRecordedOffset(double offset)
    {
        var today = DateTime.Now.Date.ToString("yyyy-MM-dd");

        if (!_recordedOffsetsCache.ContainsKey(today))
        {
            _recordedOffsetsCache.Clear();
            _recordedOffsetsCache[today] = new List<double>();
        }

        var todayOffsets = _recordedOffsetsCache[today];

        bool isDuplicate = false;
        foreach (var existingOffset in todayOffsets)
        {
            if (Math.Abs(existingOffset - offset) <= 0.5)
            {
                isDuplicate = true;
                break;
            }
        }

        if (!isDuplicate)
        {
            todayOffsets.Add(offset);
            _settingsService.Settings.TimeAutoAlignRecordedOffsets = string.Join(",", todayOffsets);

            _logger.LogInformation("今日已记录偏移: {Offsets}", string.Join(", ", todayOffsets));

            if (todayOffsets.Count >= 3)
            {
                ApplyOffset(0, todayOffsets);
                todayOffsets.Clear();
                _settingsService.Settings.TimeAutoAlignRecordedOffsets = "";
            }
        }
    }

    private void ApplyOffset(double offset, List<double> offsets)
    {
        var average = offsets.Average();

        _settingsService.Settings.TimeOffsetSeconds += average;
        _logger.LogInformation("已应用自动时间对齐偏移: {Offset}秒", average);
    }

    private void LoadTodayRecordedOffsets()
    {
        var recorded = _settingsService.Settings.TimeAutoAlignRecordedOffsets;
        if (string.IsNullOrEmpty(recorded)) return;

        var parts = recorded.Split(',');
        foreach (var part in parts)
        {
            if (double.TryParse(part, out var offset))
            {
                _todayRecordedOffsets.Add(offset);
            }
        }
    }

    public void Dispose()
    {
        _settingsService.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        StopListening();
    }
}