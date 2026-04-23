using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services;

public class AudioCaptureService : IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly SettingsService _settingsService;
    private WaveInEvent? _waveIn;
    private float _maxVolume;
    private DateTime _maxVolumeTime;
    private bool _isRecording;
    private CancellationTokenSource? _cancellationTokenSource;

    public event EventHandler<float>? VolumeRecorded;
    public event EventHandler<DateTime>? RecordingComplete;

    public AudioCaptureService(ILogger<AudioCaptureService> logger, SettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public bool IsRecording => _isRecording;

    public void StartRecording(int deviceIndex, int durationSeconds = 20)
    {
        if (_isRecording)
        {
            _logger.LogWarning("录音已在进行中，跳过请求");
            return;
        }

        try
        {
            if (deviceIndex < 0 || deviceIndex >= WaveInEvent.DeviceCount)
            {
                _logger.LogWarning("无效的麦克风设备索引: {Index}", deviceIndex);
                return;
            }

            var capabilities = WaveInEvent.GetCapabilities(deviceIndex);
            if (!capabilities.SupportsRecording)
            {
                _logger.LogWarning("设备不支持录音: {Device}", capabilities.ProductName);
                return;
            }

            _maxVolume = 0;
            _maxVolumeTime = DateTime.MinValue;
            _cancellationTokenSource = new CancellationTokenSource();

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(16000, 1)
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _isRecording = true;
            _waveIn.StartRecording();

            _logger.LogInformation("开始录音，设备: {Device}, 时长: {Duration}秒", capabilities.ProductName, durationSeconds);

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds), _cancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                }

                if (_isRecording)
                {
                    StopRecording();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动录音失败");
            _isRecording = false;
            _waveIn?.Dispose();
            _waveIn = null;
        }
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        try
        {
            _waveIn?.StopRecording();
            _cancellationTokenSource?.Cancel();
            
            // 确保记录了最大音量时刻
            if (_maxVolumeTime == DateTime.MinValue)
            {
                _maxVolumeTime = DateTime.Now;
            }

            _logger.LogInformation("录音停止，最大音量时刻: {Time}, 音量: {Volume}", 
                _maxVolumeTime, _maxVolume);

            RecordingComplete?.Invoke(this, _maxVolumeTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止录音失败");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        try
        {
            var buffer = new WaveBuffer(e.Buffer);
            float maxSample = 0;

            for (int i = 0; i < e.BytesRecorded / 4; i++)
            {
                var sample = Math.Abs(buffer.FloatBuffer[i]);
                if (sample > maxSample)
                {
                    maxSample = sample;
                }
            }

            _maxVolume = Math.Max(_maxVolume, maxSample);

            if (maxSample > _maxVolume * 0.9 || _maxVolumeTime == DateTime.MinValue)
            {
                _maxVolumeTime = DateTime.Now;
            }

            VolumeRecorded?.Invoke(this, maxSample);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频数据失败");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _isRecording = false;

        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "录音异常停止");
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}