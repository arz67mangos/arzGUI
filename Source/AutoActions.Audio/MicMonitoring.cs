using CoreAudio;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoActions.Audio
{
    /// <summary>A playback endpoint that may carry a monitored input line ("listen to this device").</summary>
    public sealed class MonitoringEndpoint
    {
        /// <summary>Core Audio endpoint ID; empty means "the default playback device".</summary>
        public string Id { get; }
        public string Name { get; }

        public MonitoringEndpoint(string id, string name)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
        }

        public override string ToString() => Name;
        public override bool Equals(object obj) => obj is MonitoringEndpoint other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    }

    /// <summary>
    /// A physical input into a playback endpoint's signal path - the "Microphone" (or "Line In", ...)
    /// row in Speakers Properties -> Levels - together with the mute and volume subunits that sit
    /// closest to it on that path. Identified durably by endpoint ID plus the connector's local part
    /// ID, which drivers keep stable, rather than by walk order.
    /// </summary>
    public sealed class MonitoringLine
    {
        public string EndpointId { get; }
        public int ConnectorId { get; }
        /// <summary>Index of the endpoint connector whose ConnectedTo leads into the adapter topology holding this line's parts.</summary>
        internal int EndpointConnectorIndex { get; }
        public string Name { get; }
        public bool HasMute => MutePartId.HasValue;
        public bool HasVolume => VolumePartId.HasValue;
        /// <summary>True when the driver tags the connector as a microphone type or names it like one.</summary>
        public bool LooksLikeMicrophone { get; }
        internal int? MutePartId { get; }
        internal int? VolumePartId { get; }

        /// <summary>The value persisted in settings.</summary>
        public string LineId => ConnectorId.ToString();

        public string Description
        {
            get
            {
                List<string> controls = new List<string>();
                if (HasMute) controls.Add("mute");
                if (HasVolume) controls.Add("volume");
                return controls.Count == 0 ? Name : $"{Name} ({string.Join(", ", controls)})";
            }
        }

        internal MonitoringLine(string endpointId, int endpointConnectorIndex, int connectorId, string name, bool looksLikeMicrophone, int? mutePartId, int? volumePartId)
        {
            EndpointId = endpointId;
            EndpointConnectorIndex = endpointConnectorIndex;
            ConnectorId = connectorId;
            Name = name;
            LooksLikeMicrophone = looksLikeMicrophone;
            MutePartId = mutePartId;
            VolumePartId = volumePartId;
        }

        public override string ToString() => Description;
        public override bool Equals(object obj) => obj is MonitoringLine other && ConnectorId == other.ConnectorId && string.Equals(EndpointId, other.EndpointId, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode() => ConnectorId ^ StringComparer.OrdinalIgnoreCase.GetHashCode(EndpointId);
    }

    /// <summary>
    /// Controls microphone monitoring (the speaker button and slider on the Microphone line in
    /// Speakers Properties -> Levels) through the Core Audio device topology. Ported from
    /// osu-companion-app's AudioManager; see Reference/ in the repo. Every operation is a no-op with
    /// a log line when the hardware exposes nothing controllable - never an exception, never a dialog.
    /// </summary>
    public sealed class MicMonitoring
    {
        private static MicMonitoring _instance;
        public static MicMonitoring Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new MicMonitoring();
                return _instance;
            }
        }

        public event EventHandler<string> NewLog;

        readonly object _lock = new object();
        MMDeviceEnumerator _enumerator;

        private MicMonitoring()
        {
        }

        private void Log(string message)
        {
            try { NewLog?.Invoke(this, message); } catch { }
        }

        private MMDeviceEnumerator Enumerator
        {
            get
            {
                if (_enumerator == null)
                    _enumerator = CreateEnumerator();
                return _enumerator;
            }
        }

        static readonly Guid MMDeviceEnumeratorClsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        /// <summary>
        /// The vendored AudioSwitcher wrappers (AudioApi.CoreAudio) and the CoreAudio package both
        /// declare a coclass for the MMDeviceEnumerator CLSID. mmdevapi hands the same COM object to
        /// both, the CLR keeps exactly one RCW per COM identity, and whichever library instantiates
        /// second fails with an InvalidCastException to its own class - in this app that is CoreAudio,
        /// because AudioController runs first. So when the plain constructor fails, obtain the COM
        /// object by CLSID without naming a class and hand it to CoreAudio's wrapper through the two
        /// private fields its constructor would have filled.
        /// </summary>
        private static MMDeviceEnumerator CreateEnumerator()
        {
            Guid eventContext = Guid.NewGuid();
            try
            {
                return new MMDeviceEnumerator(eventContext);
            }
            catch (InvalidCastException)
            {
            }

            Type enumeratorType = typeof(MMDeviceEnumerator);
            System.Reflection.FieldInfo realEnumeratorField = enumeratorType.GetField("_realEnumerator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.FieldInfo eventContextField = enumeratorType.GetField("eventContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (realEnumeratorField == null || eventContextField == null)
                throw new InvalidOperationException("CoreAudio.MMDeviceEnumerator no longer has the _realEnumerator/eventContext fields; the RCW workaround needs updating for this CoreAudio version.");

            object rawEnumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid));
            MMDeviceEnumerator enumerator = (MMDeviceEnumerator)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(enumeratorType);
            realEnumeratorField.SetValue(enumerator, rawEnumerator); // the runtime QIs the RCW for CoreAudio's IMMDeviceEnumerator here
            eventContextField.SetValue(enumerator, eventContext);
            return enumerator;
        }

        /// <summary>All active playback endpoints.</summary>
        public IReadOnlyList<MonitoringEndpoint> GetRenderEndpoints()
        {
            lock (_lock)
            {
                List<MonitoringEndpoint> endpoints = new List<MonitoringEndpoint>();
                try
                {
                    foreach (MMDevice device in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, CoreAudio.DeviceState.Active))
                        endpoints.Add(new MonitoringEndpoint(device.ID, device.DeviceFriendlyName));
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: enumerating playback devices failed: {ex.Message}");
                }
                return endpoints;
            }
        }

        /// <summary>
        /// Every physical input line on the endpoint that has a mute and/or a volume control on its
        /// path. An empty <paramref name="endpointId"/> means the default playback device.
        /// </summary>
        public IReadOnlyList<MonitoringLine> GetControllableLines(string endpointId)
        {
            lock (_lock)
            {
                List<MonitoringLine> lines = new List<MonitoringLine>();
                MMDevice device = FindDevice(endpointId);
                if (device == null)
                    return lines;
                try
                {
                    DeviceTopology topology = device.DeviceTopology;
                    int connectorCount = topology.GetConnectorCount;
                    HashSet<int> visited = new HashSet<int>();
                    for (int i = 0; i < connectorCount; i++)
                    {
                        Connector connector = topology.GetConnector(i);
                        if (connector == null || !connector.IsConnected)
                            continue;
                        Connector remote = connector.ConnectedTo;
                        if (remote == null)
                            continue;
                        Walk(device.ID, i, remote.Part, new List<Part>(), visited, lines);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: reading the topology of '{device.DeviceFriendlyName}' failed: {ex.Message}");
                }
                return lines;
            }
        }

        /// <summary>
        /// The line the app should act on: the configured one when <paramref name="lineId"/> is set,
        /// otherwise the first line that looks like a microphone, otherwise the only line if there is
        /// exactly one. Null (with a log line) when nothing fits.
        /// </summary>
        public MonitoringLine ResolveLine(string endpointId, string lineId)
        {
            IReadOnlyList<MonitoringLine> lines = GetControllableLines(endpointId);
            if (lines.Count == 0)
            {
                Log($"Mic monitoring: {DescribeEndpoint(endpointId)} exposes no controllable input line.");
                return null;
            }
            if (!string.IsNullOrEmpty(lineId))
            {
                MonitoringLine configured = lines.FirstOrDefault(l => l.LineId == lineId);
                if (configured == null)
                    Log($"Mic monitoring: configured line {lineId} not found on {DescribeEndpoint(endpointId)}; available: {string.Join("; ", lines.Select(l => $"{l.LineId}={l.Description}"))}.");
                return configured;
            }
            MonitoringLine microphone = lines.FirstOrDefault(l => l.LooksLikeMicrophone);
            if (microphone != null)
                return microphone;
            if (lines.Count == 1)
                return lines[0];
            Log($"Mic monitoring: no line on {DescribeEndpoint(endpointId)} looks like a microphone and there are {lines.Count} to choose from; select one in the settings. Available: {string.Join("; ", lines.Select(l => $"{l.LineId}={l.Description}"))}.");
            return null;
        }

        /// <summary>True when monitoring is on (the line is not muted); null when unavailable.</summary>
        public bool? GetMonitoringState(string endpointId, string lineId)
        {
            lock (_lock)
            {
                try
                {
                    AudioMute mute = GetMute(endpointId, lineId);
                    if (mute == null)
                        return null;
                    return !mute.Mute;
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: reading the mute state failed: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>Monitoring on = line unmuted. Returns false (after logging) when it could not be applied.</summary>
        public bool SetMonitoringState(string endpointId, string lineId, bool enable)
        {
            lock (_lock)
            {
                try
                {
                    AudioMute mute = GetMute(endpointId, lineId);
                    if (mute == null)
                        return false;
                    mute.Mute = !enable;
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: setting monitoring {(enable ? "on" : "off")} failed: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Monitoring level as 0-100 across the control's dB range; null when unavailable.</summary>
        public double? GetMonitoringVolume(string endpointId, string lineId)
        {
            lock (_lock)
            {
                try
                {
                    AudioVolumeLevel volume = GetVolume(endpointId, lineId);
                    if (volume == null)
                        return null;
                    PerChannelDbLevel.LevelRange range = volume.GetLevelRange(0);
                    float span = range.MaxLevel - range.MinLevel;
                    if (span <= 0)
                        return 0;
                    double percentage = (volume.GetLevel(0) - range.MinLevel) / span * 100.0;
                    return Math.Max(0, Math.Min(100, percentage));
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: reading the monitoring volume failed: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>Sets the monitoring level, 0-100 mapped linearly across the control's dB range.</summary>
        public bool SetMonitoringVolume(string endpointId, string lineId, double percentage)
        {
            lock (_lock)
            {
                try
                {
                    AudioVolumeLevel volume = GetVolume(endpointId, lineId);
                    if (volume == null)
                        return false;
                    PerChannelDbLevel.LevelRange range = volume.GetLevelRange(0);
                    double clamped = Math.Max(0, Math.Min(100, percentage));
                    float level = range.MinLevel + (float)(clamped / 100.0 * (range.MaxLevel - range.MinLevel));
                    volume.SetLevelUniform(level);
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"Mic monitoring: setting the monitoring volume failed: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Human-readable target, for logs and the status card.</summary>
        public string DescribeTarget(string endpointId, string lineId)
        {
            MonitoringLine line = ResolveLine(endpointId, lineId);
            return line == null ? DescribeEndpoint(endpointId) : $"{DescribeEndpoint(endpointId)} / {line.Name}";
        }

        private string DescribeEndpoint(string endpointId)
        {
            lock (_lock)
            {
                MMDevice device = FindDevice(endpointId);
                if (device == null)
                    return string.IsNullOrEmpty(endpointId) ? "default playback device" : $"playback device {endpointId}";
                return $"'{device.DeviceFriendlyName}'";
            }
        }

        private AudioMute GetMute(string endpointId, string lineId)
        {
            MonitoringLine line = ResolveLine(endpointId, lineId);
            if (line == null)
                return null;
            if (!line.MutePartId.HasValue)
            {
                Log($"Mic monitoring: line '{line.Name}' has no mute control.");
                return null;
            }
            Part part = GetPart(line, line.MutePartId.Value);
            return part == null ? null : part.AudioMute;
        }

        private AudioVolumeLevel GetVolume(string endpointId, string lineId)
        {
            MonitoringLine line = ResolveLine(endpointId, lineId);
            if (line == null)
                return null;
            if (!line.VolumePartId.HasValue)
            {
                Log($"Mic monitoring: line '{line.Name}' has no volume control.");
                return null;
            }
            Part part = GetPart(line, line.VolumePartId.Value);
            return part == null ? null : part.AudioVolumeLevel;
        }

        /// <summary>
        /// The parts of a line belong to the adapter device's topology, not to the endpoint's own
        /// (which only holds its one connector), so GetPartById has to be asked of the topology on
        /// the far side of the endpoint connector - otherwise it answers "Element not found".
        /// </summary>
        private Part GetPart(MonitoringLine line, int partId)
        {
            MMDevice device = FindDevice(line.EndpointId);
            if (device == null)
                return null;
            DeviceTopology endpointTopology = device.DeviceTopology;
            if (line.EndpointConnectorIndex < 0 || line.EndpointConnectorIndex >= endpointTopology.GetConnectorCount)
                return null;
            Connector connector = endpointTopology.GetConnector(line.EndpointConnectorIndex);
            if (connector == null || !connector.IsConnected)
                return null;
            Connector remote = connector.ConnectedTo;
            if (remote == null || remote.Part == null)
                return null;
            return remote.Part.TopologyObject.GetPartById(partId);
        }

        /// <summary>
        /// The active playback endpoint with this ID, or the default one for an empty ID. A configured
        /// device that is gone is reported and NOT silently replaced by the default device - that
        /// would mute a line on the wrong hardware.
        /// </summary>
        private MMDevice FindDevice(string endpointId)
        {
            try
            {
                if (string.IsNullOrEmpty(endpointId))
                    return Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                foreach (MMDevice device in Enumerator.EnumerateAudioEndPoints(DataFlow.Render, CoreAudio.DeviceState.Active))
                    if (string.Equals(device.ID, endpointId, StringComparison.OrdinalIgnoreCase))
                        return device;
                Log($"Mic monitoring: playback device {endpointId} is not active or no longer present.");
                return null;
            }
            catch (Exception ex)
            {
                Log($"Mic monitoring: looking up the playback device failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Depth-first walk upstream (EnumPartsIncoming) from the endpoint's device-side connector.
        /// Every physical connector with no further inputs is a line; the controls closest to it on
        /// the path are the last mute/volume subunits seen on the way down. That is what the original
        /// "deepest part with AudioMute whose descendants include a Microphone connector" search
        /// found, generalised to every input and without the name requirement.
        /// </summary>
        private static void Walk(string endpointId, int endpointConnectorIndex, Part part, List<Part> controlsOnPath, HashSet<int> visited, List<MonitoringLine> lines)
        {
            if (part == null || !visited.Add(part.LocalId))
                return;

            AudioMute mute = null;
            AudioVolumeLevel volume = null;
            try { mute = part.AudioMute; } catch { }
            try { volume = part.AudioVolumeLevel; } catch { }
            bool isControl = mute != null || volume != null;
            if (isControl)
                controlsOnPath.Add(part);

            PartsList incoming = null;
            try { incoming = part.EnumPartsIncoming; } catch { }
            int incomingCount = incoming != null ? incoming.Count : 0;

            if (part.PartType == PartType.Connector && incomingCount == 0)
            {
                if (IsPhysicalConnector(part))
                {
                    int? mutePartId = null;
                    int? volumePartId = null;
                    for (int i = controlsOnPath.Count - 1; i >= 0; i--)
                    {
                        Part control = controlsOnPath[i];
                        if (!mutePartId.HasValue && HasMute(control))
                            mutePartId = control.LocalId;
                        if (!volumePartId.HasValue && HasVolume(control))
                            volumePartId = control.LocalId;
                    }
                    if (mutePartId.HasValue || volumePartId.HasValue)
                    {
                        string name = part.Name;
                        string subType = null;
                        try { subType = part.SubTypeName; } catch { }
                        if (string.IsNullOrEmpty(name))
                            name = string.IsNullOrEmpty(subType) ? $"Input {part.LocalId}" : subType;
                        bool looksLikeMicrophone =
                            (subType != null && subType.IndexOf("Microphone", StringComparison.OrdinalIgnoreCase) >= 0)
                            || name.IndexOf("Mic", StringComparison.OrdinalIgnoreCase) >= 0;
                        lines.Add(new MonitoringLine(endpointId, endpointConnectorIndex, part.LocalId, name, looksLikeMicrophone, mutePartId, volumePartId));
                    }
                }
            }

            for (int i = 0; i < incomingCount; i++)
            {
                Part child = null;
                try { child = incoming.Part(i); } catch { }
                Walk(endpointId, endpointConnectorIndex, child, controlsOnPath, visited, lines);
            }

            if (isControl)
                controlsOnPath.RemoveAt(controlsOnPath.Count - 1);
        }

        private static bool HasMute(Part part)
        {
            try { return part.AudioMute != null; } catch { return false; }
        }

        private static bool HasVolume(Part part)
        {
            try { return part.AudioVolumeLevel != null; } catch { return false; }
        }

        /// <summary>Software connectors are the render stream itself, not an input line a user would monitor.</summary>
        private static bool IsPhysicalConnector(Part part)
        {
            try
            {
                Connector connector = part.Connector;
                if (connector == null)
                    return true;
                ConnectorType type = connector.ConnectorType;
                return type != ConnectorType.Software_IO && type != ConnectorType.Software_Fixed;
            }
            catch
            {
                return true;
            }
        }
    }
}
