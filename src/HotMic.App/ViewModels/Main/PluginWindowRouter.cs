using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using HotMic.App.Models;
using HotMic.App.UI.PluginComponents;
using HotMic.App.Views;
using HotMic.Common.Configuration;
using HotMic.Common.Models;
using HotMic.Core.Plugins;
using HotMic.Core.Plugins.BuiltIn;
using HotMic.Vst3;

namespace HotMic.App.ViewModels;

internal sealed class PluginWindowRouter
{
    public PluginChoice? ShowPluginBrowser(AppConfig config)
    {
        var choices = new List<PluginChoice>
        {
            new() { Id = "ui:container", Name = "Plugin Container", IsVst3 = false, Category = PluginCategory.Utility, Description = "Create a visual container to group plugins on the main strip" },
            new() { Id = "builtin:input", Name = "Input Source", IsVst3 = false, Category = PluginCategory.Routing, Description = "Read a microphone input into this channel" },
            new() { Id = "builtin:copy", Name = "Copy to Channel", IsVst3 = false, Category = PluginCategory.Routing, Description = "Duplicate audio + sidechain into a new channel" },
            new() { Id = "builtin:merge", Name = "Merge", IsVst3 = false, Category = PluginCategory.Routing, Description = "Merge 2-N channels into the current chain with alignment" },
            new() { Id = "builtin:output-send", Name = "Output Send", IsVst3 = false, Category = PluginCategory.Routing, Description = "Send this chain to the main output (left/right/both)" },
            // Dynamics
            new() { Id = "builtin:gain", Name = "Gain", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Simple gain and phase control" },
            new() { Id = "builtin:compressor", Name = "Compressor", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Dynamic range compression with soft knee" },
            new() { Id = "builtin:noisegate", Name = "Noise Gate", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Removes audio below threshold" },
            new() { Id = "builtin:deesser", Name = "De-Esser", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Tames sibilance in the high band" },
            new() { Id = "builtin:limiter", Name = "Limiter", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Brick-wall peak control" },
            new() { Id = "builtin:upward-expander", Name = "Upward Expander", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Restores micro-dynamics across bands" },
            new() { Id = "builtin:consonant-transient", Name = "Consonant Transient", IsVst3 = false, Category = PluginCategory.Dynamics, Description = "Emphasizes consonant transients without harshness" },

            // EQ
            new() { Id = "builtin:hpf", Name = "High-Pass Filter", IsVst3 = false, Category = PluginCategory.Eq, Description = "Fast rumble and plosive removal" },
            new() { Id = "builtin:eq3", Name = "5-Band EQ", IsVst3 = false, Category = PluginCategory.Eq, Description = "HPF + shelves + dual mid bands" },
            new() { Id = "builtin:dynamic-eq", Name = "Dynamic EQ", IsVst3 = false, Category = PluginCategory.Eq, Description = "Voiced/unvoiced keyed tonal movement" },

            // Noise Reduction
            new() { Id = "builtin:fft-noise", Name = "FFT Noise Removal", IsVst3 = false, Category = PluginCategory.NoiseReduction, Description = "Learns and removes background noise" },

            // Analysis
            new() { Id = "builtin:freq-analyzer", Name = "Frequency Analyzer", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Real-time spectrum view with tunable bins" },
            new() { Id = "builtin:vocal-spectrograph", Name = "Vocal Spectrograph", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Vocal-focused spectrogram with overlays" },
            new() { Id = "builtin:signal-generator", Name = "Signal Generator", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Test tones, noise, and sample playback" },
            new() { Id = "builtin:sidechain-tap", Name = "Sidechain Tap", IsVst3 = false, Category = PluginCategory.Analysis, Description = "Sidechain signal source for downstream plugins" },

            // AI/ML
            new() { Id = "builtin:rnnoise", Name = "RNNoise", IsVst3 = false, Category = PluginCategory.AiMl, Description = "Neural network noise suppression" },
            new() { Id = "builtin:speechdenoiser", Name = "Speech Denoiser", IsVst3 = false, Category = PluginCategory.AiMl, Description = "SpeechDenoiser streaming model (DFN3)" },
            new() { Id = "builtin:voice-gate", Name = "Voice Gate", IsVst3 = false, Category = PluginCategory.AiMl, Description = "AI-powered voice activity detection" },

            // Effects
            new() { Id = "builtin:saturation", Name = "Saturation", IsVst3 = false, Category = PluginCategory.Effects, Description = "Soft clipping harmonic warmth" },
            new() { Id = "builtin:reverb", Name = "Reverb", IsVst3 = false, Category = PluginCategory.Effects, Description = "Convolution reverb with IR presets" },
            new() { Id = "builtin:spectral-contrast", Name = "Spectral Contrast", IsVst3 = false, Category = PluginCategory.Effects, Description = "Enhances spectral detail via lateral inhibition" },
            new() { Id = "builtin:air-exciter", Name = "Air Exciter", IsVst3 = false, Category = PluginCategory.Effects, Description = "Keyed high-frequency excitation" },
            new() { Id = "builtin:bass-enhancer", Name = "Bass Enhancer", IsVst3 = false, Category = PluginCategory.Effects, Description = "Psychoacoustic low-end harmonics" },
            new() { Id = "builtin:room-tone", Name = "Room Tone", IsVst3 = false, Category = PluginCategory.Effects, Description = "Controlled ambience bed with ducking" },
            new() { Id = "builtin:formant-enhance", Name = "Formant Enhancer", IsVst3 = false, Category = PluginCategory.Effects, Description = "Formant-aware enhancement for vowels" }
        };

        if (config.EnableVstPlugins)
        {
            var scanner = new Vst3Scanner();
            foreach (var vst in scanner.Scan(config.Vst2SearchPaths, config.Vst3SearchPaths))
            {
                string prefix = vst.Format == VstPluginFormat.Vst2 ? "vst2:" : "vst3:";
                string label = vst.Format == VstPluginFormat.Vst2 ? "VST2" : "VST3";
                choices.Add(new PluginChoice
                {
                    Id = $"{prefix}{vst.Path}",
                    Name = $"{vst.Name} ({label})",
                    Path = vst.Path,
                    IsVst3 = true,
                    Format = vst.Format,
                    Category = PluginCategory.Vst,
                    Description = $"External {label} plugin"
                });
            }
        }

        var viewModel = new PluginBrowserViewModel(choices);
        var window = new PluginBrowserWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true ? viewModel.SelectedChoice : null;
    }

    public void ShowPluginParameters(PluginWindowRequest request)
    {
        var plugin = request.Plugin;
        int channelIndex = request.ChannelIndex;
        int pluginInstanceId = request.PluginInstanceId;

        if (plugin is BusInputPlugin or CopyToChannelPlugin or MergePlugin or OutputSendPlugin)
        {
            return;
        }

        if (plugin is InputPlugin inputPlugin)
        {
            ShowInputSourceWindow(channelIndex, pluginInstanceId, inputPlugin, request);
            return;
        }

        if (plugin is NoiseGatePlugin noiseGate)
        {
            ShowNoiseGateWindow(channelIndex, pluginInstanceId, noiseGate, request);
            return;
        }

        if (plugin is CompressorPlugin compressor)
        {
            ShowCompressorWindow(channelIndex, pluginInstanceId, compressor, request);
            return;
        }

        if (plugin is SignalGeneratorPlugin signalGen)
        {
            ShowSignalGeneratorWindow(channelIndex, pluginInstanceId, signalGen, request);
            return;
        }

        if (plugin is GainPlugin gain)
        {
            ShowGainWindow(channelIndex, pluginInstanceId, gain, request);
            return;
        }

        if (plugin is FiveBandEqPlugin eq)
        {
            ShowEqWindow(channelIndex, pluginInstanceId, eq, request);
            return;
        }

        if (plugin is FFTNoiseRemovalPlugin fftNoise)
        {
            ShowFFTNoiseWindow(channelIndex, pluginInstanceId, fftNoise, request);
            return;
        }

        if (plugin is FrequencyAnalyzerPlugin analyzer)
        {
            ShowFrequencyAnalyzerWindow(channelIndex, pluginInstanceId, analyzer, request);
            return;
        }

        if (plugin is VocalSpectrographPlugin spectrograph)
        {
            ShowVocalSpectrographWindow(channelIndex, pluginInstanceId, spectrograph, request);
            return;
        }

        if (plugin is SileroVoiceGatePlugin silero)
        {
            ShowVoiceGateWindow(channelIndex, pluginInstanceId, silero, request);
            return;
        }

        if (plugin is RNNoisePlugin rnnoise)
        {
            ShowRNNoiseWindow(channelIndex, pluginInstanceId, rnnoise, request);
            return;
        }

        if (plugin is SpeechDenoiserPlugin speechDenoiser)
        {
            ShowSpeechDenoiserWindow(channelIndex, pluginInstanceId, speechDenoiser, request);
            return;
        }

        if (plugin is ConvolutionReverbPlugin reverb)
        {
            ShowReverbWindow(channelIndex, pluginInstanceId, reverb, request);
            return;
        }

        if (plugin is LimiterPlugin limiter)
        {
            ShowLimiterWindow(channelIndex, pluginInstanceId, limiter, request);
            return;
        }

        if (plugin is DeEsserPlugin deesser)
        {
            ShowDeEsserWindow(channelIndex, pluginInstanceId, deesser, request);
            return;
        }

        if (plugin is HighPassFilterPlugin hpf)
        {
            ShowHighPassFilterWindow(channelIndex, pluginInstanceId, hpf, request);
            return;
        }

        if (plugin is SaturationPlugin saturation)
        {
            ShowSaturationWindow(channelIndex, pluginInstanceId, saturation, request);
            return;
        }

        if (plugin is AirExciterPlugin airExciter)
        {
            ShowAirExciterWindow(channelIndex, pluginInstanceId, airExciter, request);
            return;
        }

        if (plugin is BassEnhancerPlugin bassEnhancer)
        {
            ShowBassEnhancerWindow(channelIndex, pluginInstanceId, bassEnhancer, request);
            return;
        }

        if (plugin is ConsonantTransientPlugin consonantTransient)
        {
            ShowConsonantTransientWindow(channelIndex, pluginInstanceId, consonantTransient, request);
            return;
        }

        if (plugin is DynamicEqPlugin dynamicEq)
        {
            ShowDynamicEqWindow(channelIndex, pluginInstanceId, dynamicEq, request);
            return;
        }

        if (plugin is FormantEnhancerPlugin formantEnhancer)
        {
            ShowFormantEnhancerWindow(channelIndex, pluginInstanceId, formantEnhancer, request);
            return;
        }

        if (plugin is RoomTonePlugin roomTone)
        {
            ShowRoomToneWindow(channelIndex, pluginInstanceId, roomTone, request);
            return;
        }

        if (plugin is SidechainTapPlugin sidechainTap)
        {
            ShowSidechainTapWindow(channelIndex, pluginInstanceId, sidechainTap, request);
            return;
        }

        if (plugin is SpectralContrastPlugin spectralContrast)
        {
            ShowSpectralContrastWindow(channelIndex, pluginInstanceId, spectralContrast, request);
            return;
        }

        if (plugin is UpwardExpanderPlugin upwardExpander)
        {
            ShowUpwardExpanderWindow(channelIndex, pluginInstanceId, upwardExpander, request);
            return;
        }

        var parameterViewModels = plugin.Parameters.Select(parameter =>
        {
            float currentValue = request.GetPluginParameterValue(channelIndex, pluginInstanceId, parameter.Name, parameter.DefaultValue);
            return new PluginParameterViewModel(
                parameter.Index,
                parameter.Name,
                parameter.MinValue,
                parameter.MaxValue,
                currentValue,
                parameter.Unit,
                value => request.ApplyPluginParameter(channelIndex, pluginInstanceId, parameter.Index, parameter.Name, value),
                parameter.FormatValue);
        }).ToList();

        Action? learnNoiseAction = plugin is FFTNoiseRemovalPlugin
            ? () => request.RequestNoiseLearn(channelIndex, pluginInstanceId)
            : null;
        Func<float>? vadProvider = plugin switch
        {
            RNNoisePlugin rnn => () => rnn.VadProbability,
            SileroVoiceGatePlugin vad => () => vad.VadProbability,
            _ => null
        };
        string statusMessage = plugin is IPluginStatusProvider statusProvider ? statusProvider.StatusMessage : string.Empty;

        float latencyMs = request.SampleRate > 0
            ? plugin.LatencySamples * 1000f / request.SampleRate
            : 0f;
        var viewModel = new PluginParametersViewModel(plugin.Name, parameterViewModels, null, null, learnNoiseAction, latencyMs, statusMessage, vadProvider);
        var window = new PluginParametersWindow(viewModel)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void ShowOutputSendWindow(int channelIndex, int pluginInstanceId, OutputSendPlugin plugin, PluginWindowRequest request)
    {
        if ((uint)channelIndex >= (uint)request.Channels.Count)
        {
            return;
        }

        var channel = request.Channels[channelIndex];
        string outputDeviceName = request.SelectedOutputDevice?.Name ?? "No Output";

        var window = new OutputSendWindow(plugin,
            outputDeviceName,
            () => channel.OutputPeakLevel,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    public void ShowVst3Editor(Vst3PluginWrapper plugin)
    {
        var window = new Vst3EditorWindow(plugin)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            Title = plugin.Name
        };
        window.Show();
    }

    private void ShowNoiseGateWindow(int channelIndex, int pluginInstanceId, NoiseGatePlugin plugin, PluginWindowRequest request)
    {
        var window = new NoiseGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowCompressorWindow(int channelIndex, int pluginInstanceId, CompressorPlugin plugin, PluginWindowRequest request)
    {
        var window = new CompressorWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSignalGeneratorWindow(int channelIndex, int pluginInstanceId, SignalGeneratorPlugin plugin, PluginWindowRequest request)
    {
        var window = new SignalGeneratorWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters.FirstOrDefault(p => p.Index == paramIndex)?.Name ?? string.Empty;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowGainWindow(int channelIndex, int pluginInstanceId, GainPlugin plugin, PluginWindowRequest request)
    {
        var window = new GainWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowInputSourceWindow(int channelIndex, int pluginInstanceId, InputPlugin plugin, PluginWindowRequest request)
    {
        if ((uint)channelIndex >= (uint)request.Channels.Count)
        {
            return;
        }

        var channel = request.Channels[channelIndex];
        var devices = request.InputDevices
            .Select(d => new InputSourceDevice(d.Id, d.Name))
            .ToList();

        var window = new InputSourceWindow(
            devices,
            () => channel.InputDeviceId,
            () => channel.InputChannelMode,
            () => channel.InputGainDb,
            () => channel.InputPeakLevel,
            () => plugin.IsBypassed,
            deviceId => request.SetChannelInputDevice(channelIndex, deviceId),
            mode => request.SetChannelInputMode(channelIndex, mode),
            gainDb => request.SetChannelInputGain(channelIndex, gainDb),
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowEqWindow(int channelIndex, int pluginInstanceId, FiveBandEqPlugin plugin, PluginWindowRequest request)
    {
        var window = new EqWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFFTNoiseWindow(int channelIndex, int pluginInstanceId, FFTNoiseRemovalPlugin plugin, PluginWindowRequest request)
    {
        var window = new FFTNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed),
            () => request.RequestNoiseLearn(channelIndex, pluginInstanceId))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFrequencyAnalyzerWindow(int channelIndex, int pluginInstanceId, FrequencyAnalyzerPlugin plugin, PluginWindowRequest request)
    {
        var window = new FrequencyAnalyzerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowVocalSpectrographWindow(int channelIndex, int pluginInstanceId, VocalSpectrographPlugin plugin, PluginWindowRequest request)
    {
        var window = new VocalSpectrographWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowVoiceGateWindow(int channelIndex, int pluginInstanceId, SileroVoiceGatePlugin plugin, PluginWindowRequest request)
    {
        var window = new VoiceGateWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowRNNoiseWindow(int channelIndex, int pluginInstanceId, RNNoisePlugin plugin, PluginWindowRequest request)
    {
        var window = new RNNoiseWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSpeechDenoiserWindow(int channelIndex, int pluginInstanceId, SpeechDenoiserPlugin plugin, PluginWindowRequest request)
    {
        var window = new SpeechDenoiserWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowReverbWindow(int channelIndex, int pluginInstanceId, ConvolutionReverbPlugin plugin, PluginWindowRequest request)
    {
        var window = new ReverbWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowLimiterWindow(int channelIndex, int pluginInstanceId, LimiterPlugin plugin, PluginWindowRequest request)
    {
        var window = new LimiterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowDeEsserWindow(int channelIndex, int pluginInstanceId, DeEsserPlugin plugin, PluginWindowRequest request)
    {
        var window = new DeEsserWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowHighPassFilterWindow(int channelIndex, int pluginInstanceId, HighPassFilterPlugin plugin, PluginWindowRequest request)
    {
        var window = new HighPassFilterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSaturationWindow(int channelIndex, int pluginInstanceId, SaturationPlugin plugin, PluginWindowRequest request)
    {
        var window = new SaturationWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowAirExciterWindow(int channelIndex, int pluginInstanceId, AirExciterPlugin plugin, PluginWindowRequest request)
    {
        var window = new AirExciterWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowBassEnhancerWindow(int channelIndex, int pluginInstanceId, BassEnhancerPlugin plugin, PluginWindowRequest request)
    {
        var window = new BassEnhancerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowConsonantTransientWindow(int channelIndex, int pluginInstanceId, ConsonantTransientPlugin plugin, PluginWindowRequest request)
    {
        var window = new ConsonantTransientWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowDynamicEqWindow(int channelIndex, int pluginInstanceId, DynamicEqPlugin plugin, PluginWindowRequest request)
    {
        var window = new DynamicEqWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowFormantEnhancerWindow(int channelIndex, int pluginInstanceId, FormantEnhancerPlugin plugin, PluginWindowRequest request)
    {
        var window = new FormantEnhancerWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowRoomToneWindow(int channelIndex, int pluginInstanceId, RoomTonePlugin plugin, PluginWindowRequest request)
    {
        var window = new RoomToneWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSidechainTapWindow(int channelIndex, int pluginInstanceId, SidechainTapPlugin plugin, PluginWindowRequest request)
    {
        var window = new SidechainTapWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed),
            () => request.GetSidechainUsageMask(channelIndex, pluginInstanceId))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowSpectralContrastWindow(int channelIndex, int pluginInstanceId, SpectralContrastPlugin plugin, PluginWindowRequest request)
    {
        var window = new SpectralContrastWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }

    private void ShowUpwardExpanderWindow(int channelIndex, int pluginInstanceId, UpwardExpanderPlugin plugin, PluginWindowRequest request)
    {
        var window = new UpwardExpanderWindow(plugin,
            (paramIndex, value) =>
            {
                string paramName = plugin.Parameters[paramIndex].Name;
                request.ApplyPluginParameter(channelIndex, pluginInstanceId, paramIndex, paramName, value);
            },
            bypassed => request.SetPluginBypass(channelIndex, pluginInstanceId, bypassed))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        window.Show();
    }
}

internal sealed record PluginWindowRequest
{
    public int ChannelIndex { get; init; }
    public int PluginInstanceId { get; init; }
    public IPlugin Plugin { get; init; } = null!;
    public IReadOnlyList<AudioDevice> InputDevices { get; init; } = Array.Empty<AudioDevice>();
    public IReadOnlyList<ChannelStripViewModel> Channels { get; init; } = Array.Empty<ChannelStripViewModel>();
    public AudioDevice? SelectedOutputDevice { get; init; }
    public int SampleRate { get; init; }
    public Func<int, int, string, float, float> GetPluginParameterValue { get; init; } = (_, _, _, fallback) => fallback;
    public Action<int, int, int, string, float> ApplyPluginParameter { get; init; } = (_, _, _, _, _) => { };
    public Action<int, int, bool> SetPluginBypass { get; init; } = (_, _, _) => { };
    public Func<int, int, SidechainSignalMask> GetSidechainUsageMask { get; init; } = (_, _) => SidechainSignalMask.None;
    public Action<int, int> RequestNoiseLearn { get; init; } = (_, _) => { };
    public Action<int, string> SetChannelInputDevice { get; init; } = (_, _) => { };
    public Action<int, InputChannelMode> SetChannelInputMode { get; init; } = (_, _) => { };
    public Action<int, float> SetChannelInputGain { get; init; } = (_, _) => { };
}
