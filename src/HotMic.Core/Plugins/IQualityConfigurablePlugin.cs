using HotMic.Common.Configuration;

namespace HotMic.Core.Plugins;

public interface IQualityConfigurablePlugin
{
    void ApplyQuality(AudioQualityProfile profile);
}
