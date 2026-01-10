using HotMic.Common.Models;
using NAudio.CoreAudioApi;

namespace HotMic.Core.Engine;

public sealed class DeviceManager
{
    public IReadOnlyList<AudioDevice> GetInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        return devices.Select(device => new AudioDevice
        {
            Id = device.ID,
            Name = device.FriendlyName,
            IsDefault = device.ID == enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID,
            IsVirtual = device.FriendlyName.Contains("VB", StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.Select(device => new AudioDevice
        {
            Id = device.ID,
            Name = device.FriendlyName,
            IsDefault = device.ID == enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console).ID,
            IsVirtual = device.FriendlyName.Contains("VB", StringComparison.OrdinalIgnoreCase)
        }).ToList();
    }

    public AudioDevice? FindVBCableOutput()
    {
        return GetOutputDevices().FirstOrDefault(device => device.Name.Contains("VB-Cable", StringComparison.OrdinalIgnoreCase));
    }
}
