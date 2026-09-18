using CoreAudio;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoActions.Audio
{
    /// <summary>
    /// The playback and recording endpoints, and which one is default. Built on the CoreAudio
    /// package (MIT) - it replaced the vendored AudioSwitcher wrappers, which are Ms-PL and cannot
    /// be shipped inside a GPL-3.0 release.
    /// </summary>
    public class AudioController
    {
        static AudioController _instance;

        public static AudioController Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AudioController();
                return _instance;
            }
        }

        readonly object _lockDevices = new object();
        readonly MMDeviceEnumerator _enumerator;
        readonly MMNotificationClient _notifications;

        List<AudioDevice> _outputAudioDevices = new List<AudioDevice>();
        List<AudioDevice> _inputAudioDevices = new List<AudioDevice>();

        public event EventHandler DevicesChanged;

        public IReadOnlyList<AudioDevice> OutputAudioDevices
        {
            get { lock (_lockDevices) { return _outputAudioDevices.AsReadOnly(); } }
        }

        public IReadOnlyList<AudioDevice> InputAudioDevices
        {
            get { lock (_lockDevices) { return _inputAudioDevices.AsReadOnly(); } }
        }

        public AudioController()
        {
            _enumerator = new MMDeviceEnumerator(Guid.NewGuid());
            _notifications = new MMNotificationClient(_enumerator);
            _notifications.DeviceAdded += (o, e) => UpdateDevices();
            _notifications.DeviceRemoved += (o, e) => UpdateDevices();
            _notifications.DeviceStateChanged += (o, e) => UpdateDevices();
            _notifications.DefaultDeviceChanged += (o, e) => OnDevicesChanged();
            UpdateDevices();
        }

        /// <summary>Re-reads the endpoint lists. Cheap enough to call whenever a page is shown.</summary>
        public void UpdateDevices()
        {
            try
            {
                // Unplugged devices are listed too: a headset that is off right now is still a valid
                // thing to name in a profile action.
                const CoreAudio.DeviceState wanted = CoreAudio.DeviceState.Active | CoreAudio.DeviceState.Unplugged;
                List<AudioDevice> outputs = Read(DataFlow.Render, wanted);
                List<AudioDevice> inputs = Read(DataFlow.Capture, wanted);
                lock (_lockDevices)
                {
                    _outputAudioDevices = outputs;
                    _inputAudioDevices = inputs;
                }
            }
            catch (Exception)
            {
                // Enumeration fails while the audio service restarts; the next call picks it up.
                return;
            }
            OnDevicesChanged();
        }

        List<AudioDevice> Read(DataFlow flow, CoreAudio.DeviceState state)
        {
            List<AudioDevice> devices = new List<AudioDevice>();
            MMDeviceCollection collection = _enumerator.EnumerateAudioEndPoints(flow, state);
            for (int i = 0; i < collection.Count; i++)
            {
                AudioDevice device = new AudioDevice(collection[i]);
                if (device.ID != Guid.Empty && !devices.Any(d => d.ID == device.ID))
                    devices.Add(device);
            }
            return devices;
        }

        internal bool IsDefault(AudioDevice device)
        {
            try
            {
                MMDevice standard = _enumerator.GetDefaultAudioEndpoint(device.BaseDevice.DataFlow, Role.Multimedia);
                return standard != null && AudioDevice.ToId(standard.ID) == device.ID;
            }
            catch (Exception)
            {
                // No default endpoint of that kind, which is not an error - nothing is default then.
                return false;
            }
        }

        internal void SetDefault(AudioDevice device)
        {
            _enumerator.SetDefaultAudioEndpoint(device.BaseDevice);
        }

        void OnDevicesChanged()
        {
            EventHandler handler = DevicesChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
