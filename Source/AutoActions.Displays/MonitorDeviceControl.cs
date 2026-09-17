using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace AutoActions.Displays
{
    /// <summary>
    /// Enables and disables the entries under "Monitors" in Device Manager - that Device Manager
    /// operation and nothing else. The display keeps working either way.
    ///
    /// Why an application would want that: with the monitor device disabled Windows loses the EDID
    /// and games stop overriding the scaling mode, which is how a true stretched resolution is made
    /// to stick. The cost is that Windows then has no monitor to hang a gamma ramp on -
    /// GetDeviceGammaRamp fails on every device context for that display, measured on both states of
    /// the same machine, so <see cref="DisplayColorControl"/>'s brightness, contrast and gamma do
    /// nothing until the device is enabled again. Digital vibrance and hue are unaffected: they go
    /// through NVAPI, below the layer this switches off.
    ///
    /// Changing a device state needs administrator rights. Without them SetupDiCallClassInstaller
    /// fails with ERROR_ACCESS_DENIED, which is reported like any other failure - nothing here
    /// throws, because this runs on the process watcher thread inside the daemon lock.
    /// </summary>
    public static class MonitorDeviceControl
    {
        public static event EventHandler<string> NewLog;

        private static void Log(string message)
        {
            try { NewLog?.Invoke(null, message); } catch { }
        }

        /// <summary>True when this process can actually change a device's state.</summary>
        public static bool IsElevated
        {
            get
            {
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch (Exception ex)
                {
                    Log($"Could not determine whether AutoActions is elevated: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>Every monitor device Windows currently knows about, enabled or not.</summary>
        public static List<MonitorDevice> GetMonitorDevices()
        {
            List<MonitorDevice> devices = new List<MonitorDevice>();
            Guid monitorClass = NativeMethods.MonitorClassGuid;
            IntPtr set = NativeMethods.SetupDiGetClassDevs(ref monitorClass, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
            if (set == NativeMethods.InvalidHandle)
            {
                Log($"Could not enumerate monitor devices: error {Marshal.GetLastWin32Error()}.");
                return devices;
            }
            try
            {
                NativeMethods.SP_DEVINFO_DATA info = new NativeMethods.SP_DEVINFO_DATA();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));
                for (uint index = 0; NativeMethods.SetupDiEnumDeviceInfo(set, index, ref info); index++)
                {
                    string instanceId = GetInstanceId(set, ref info);
                    if (!string.IsNullOrEmpty(instanceId))
                        devices.Add(new MonitorDevice(instanceId, GetName(set, ref info), IsEnabled(info.DevInst)));
                    info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));
                }
            }
            catch (Exception ex)
            {
                Log($"Could not enumerate monitor devices: {ex.Message}");
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
            return devices;
        }

        /// <summary>Enables or disables one monitor device. Already being in that state is a success.</summary>
        public static bool SetEnabled(string instanceId, bool enable)
        {
            if (string.IsNullOrEmpty(instanceId))
                return false;
            if (!IsElevated)
            {
                Log($"Cannot {(enable ? "enable" : "disable")} {instanceId}: AutoActions is not running as administrator.");
                return false;
            }

            Guid monitorClass = NativeMethods.MonitorClassGuid;
            IntPtr set = NativeMethods.SetupDiGetClassDevs(ref monitorClass, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
            if (set == NativeMethods.InvalidHandle)
            {
                Log($"Could not open the monitor device list: error {Marshal.GetLastWin32Error()}.");
                return false;
            }
            try
            {
                NativeMethods.SP_DEVINFO_DATA info = new NativeMethods.SP_DEVINFO_DATA();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));
                for (uint index = 0; NativeMethods.SetupDiEnumDeviceInfo(set, index, ref info); index++)
                {
                    if (string.Equals(GetInstanceId(set, ref info), instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsEnabled(info.DevInst) == enable)
                        {
                            Log($"Monitor device '{GetName(set, ref info)}' is already {(enable ? "enabled" : "disabled")}.");
                            return true;
                        }
                        return ChangeState(set, ref info, enable);
                    }
                    info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));
                }
                Log($"No monitor device matches {instanceId}; it may have been unplugged.");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Could not change the state of {instanceId}: {ex.Message}");
                return false;
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static bool ChangeState(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info, bool enable)
        {
            NativeMethods.SP_PROPCHANGE_PARAMS parameters = new NativeMethods.SP_PROPCHANGE_PARAMS();
            parameters.ClassInstallHeader.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_CLASSINSTALL_HEADER));
            parameters.ClassInstallHeader.InstallFunction = NativeMethods.DIF_PROPERTYCHANGE;
            parameters.StateChange = enable ? NativeMethods.DICS_ENABLE : NativeMethods.DICS_DISABLE;
            parameters.Scope = NativeMethods.DICS_FLAG_GLOBAL;
            parameters.HwProfile = 0;

            if (!NativeMethods.SetupDiSetClassInstallParams(set, ref info, ref parameters, Marshal.SizeOf(typeof(NativeMethods.SP_PROPCHANGE_PARAMS))))
            {
                Log($"Could not prepare the state change: error {Marshal.GetLastWin32Error()}.");
                return false;
            }
            if (!NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, set, ref info))
            {
                int error = Marshal.GetLastWin32Error();
                Log(error == NativeMethods.ErrorAccessDenied
                    ? "Windows refused the state change: AutoActions needs to run as administrator."
                    : $"Windows refused the state change: error {error}.");
                return false;
            }
            return true;
        }

        /// <summary>The state of every monitor device right now, to be put back later.</summary>
        public static MonitorDeviceSnapshot Capture()
        {
            return new MonitorDeviceSnapshot(GetMonitorDevices());
        }

        /// <summary>Puts every device in a snapshot back to the state it was captured in.</summary>
        public static bool Restore(MonitorDeviceSnapshot snapshot)
        {
            if (snapshot == null)
                return false;
            List<MonitorDevice> current = GetMonitorDevices();
            bool restored = true;
            foreach (MonitorDevice device in snapshot.Devices)
            {
                MonitorDevice now = current.FirstOrDefault(d => d.Equals(device));
                if (now == null || now.IsEnabled == device.IsEnabled)
                    continue;
                Log($"Restoring monitor device '{device.Name}' to {(device.IsEnabled ? "enabled" : "disabled")}.");
                restored &= SetEnabled(device.InstanceId, device.IsEnabled);
            }
            return restored;
        }

        /// <summary>A device node whose problem code is CM_PROB_DISABLED is the Device Manager disable.</summary>
        private static bool IsEnabled(uint devInst)
        {
            uint status;
            uint problem;
            if (NativeMethods.CM_Get_DevNode_Status(out status, out problem, devInst, 0) != NativeMethods.CrSuccess)
                return true;
            return !((status & NativeMethods.DN_HAS_PROBLEM) != 0 && problem == NativeMethods.CM_PROB_DISABLED);
        }

        private static string GetInstanceId(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info)
        {
            StringBuilder buffer = new StringBuilder(1024);
            int required;
            return NativeMethods.SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Capacity, out required)
                ? buffer.ToString()
                : string.Empty;
        }

        private static string GetName(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info)
        {
            return Property(set, ref info, NativeMethods.SPDRP_FRIENDLYNAME)
                ?? Property(set, ref info, NativeMethods.SPDRP_DEVICEDESC)
                ?? "Monitor";
        }

        private static string Property(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info, uint property)
        {
            StringBuilder buffer = new StringBuilder(512);
            uint type;
            uint required;
            if (!NativeMethods.SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, buffer, (uint)buffer.Capacity, out required))
                return null;
            string value = buffer.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static class NativeMethods
        {
            internal static readonly Guid MonitorClassGuid = new Guid("4d36e96e-e325-11ce-bfc1-08002be10318");
            internal static readonly IntPtr InvalidHandle = new IntPtr(-1);

            internal const uint DIGCF_PRESENT = 0x00000002;
            internal const uint DIF_PROPERTYCHANGE = 0x00000012;
            internal const uint DICS_ENABLE = 0x00000001;
            internal const uint DICS_DISABLE = 0x00000002;
            internal const uint DICS_FLAG_GLOBAL = 0x00000001;
            internal const uint SPDRP_DEVICEDESC = 0x00000000;
            internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
            internal const uint DN_HAS_PROBLEM = 0x00000400;
            internal const uint CM_PROB_DISABLED = 22;
            internal const int CrSuccess = 0;
            internal const int ErrorAccessDenied = 5;

            [StructLayout(LayoutKind.Sequential)]
            internal struct SP_DEVINFO_DATA
            {
                public int cbSize;
                public Guid ClassGuid;
                public uint DevInst;
                public IntPtr Reserved;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct SP_CLASSINSTALL_HEADER
            {
                public int cbSize;
                public uint InstallFunction;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct SP_PROPCHANGE_PARAMS
            {
                public SP_CLASSINSTALL_HEADER ClassInstallHeader;
                public uint StateChange;
                public uint Scope;
                public uint HwProfile;
            }

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("setupapi.dll", SetLastError = true)]
            internal static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, StringBuilder propertyBuffer, uint propertyBufferSize, out uint requiredSize);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiSetClassInstallParams(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

            [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

            [DllImport("setupapi.dll")]
            internal static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, int flags);
        }
    }

    /// <summary>One entry under "Monitors" in Device Manager.</summary>
    public class MonitorDevice
    {
        public string InstanceId { get; private set; }
        public string Name { get; private set; }
        public bool IsEnabled { get; private set; }

        public MonitorDevice(string instanceId, string name, bool isEnabled)
        {
            InstanceId = instanceId;
            Name = name;
            IsEnabled = isEnabled;
        }

        /// <summary>What the combo box shows, so a disabled device is obvious in the list.</summary>
        public string Description
        {
            get { return IsEnabled ? Name : $"{Name} (disabled)"; }
        }

        /// <summary>
        /// By instance id: the list is rebuilt on every read, so the view binds to objects that are
        /// never the same reference twice.
        /// </summary>
        public override bool Equals(object obj)
        {
            MonitorDevice other = obj as MonitorDevice;
            return other != null && string.Equals(other.InstanceId, InstanceId, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return InstanceId == null ? 0 : InstanceId.ToUpperInvariant().GetHashCode();
        }

        public override string ToString()
        {
            return Description;
        }
    }

    /// <summary>The state of every monitor device at one moment.</summary>
    public class MonitorDeviceSnapshot
    {
        public List<MonitorDevice> Devices { get; private set; }

        public MonitorDeviceSnapshot(List<MonitorDevice> devices)
        {
            Devices = devices ?? new List<MonitorDevice>();
        }

        public override string ToString()
        {
            if (Devices.Count == 0)
                return "no monitor devices";
            return string.Join(", ", Devices.Select(d => $"{d.Name} {(d.IsEnabled ? "enabled" : "disabled")}"));
        }
    }
}
