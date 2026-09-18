using CoreAudio;
using System;
using System.Text.RegularExpressions;

namespace AutoActions.Audio
{
    /// <summary>
    /// A playback or recording endpoint. Wraps an <see cref="MMDevice"/> from the CoreAudio package;
    /// the app only ever needs to name a device, identify it across restarts, and make it default.
    /// </summary>
    public class AudioDevice
    {
        // The endpoint id is "{0.0.0.00000000}.{a-real-guid}". The first real GUID in it identifies
        // the device and is what settings files store - keep reading it exactly this way, or every
        // saved audio action stops resolving.
        static readonly Regex GuidInId = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        internal MMDevice BaseDevice { get; private set; }

        public AudioDevice(MMDevice device)
        {
            BaseDevice = device;
            ID = ToId(device.ID);
            Name = device.FriendlyName;
            DeviceType = device.DataFlow == DataFlow.Capture ? AudioDeviceType.Capture
                : device.DataFlow == DataFlow.Render ? AudioDeviceType.Playback
                : AudioDeviceType.All;
            State = ToState(device.State);
        }

        public Guid ID { get; private set; }

        public string Name { get; private set; }

        public AudioDeviceType DeviceType { get; private set; }

        public DeviceState State { get; private set; }

        /// <summary>Read live: Windows, another application or a profile action can change it at any time.</summary>
        public bool IsDefaultDevice
        {
            get { return AudioController.Instance.IsDefault(this); }
        }

        public void SetAsDefault()
        {
            AudioController.Instance.SetDefault(this);
        }

        /// <summary>The device guid inside an endpoint id, which is what settings files store.</summary>
        public static Guid ToId(string endpointId)
        {
            Match match = endpointId == null ? Match.Empty : GuidInId.Match(endpointId);
            return match.Success ? new Guid(match.Value) : Guid.Empty;
        }

        static DeviceState ToState(CoreAudio.DeviceState state)
        {
            if ((state & CoreAudio.DeviceState.Active) != 0)
                return DeviceState.Active;
            if ((state & CoreAudio.DeviceState.Unplugged) != 0)
                return DeviceState.Unplugged;
            if ((state & CoreAudio.DeviceState.NotPresent) != 0)
                return DeviceState.NotPresent;
            return DeviceState.Disabled;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
