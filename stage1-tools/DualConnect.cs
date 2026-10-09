// DualConnect.exe: connect or disconnect a paired Bluetooth headset (AirPods) on Windows
// from user mode, without unpairing, admin rights, registry changes or drivers.
//
// Method: the one ToothTray uses (github.com/m2jean/ToothTray). Each Bluetooth audio
// endpoint sits on a kernel-streaming filter owned by Windows' in-box Bluetooth audio
// driver. Sending KSPROPERTY_ONESHOT_RECONNECT or KSPROPERTY_ONESHOT_DISCONNECT (property
// set KSPROPSETID_BtAudio) to that filter through IKsControl asks the driver to connect
// or disconnect the headset's audio profiles.
//
// UNTESTED: written on 8 and 9 Oct 2026 without access to a Windows machine. Compiled only
// as a syntax and type check. Nothing here has run against real AirPods.
//
// Language level: C# 5, so the csc.exe that ships with .NET Framework 4 can build it:
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:DualConnect.exe DualConnect.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DualConnect
{
    // ---------------------------------------------------------------- COM interop

    internal static class Native
    {
        public const int CLSCTX_ALL = 0x17;
        public const int STGM_READ = 0;
        public const int DEVICE_STATE_ACTIVE = 0x1;
        public const int DEVICE_STATE_DISABLED = 0x2;
        public const int DEVICE_STATE_NOTPRESENT = 0x4;
        public const int DEVICE_STATE_UNPLUGGED = 0x8;
        public const int DEVICE_STATEMASK_ALL = 0xF;
        public const int eRender = 0;
        public const int eCapture = 1;
        public const int eMultimedia = 1;
        public const int E_NOTFOUND = unchecked((int)0x80070490);
        public const int E_FAIL = unchecked((int)0x80004005);
        public const uint KSPROPERTY_TYPE_GET = 0x1;
        public const ushort VT_LPWSTR = 31;
        public const ushort VT_CLSID = 72;

        public static readonly Guid IID_IDeviceTopology = new Guid("2A07407E-6497-4A18-9787-32F79BD0D98F");
        public static readonly Guid IID_IKsControl = new Guid("28F54685-06FD-11D2-B27A-00A0C9223196");
        public static readonly Guid IID_IAudioMeterInformation = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");

        // ksmedia.h: KSPROPSETID_BtAudio and its KSPROPERTY_BTAUDIO enum.
        public static readonly Guid KSPROPSETID_BtAudio = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
        public const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
        public const uint KSPROPERTY_ONESHOT_DISCONNECT = 1;

        public static PropertyKey PKEY_Device_FriendlyName =
            new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
        public static PropertyKey PKEY_DeviceInterface_FriendlyName =
            new PropertyKey(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2);
        // Same value on every endpoint of one physical device (stereo and hands-free alike).
        public static PropertyKey PKEY_Device_ContainerId =
            new PropertyKey(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant pvar);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid fmtid, int pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    // PROPVARIANT is 16 bytes on 32-bit and 24 bytes on 64-bit Windows. Only the pointer
    // member is read here (VT_LPWSTR, VT_CLSID); the second pointer pads the struct to full size.
    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(16)] public IntPtr padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
        [PreserveSig] int GetPartById(uint id, out IntPtr part);
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSignalPath(IntPtr partFrom, IntPtr partTo, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);   // named GetType in devicetopology.h; renamed to avoid hiding object.GetType
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected(out int connected);    // BOOL, 4 bytes
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr propertyData, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsMethod(IntPtr method, uint methodLength, IntPtr methodData, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsEvent(IntPtr evt, uint eventLength, IntPtr eventData, uint dataLength, out uint bytesReturned);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint count);
        [PreserveSig] int GetChannelsPeakValues(uint count, IntPtr peaks);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }

    // ---------------------------------------------------------------- public model

    public sealed class EndpointInfo
    {
        public string Name;          // e.g. "Headphones (AirPods Pro)"
        public string DeviceName;    // e.g. "AirPods Pro"
        public string Flow;          // "render" or "capture"
        public string State;         // "active", "unplugged", "notpresent", "disabled"
        public bool IsDefault;       // default multimedia device for its direction
        public float Peak;           // 0..1 level Windows is sending; -1 when not active, not readable, or a microphone
        public string Id;            // Core Audio endpoint id
        public string FilterId;      // first Bluetooth KS filter behind the endpoint (null when not looked up)
        public string ContainerId;   // physical device the endpoint belongs to
        internal List<string> FilterIds = new List<string>();
    }

    public sealed class Result
    {
        public string Command;
        public bool Ok;
        public int ExitCode;
        public long ElapsedMs;
        public string Message;
        public List<EndpointInfo> Endpoints = new List<EndpointInfo>();
        internal bool Sent;          // Windows accepted at least one request (not in the output)
    }

    /// <summary>One reading for the logger: the matched device's endpoints summed up.</summary>
    public sealed class Observation
    {
        public string RenderState;    // active, unplugged, disabled, notpresent, or none
        public string CaptureState;
        public string RenderDefault;  // yes, no, or empty when there is no playback endpoint
        public string RenderPeak;     // highest level across the playback endpoints, "0.000"; empty when none is readable
        public int Devices;           // physical devices that matched; above 1 the name is ambiguous
    }

    // ---------------------------------------------------------------- core operations

    public static class Core
    {
        public const int ExitOk = 0;
        public const int ExitUsage = 1;
        public const int ExitNotFound = 2;
        public const int ExitRefused = 3;
        public const int ExitTimeout = 4;

        // Milliseconds between repeated requests. One request can be swallowed while the
        // AirPods are asleep or held by the phone, and the Bluetooth stack does not retry it
        // (reported by the QuickPods and PodBridge projects; not measured here).
        private const int ResendMs = 4000;
        // How long a connect keeps watching after an interrupted switch. Kept short, and with at
        // most one repeated request, so it can't fight the iPhone for long (for example a call).
        private const int ConnectSettleMs = 2 * ResendMs;
        private const int PollMs = 200;

        // Bluetooth audio filters live under the BTHENUM (A2DP), BTHHFENUM (hands-free) or
        // BTHLEENUM (LE Audio) bus. ToothTray tests the "{2}.\\?\\bth" prefix for the same thing.
        private static bool IsBluetoothFilter(string filterId)
        {
            if (string.IsNullOrEmpty(filterId)) return false;
            string f = filterId.ToLowerInvariant();
            return f.Contains("bthenum") || f.Contains("bthhfenum") || f.Contains("bthleenum");
        }

        private static string StateName(int state)
        {
            switch (state)
            {
                case Native.DEVICE_STATE_ACTIVE: return "active";
                case Native.DEVICE_STATE_DISABLED: return "disabled";
                case Native.DEVICE_STATE_NOTPRESENT: return "notpresent";
                case Native.DEVICE_STATE_UNPLUGGED: return "unplugged";
                default: return "state" + state.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string ReadString(IPropertyStore store, PropertyKey key)
        {
            PropVariant pv;
            if (store.GetValue(ref key, out pv) != 0) return null;
            try
            {
                if (pv.vt == Native.VT_LPWSTR && pv.pointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringUni(pv.pointerValue);
                return null;
            }
            finally
            {
                Native.PropVariantClear(ref pv);
            }
        }

        private static string ReadGuid(IPropertyStore store, PropertyKey key)
        {
            PropVariant pv;
            if (store.GetValue(ref key, out pv) != 0) return null;
            try
            {
                if (pv.vt == Native.VT_CLSID && pv.pointerValue != IntPtr.Zero)
                    return ((Guid)Marshal.PtrToStructure(pv.pointerValue, typeof(Guid))).ToString("D");
                return null;
            }
            finally
            {
                Native.PropVariantClear(ref pv);
            }
        }

        private static IMMDeviceEnumerator NewEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        private static string DefaultId(IMMDeviceEnumerator en, int flow)
        {
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(flow, Native.eMultimedia, out dev) != 0 || dev == null) return null;
            try
            {
                string id;
                return dev.GetId(out id) == 0 ? id : null;
            }
            finally { Marshal.ReleaseComObject(dev); }
        }

        // Every Bluetooth KS filter wired to the endpoint, over all of its connectors.
        private static List<string> FilterIdsOf(IMMDevice endpoint)
        {
            var ids = new List<string>();
            Guid iid = Native.IID_IDeviceTopology;
            object o;
            if (endpoint.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) return ids;
            IDeviceTopology topo = (IDeviceTopology)o;
            try
            {
                uint count;
                if (topo.GetConnectorCount(out count) != 0) return ids;
                for (uint i = 0; i < count; i++)
                {
                    IConnector conn;
                    if (topo.GetConnector(i, out conn) != 0 || conn == null) continue;
                    try
                    {
                        string filterId;
                        if (conn.GetDeviceIdConnectedTo(out filterId) == 0 && IsBluetoothFilter(filterId) && !ids.Contains(filterId))
                            ids.Add(filterId);
                    }
                    finally { Marshal.ReleaseComObject(conn); }
                }
            }
            finally { Marshal.ReleaseComObject(topo); }
            return ids;
        }

        private static float PeakOf(IMMDevice endpoint)
        {
            Guid iid = Native.IID_IAudioMeterInformation;
            object o;
            if (endpoint.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) return -1f;
            IAudioMeterInformation meter = (IAudioMeterInformation)o;
            try
            {
                float peak;
                if (meter.GetPeakValue(out peak) != 0) return -1f;
                return peak;
            }
            finally { Marshal.ReleaseComObject(meter); }
        }

        /// <summary>Lists Bluetooth audio endpoints whose name contains <paramref name="nameFilter"/>. Read only.</summary>
        public static List<EndpointInfo> FindEndpoints(string nameFilter)
        {
            return FindEndpoints(nameFilter, true);
        }

        /// <summary>
        /// Read only. With resolveFilters false it skips the device-topology walk and matches by
        /// name alone (FilterId stays null), the most passive reading, used by the logger.
        /// Throws COMException when Windows cannot list its audio endpoints at all.
        /// </summary>
        public static List<EndpointInfo> FindEndpoints(string nameFilter, bool resolveFilters)
        {
            var list = new List<EndpointInfo>();
            IMMDeviceEnumerator en = NewEnumerator();
            try
            {
                string defRender = DefaultId(en, Native.eRender);
                string defCapture = DefaultId(en, Native.eCapture);
                for (int flow = Native.eRender; flow <= Native.eCapture; flow++)
                {
                    IMMDeviceCollection col;
                    int hr = en.EnumAudioEndpoints(flow, Native.DEVICE_STATEMASK_ALL, out col);
                    if (hr != 0 || col == null)
                        throw new COMException("Windows could not list its audio devices", hr != 0 ? hr : Native.E_FAIL);
                    try
                    {
                        uint count;
                        hr = col.GetCount(out count);
                        if (hr != 0) throw new COMException("Windows could not count its audio devices", hr);
                        for (uint i = 0; i < count; i++)
                        {
                            IMMDevice dev;
                            if (col.Item(i, out dev) != 0 || dev == null) continue;
                            try
                            {
                                var info = Describe(dev, flow, nameFilter, defRender, defCapture, resolveFilters);
                                if (info != null) list.Add(info);
                            }
                            finally { Marshal.ReleaseComObject(dev); }
                        }
                    }
                    finally { Marshal.ReleaseComObject(col); }
                }
            }
            finally { Marshal.ReleaseComObject(en); }
            return list;
        }

        // As above, keeping only the endpoints of one physical device when containerId is given.
        internal static List<EndpointInfo> FindEndpoints(string nameFilter, string containerId, bool resolveFilters)
        {
            List<EndpointInfo> list = FindEndpoints(nameFilter, resolveFilters);
            if (containerId == null) return list;
            var kept = new List<EndpointInfo>();
            foreach (var e in list)
                if (e.ContainerId != null && string.Equals(e.ContainerId, containerId, StringComparison.OrdinalIgnoreCase)) kept.Add(e);
            return kept;
        }

        private static string Selector(string nameFilter, string containerId)
        {
            return "'" + nameFilter + "'" + (containerId != null ? " with containerId " + containerId : "");
        }

        private static bool Matches(EndpointInfo info, string nameFilter)
        {
            if (string.IsNullOrEmpty(nameFilter)) return false;
            string f = nameFilter.ToLowerInvariant();
            return (info.Name != null && info.Name.ToLowerInvariant().Contains(f))
                || (info.DeviceName != null && info.DeviceName.ToLowerInvariant().Contains(f));
        }

        // Returns null for an endpoint whose name doesn't match, or (with resolveFilters) that
        // has no Bluetooth filter behind it. The name is checked first, so other devices'
        // endpoints are never activated.
        private static EndpointInfo Describe(IMMDevice dev, int flow, string nameFilter, string defRender, string defCapture, bool resolveFilters)
        {
            var info = new EndpointInfo();
            info.Flow = flow == Native.eRender ? "render" : "capture";
            IPropertyStore store;
            if (dev.OpenPropertyStore(Native.STGM_READ, out store) != 0 || store == null) return null;
            try
            {
                info.Name = ReadString(store, Native.PKEY_Device_FriendlyName);
                info.DeviceName = ReadString(store, Native.PKEY_DeviceInterface_FriendlyName);
                if (!Matches(info, nameFilter)) return null;
                info.ContainerId = ReadGuid(store, Native.PKEY_Device_ContainerId);
            }
            finally { Marshal.ReleaseComObject(store); }

            if (resolveFilters)
            {
                info.FilterIds = FilterIdsOf(dev);
                if (info.FilterIds.Count == 0) return null;
                info.FilterId = info.FilterIds[0];
            }
            dev.GetId(out info.Id);
            int state;
            if (dev.GetState(out state) != 0) state = 0;
            info.State = StateName(state);
            info.IsDefault = info.Id != null && info.Id == (flow == Native.eRender ? defRender : defCapture);
            // Playback only: nothing is ever activated on the microphone endpoint, so reading
            // cannot push the headset into its hands-free profile.
            info.Peak = (flow == Native.eRender && state == Native.DEVICE_STATE_ACTIVE) ? PeakOf(dev) : -1f;
            return info;
        }

        private static bool IsPresent(EndpointInfo e)
        {
            return e.State == "active" || e.State == "unplugged";
        }

        private static string DisplayName(List<EndpointInfo> group)
        {
            foreach (var e in group) if (e.Flow == "render" && !string.IsNullOrEmpty(e.DeviceName)) return e.DeviceName;
            foreach (var e in group) if (!string.IsNullOrEmpty(e.DeviceName)) return e.DeviceName;
            return group[0].Name ?? "(no name)";
        }

        // Groups the present (active or unplugged) endpoints by physical device and returns
        // the one device a command acts on. When several devices match, returns null and
        // explains in 'problem', so nothing is ever sent to the wrong headset.
        internal static List<EndpointInfo> SelectDevice(List<EndpointInfo> endpoints, out string problem)
        {
            problem = null;
            var order = new List<string>();
            var groups = new Dictionary<string, List<EndpointInfo>>();
            foreach (var e in endpoints)
            {
                if (!IsPresent(e)) continue;
                string key = e.ContainerId ?? "";
                List<EndpointInfo> g;
                if (!groups.TryGetValue(key, out g))
                {
                    g = new List<EndpointInfo>();
                    groups[key] = g;
                    order.Add(key);
                }
                g.Add(e);
            }
            if (order.Count == 0) return new List<EndpointInfo>();
            if (order.Count == 1) return groups[order[0]];
            var parts = new List<string>();
            var names = new List<string>();
            bool sameName = false;
            foreach (string k in order)
            {
                string n = DisplayName(groups[k]);
                foreach (string seen in names)
                    if (string.Equals(seen, n, StringComparison.OrdinalIgnoreCase)) sameName = true;
                names.Add(n);
                parts.Add("'" + n + "' (" + (AnyRenderActive(groups[k]) ? "connected" : "not connected")
                    + ", containerId " + (k.Length > 0 ? k : "unknown") + ")");
            }
            problem = order.Count.ToString(CultureInfo.InvariantCulture) + " paired devices match the name: "
                + string.Join(", ", parts.ToArray()) + ". "
                + (sameName
                    ? "Some have the same name, so --name can't tell them apart: add --container with the containerId of yours."
                    : "Use --name with text that only one of them contains, or --container with the containerId of yours.");
            return null;
        }

        // Sends one KSPROPERTY_ONESHOT_* request to one KS filter. Returns the HRESULT.
        private static int SendOneShot(IMMDeviceEnumerator en, string filterId, uint propertyId)
        {
            IMMDevice filter;
            int hr = en.GetDevice(filterId, out filter);
            if (hr != 0 || filter == null) return hr != 0 ? hr : Native.E_NOTFOUND;
            try
            {
                Guid iid = Native.IID_IKsControl;
                object o;
                hr = filter.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o);
                if (hr != 0 || o == null) return hr != 0 ? hr : Native.E_NOTFOUND;
                IKsControl ks = (IKsControl)o;
                try
                {
                    var prop = new KsProperty();
                    prop.Set = Native.KSPROPSETID_BtAudio;
                    prop.Id = propertyId;
                    prop.Flags = Native.KSPROPERTY_TYPE_GET;
                    uint returned;
                    return ks.KsProperty(ref prop, (uint)Marshal.SizeOf(typeof(KsProperty)), IntPtr.Zero, 0, out returned);
                }
                finally { Marshal.ReleaseComObject(ks); }
            }
            finally { Marshal.ReleaseComObject(filter); }
        }

        // Distinct Bluetooth filters behind the chosen device's endpoints, playback filters first.
        private static List<string> FiltersOf(List<EndpointInfo> endpoints)
        {
            var filters = new List<string>();
            foreach (var pass in new[] { "render", "capture" })
                foreach (var e in endpoints)
                    if (e.Flow == pass && IsPresent(e))
                        foreach (string f in e.FilterIds)
                            if (!filters.Contains(f)) filters.Add(f);
            return filters;
        }

        private static bool AnyRenderActive(List<EndpointInfo> endpoints)
        {
            foreach (var e in endpoints)
                if (e.Flow == "render" && e.State == "active") return true;
            return false;
        }

        // Windows holds the link while any profile, playback or microphone, is active.
        private static bool AnyActive(List<EndpointInfo> endpoints)
        {
            foreach (var e in endpoints)
                if (e.State == "active") return true;
            return false;
        }

        private static bool NoneActive(List<EndpointInfo> endpoints)
        {
            return !AnyActive(endpoints);
        }

        // Sends one property to every filter. Returns how many requests Windows accepted.
        // An accepted request only means the driver will try; success is judged by endpoint state.
        private static int SendAll(IMMDeviceEnumerator en, List<string> filters, uint propertyId, string label, StringBuilder notes)
        {
            int accepted = 0;
            foreach (string f in filters)
            {
                int hr = SendOneShot(en, f, propertyId);
                if (hr == 0) accepted++;
                notes.AppendFormat(CultureInfo.InvariantCulture, "{0} hr=0x{1:X8}; ", label, hr);
            }
            return accepted;
        }

        // Re-reads the states of the endpoints chosen at the start, by endpoint id, without the
        // topology walk. A failed or partial reading keeps the last known state, so a hiccup
        // in Windows' audio service never looks like a disconnect.
        private static List<EndpointInfo> Refresh(string nameFilter, List<EndpointInfo> previous)
        {
            List<EndpointInfo> fresh;
            try { fresh = FindEndpoints(nameFilter, false); }
            catch (Exception) { return previous; }
            var result = new List<EndpointInfo>();
            foreach (var old in previous)
            {
                EndpointInfo now = null;
                foreach (var f in fresh)
                    if (f.Id != null && f.Id == old.Id) { now = f; break; }
                if (now == null) { result.Add(old); continue; }
                now.FilterId = old.FilterId;
                now.FilterIds = old.FilterIds;
                result.Add(now);
            }
            return result;
        }

        // Polls until done() holds, the deadline passes, or a newer command signals 'cancel'.
        // While waiting it re-sends the request every ResendMs, but only while a whole interval
        // still fits before the deadline. A request the driver already accepted can still
        // complete after the deadline; nothing here can take it back.
        // watchUntilMs: keep watching after done() first holds, until then (at most the deadline),
        // and send the request again at once, however close the deadline is, whenever the state
        // flips back, up to flipResends times. Used after an interrupted switch, whose last
        // request can still land. 0 and 0 = return as soon as done() holds.
        private static List<EndpointInfo> WaitFor(string nameFilter, List<EndpointInfo> endpoints, Func<List<EndpointInfo>, bool> done,
            long watchUntilMs, int flipResends, Stopwatch sw, long deadlineMs, IMMDeviceEnumerator en, List<string> filters,
            uint resendProperty, string label, StringBuilder notes, WaitHandle cancel, ref bool sent, out bool superseded)
        {
            superseded = false;
            List<EndpointInfo> eps = Refresh(nameFilter, endpoints);
            bool wasDone = done(eps), flipped = false;
            long nextResend = sw.ElapsedMilliseconds + ResendMs;
            while ((!wasDone || sw.ElapsedMilliseconds < watchUntilMs) && sw.ElapsedMilliseconds < deadlineMs)
            {
                if (cancel != null ? cancel.WaitOne(PollMs) : SleepPoll())
                {
                    superseded = true;
                    return eps;
                }
                long now = sw.ElapsedMilliseconds;
                if (!wasDone && (flipped || (now >= nextResend && now + ResendMs <= deadlineMs)))
                {
                    SendAll(en, filters, resendProperty, label + "-again", notes);
                    sent = true;
                    flipped = false;
                    nextResend = sw.ElapsedMilliseconds + ResendMs;
                }
                eps = Refresh(nameFilter, eps);
                bool isDone = done(eps);
                if (wasDone && !isDone && flipResends > 0)
                {
                    flipped = true;   // flipped back: ask again on the next poll
                    flipResends--;
                }
                wasDone = isDone;
            }
            // A stop that arrives while the last reading comes in still counts.
            if (IsSet(cancel)) superseded = true;
            return eps;
        }

        private static bool IsSet(WaitHandle cancel)
        {
            return cancel != null && cancel.WaitOne(0);
        }

        private static bool SleepPoll()
        {
            Thread.Sleep(PollMs);
            return false;
        }

        public static Result Status(string nameFilter)
        {
            return Status(nameFilter, null);
        }

        internal static Result Status(string nameFilter, string containerId)
        {
            var sw = Stopwatch.StartNew();
            var r = new Result();
            r.Command = "status";
            r.Endpoints = FindEndpoints(nameFilter, containerId, true);
            string problem;
            List<EndpointInfo> device = SelectDevice(r.Endpoints, out problem);
            if (problem != null)
            {
                r.ExitCode = ExitUsage;
                r.Message = problem;
            }
            else if (r.Endpoints.Count == 0)
            {
                r.ExitCode = ExitNotFound;
                r.Message = "no Bluetooth audio endpoint matches " + Selector(nameFilter, containerId);
            }
            else
            {
                r.ExitCode = ExitOk;
                r.Message = AnyRenderActive(device) ? "connected" : "not connected";
            }
            return Finish(r, sw);
        }

        public static Result Connect(string nameFilter, int timeoutSeconds) { return Connect(nameFilter, timeoutSeconds, null); }
        public static Result Disconnect(string nameFilter, int timeoutSeconds) { return Disconnect(nameFilter, timeoutSeconds, null); }
        public static Result Take(string nameFilter, int timeoutSeconds) { return Take(nameFilter, timeoutSeconds, null); }
        public static Result Give(string nameFilter, int timeoutSeconds) { return Give(nameFilter, timeoutSeconds, null); }

        // 'cancel', when given, is signalled by a newer DualConnect command; the running one then stops.
        public static Result Connect(string nameFilter, int timeoutSeconds, WaitHandle cancel)
        {
            return RunCommand("connect", nameFilter, null, timeoutSeconds, cancel, false, false);
        }

        public static Result Disconnect(string nameFilter, int timeoutSeconds, WaitHandle cancel)
        {
            return RunCommand("disconnect", nameFilter, null, timeoutSeconds, cancel, false, false);
        }

        // Up to timeoutSeconds for the drop, then up to timeoutSeconds again for the connect.
        public static Result Take(string nameFilter, int timeoutSeconds, WaitHandle cancel)
        {
            return RunCommand("take", nameFilter, null, timeoutSeconds, cancel, false, false);
        }

        public static Result Give(string nameFilter, int timeoutSeconds, WaitHandle cancel)
        {
            return RunCommand("give", nameFilter, null, timeoutSeconds, cancel, false, false);
        }

        // For the command line.
        // afterLaptopOk: this command waited for an earlier one that connected the laptop successfully.
        //   A take or connect then accepts an active link as that command's result, instead of
        //   dropping it and connecting all over again.
        // settle: this command waited for an earlier one that was stopped or failed after Windows
        //   accepted a request, or the earlier one was killed. That request can still land. A give
        //   or disconnect then watches until its timeout and drops the link again whenever it comes
        //   back; a connect or take watches for ConnectSettleMs after connecting and asks once more
        //   if the link drops in that time.
        internal static Result RunCommand(string command, string nameFilter, string containerId, int timeoutSeconds,
            WaitHandle cancel, bool afterLaptopOk, bool settle)
        {
            switch (command)
            {
                case "status": return Status(nameFilter, containerId);
                case "connect": return Run("connect", nameFilter, containerId, timeoutSeconds, false, true, cancel, afterLaptopOk, settle);
                case "disconnect": return Run("disconnect", nameFilter, containerId, timeoutSeconds, true, false, cancel, afterLaptopOk, settle);
                case "take": return Run("take", nameFilter, containerId, timeoutSeconds, true, true, cancel, afterLaptopOk, settle);
                default: return Run("give", nameFilter, containerId, timeoutSeconds, true, false, cancel, afterLaptopOk, settle);
            }
        }

        private const string Stopped = "stopped because a newer DualConnect command started";

        // disconnectFirst: when Windows holds the link, send DISCONNECT and wait until nothing is active.
        // connectAfter:    then send RECONNECT and wait, with a fresh timeout, until playback is active.
        // Every send is preceded by a check of 'cancel', so a stopped command sends nothing new.
        private static Result Run(string command, string nameFilter, string containerId, int timeoutSeconds,
            bool disconnectFirst, bool connectAfter, WaitHandle cancel, bool afterLaptopOk, bool settle)
        {
            var r = new Result();
            r.Command = command;
            var sw = Stopwatch.StartNew();
            long timeoutMs = (long)timeoutSeconds * 1000;
            List<EndpointInfo> all = FindEndpoints(nameFilter, containerId, true);
            string problem;
            List<EndpointInfo> eps = SelectDevice(all, out problem);
            if (problem != null)
            {
                r.ExitCode = ExitUsage;
                r.Message = problem + " Nothing was sent.";
                r.Endpoints = all;
                return Finish(r, sw);
            }
            List<string> filters = FiltersOf(eps);
            if (filters.Count == 0)
            {
                r.ExitCode = ExitNotFound;
                r.Message = "no Bluetooth audio endpoint matches " + Selector(nameFilter, containerId);
                r.Endpoints = all;
                return Finish(r, sw);
            }
            var notes = new StringBuilder();
            IMMDeviceEnumerator en = NewEnumerator();
            try
            {
                bool superseded;
                bool sentDisconnect = false, sentReconnect = false;
                if (afterLaptopOk && connectAfter && AnyRenderActive(eps))
                {
                    return End(r, sw, ExitOk, eps, notes, "already connected by the previous command");
                }

                // Disconnect phase. After an interrupted switch a give or disconnect watches even
                // when nothing is active yet, because the stopped command's connect can still land.
                if (disconnectFirst && (AnyActive(eps) || (settle && !connectAfter)))
                {
                    long deadline = sw.ElapsedMilliseconds + timeoutMs;
                    if (AnyActive(eps))
                    {
                        if (IsSet(cancel))
                            return End(r, sw, ExitTimeout, eps, notes, Stopped + "; nothing was sent");
                        if (SendAll(en, filters, Native.KSPROPERTY_ONESHOT_DISCONNECT, "disconnect", notes) == 0)
                            return End(r, sw, ExitRefused, Refresh(nameFilter, eps), notes, "Windows refused the disconnect");
                        sentDisconnect = true;
                        r.Sent = true;
                    }
                    // A give or disconnect may watch to its timeout and repeat itself on every flip:
                    // dropping the laptop's link never takes anything away from the iPhone.
                    bool watch = settle && !connectAfter;
                    eps = WaitFor(nameFilter, eps, NoneActive, watch ? deadline : 0, watch ? int.MaxValue : 0, sw, deadline,
                        en, filters, Native.KSPROPERTY_ONESHOT_DISCONNECT, "disconnect", notes, cancel, ref sentDisconnect, out superseded);
                    if (sentDisconnect) r.Sent = true;
                    if (superseded)
                        return End(r, sw, ExitTimeout, eps, notes, Stopped);
                    if (AnyActive(eps))
                        return End(r, sw, ExitTimeout, eps, notes, AnyRenderActive(eps)
                            ? "still connected at timeout" + (connectAfter ? "; nothing else was sent" : "")
                            : "sound disconnected but the microphone is still connected at timeout");
                    if (!connectAfter)
                        return End(r, sw, ExitOk, eps, notes, sentDisconnect ? "disconnected" : "already disconnected");
                }
                else if (disconnectFirst && !connectAfter)
                {
                    return End(r, sw, ExitOk, eps, notes, "already disconnected");
                }
                else if (!disconnectFirst && AnyRenderActive(eps) && !settle)
                {
                    return End(r, sw, ExitOk, eps, notes, "already connected");
                }

                // Connect phase, with a fresh timeout.
                long connectStart = sw.ElapsedMilliseconds, connectDeadline = connectStart + timeoutMs;
                if (!AnyRenderActive(eps))
                {
                    if (IsSet(cancel))
                        return End(r, sw, ExitTimeout, eps, notes, Stopped + (sentDisconnect ? "; the connect was not sent" : "; nothing was sent"));
                    if (SendAll(en, filters, Native.KSPROPERTY_ONESHOT_RECONNECT, "reconnect", notes) == 0)
                        return End(r, sw, ExitRefused, Refresh(nameFilter, eps), notes, "Windows refused the connect");
                    sentReconnect = true;
                    r.Sent = true;
                }
                eps = WaitFor(nameFilter, eps, AnyRenderActive, settle ? Math.Min(connectDeadline, connectStart + ConnectSettleMs) : 0, settle ? 1 : 0,
                    sw, connectDeadline, en, filters, Native.KSPROPERTY_ONESHOT_RECONNECT, "reconnect", notes, cancel, ref sentReconnect, out superseded);
                if (sentReconnect) r.Sent = true;
                if (superseded)
                {
                    // Only a command that moves the sound away can stop this one, so a connect
                    // already under way is asked to drop again rather than land after it.
                    if (sentReconnect)
                    {
                        SendAll(en, filters, Native.KSPROPERTY_ONESHOT_DISCONNECT, "undo", notes);
                        return End(r, sw, ExitTimeout, Refresh(nameFilter, eps), notes, Stopped + "; asked Windows to drop the connect it had started");
                    }
                    return End(r, sw, ExitTimeout, eps, notes, Stopped);
                }
                if (AnyRenderActive(eps))
                    return End(r, sw, ExitOk, eps, notes, sentReconnect ? "connected" : "already connected");
                return End(r, sw, ExitTimeout, eps, notes, AnyActive(eps)
                    ? "the microphone connected but sound did not at timeout"
                    : "not connected at timeout; the AirPods may be in the case, out of range or busy with the iPhone");
            }
            finally { Marshal.ReleaseComObject(en); }
        }

        private static Result End(Result r, Stopwatch sw, int exitCode, List<EndpointInfo> eps, StringBuilder notes, string outcome)
        {
            r.ExitCode = exitCode;
            r.Endpoints = eps;
            string n = notes.ToString().Trim();
            r.Message = n.Length > 0 ? n + " " + outcome : outcome;
            return Finish(r, sw);
        }

        private static Result Finish(Result r, Stopwatch sw)
        {
            r.ElapsedMs = sw.ElapsedMilliseconds;
            r.Ok = r.ExitCode == ExitOk;
            return r;
        }

        // ------------------------------------------------------------ logger support

        private static int StateRank(string state)
        {
            switch (state)
            {
                case "active": return 4;
                case "unplugged": return 3;
                case "disabled": return 2;
                case "notpresent": return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// The most connected state among the endpoints of one flow ("render" or "capture"):
        /// active beats unplugged beats disabled beats notpresent; "none" when there is none.
        /// </summary>
        public static string AggregateState(List<EndpointInfo> endpoints, string flow)
        {
            string best = null;
            foreach (var e in endpoints)
            {
                if (e.Flow != flow) continue;
                if (best == null || StateRank(e.State) > StateRank(best)) best = e.State;
            }
            return best ?? "none";
        }

        private static int CountDevices(List<EndpointInfo> endpoints)
        {
            var seen = new List<string>();
            foreach (var e in endpoints)
                if (IsPresent(e) && !seen.Contains(e.ContainerId ?? "")) seen.Add(e.ContainerId ?? "");
            return seen.Count;
        }

        /// <summary>Read only, for the logger: one summed-up reading without the topology walk.</summary>
        public static Observation Observe(string nameFilter)
        {
            List<EndpointInfo> all = FindEndpoints(nameFilter, false);
            return Summarize(all);
        }

        /// <summary>Sums up endpoints as Observe does. Public so it can be checked without Windows.</summary>
        public static Observation Summarize(List<EndpointInfo> all)
        {
            string problem;
            List<EndpointInfo> eps = SelectDevice(all, out problem);
            if (problem != null || eps.Count == 0) eps = all;   // ambiguous, or only old entries: sum up everything that matched
            var o = new Observation();
            o.Devices = CountDevices(all);
            o.RenderState = AggregateState(eps, "render");
            o.CaptureState = AggregateState(eps, "capture");
            bool anyRender = false, anyDefault = false;
            float peak = -1f;
            foreach (var e in eps)
            {
                if (e.Flow != "render") continue;
                anyRender = true;
                if (e.IsDefault) anyDefault = true;
                if (e.Peak > peak) peak = e.Peak;
            }
            o.RenderDefault = anyRender ? (anyDefault ? "yes" : "no") : "";
            o.RenderPeak = peak >= 0 ? peak.ToString("0.000", CultureInfo.InvariantCulture) : "";
            return o;
        }

        // ------------------------------------------------------------ output helpers

        public static string ToJson(Result r)
        {
            var sb = new StringBuilder();
            sb.Append("{\"command\":").Append(Q(r.Command));
            sb.Append(",\"ok\":").Append(r.Ok ? "true" : "false");
            sb.Append(",\"exitCode\":").Append(r.ExitCode.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elapsedMs\":").Append(r.ElapsedMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"message\":").Append(Q(r.Message));
            sb.Append(",\"endpoints\":[");
            for (int i = 0; i < r.Endpoints.Count; i++)
            {
                EndpointInfo e = r.Endpoints[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Q(e.Name));
                sb.Append(",\"deviceName\":").Append(Q(e.DeviceName));
                sb.Append(",\"flow\":").Append(Q(e.Flow));
                sb.Append(",\"state\":").Append(Q(e.State));
                sb.Append(",\"isDefault\":").Append(e.IsDefault ? "true" : "false");
                sb.Append(",\"peak\":").Append(e.Peak.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(",\"id\":").Append(Q(e.Id));
                sb.Append(",\"filterId\":").Append(Q(e.FilterId));
                sb.Append(",\"containerId\":").Append(Q(e.ContainerId));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Everything outside printable ASCII is escaped, so the line survives
                        // a redirected stdout that uses the OEM code page.
                        if (c < 0x20 || c > 0x7E) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static string ToText(Result r)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1} ({2} ms) {3}", r.Command, r.Ok ? "OK" : "FAILED", r.ElapsedMs, r.Message);
            sb.AppendLine();
            foreach (EndpointInfo e in r.Endpoints)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "  [{0,-7}] {1,-10} {2}{3}{4}",
                    e.Flow, e.State, e.Name ?? "(no name)",
                    e.IsDefault ? "  (default)" : "",
                    e.Peak >= 0 ? "  peak " + e.Peak.ToString("0.00", CultureInfo.InvariantCulture) : "");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string Csv(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        /// <summary>
        /// Appends one CSV row to actions.csv next to the exe. Never throws: returns null when
        /// the row was written, otherwise the reason it wasn't (for example the file is open in Excel).
        /// </summary>
        public static string AppendActionLog(Result r)
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(Core).Assembly.Location);
                string path = Path.Combine(dir, "actions.csv");
                string line = string.Join(",", new[] {
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
                    r.Command, r.Ok ? "ok" : "failed",
                    r.ExitCode.ToString(CultureInfo.InvariantCulture),
                    r.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                    AggregateState(r.Endpoints, "render"), AggregateState(r.Endpoints, "capture"),
                    Csv(r.Message) });
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        // FileShare.Read: a second writer gets a sharing violation and retries, so rows never overwrite each other.
                        using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                        {
                            if (fs.Length == 0) w.WriteLine("time,command,result,exitCode,elapsedMs,renderState,captureState,message");
                            w.WriteLine(line);
                        }
                        return null;
                    }
                    catch (IOException ex)
                    {
                        // Usually another DualConnect finishing at the same moment, or the file open in Excel.
                        if (attempt >= 5) return ex.Message;
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                // Logging must never block a switch.
                return ex.Message;
            }
        }
    }

    // ---------------------------------------------------------------- one switch at a time

    // Lets only one switching command run at a time, across processes (hotkeys start a new
    // process per press), and makes the newest key press win:
    // - On arrival a command stops every running or waiting command that moves the sound the
    //   other way, and cancels any stop request aimed at its own direction, since it is newer.
    // - A waiting command gives up without sending anything when a newer opposite one arrives.
    // - When the lock is free, the command reads what the previous holder left behind.
    // All objects are in the user's own session ("Local\").
    internal sealed class SwitchGate : IDisposable
    {
        private readonly Mutex switchLock;          // held for the whole switch
        private readonly Mutex arriveLock;          // held for a moment while a command arrives
        private readonly EventWaitHandle stopToLaptop;   // set: commands moving the sound to the laptop must stop
        private readonly EventWaitHandle stopToPhone;    // set: commands moving the sound away must stop
        private readonly EventWaitHandle aimToLaptop;    // set while the holder moves the sound to the laptop
        private readonly EventWaitHandle lastToLaptopOk; // the previous holder connected the laptop (exit 0)
        private readonly EventWaitHandle lastUnsettled;  // the previous holder stopped or failed after sending
        private readonly bool toLaptop;
        private bool released;

        public readonly bool Acquired;         // this process holds the lock
        public readonly bool Waited;           // another command held the lock when this one arrived
        public readonly bool HolderSameAim;    // ... and it moved the sound the same way
        public readonly bool Replaced;         // a newer opposite command arrived while this one waited
        public readonly bool PreviousLaptopOk; // the previous holder connected the laptop successfully
        public readonly bool Settle;           // the previous holder was stopped or failed after sending, or was killed
        public readonly WaitHandle StopThis;   // set when a newer command wants this one to stop

        public SwitchGate(bool toLaptop, int waitMs)
        {
            this.toLaptop = toLaptop;
            try
            {
                switchLock = new Mutex(false, @"Local\DualConnect.Switch");
                arriveLock = new Mutex(false, @"Local\DualConnect.Arrive");
                stopToLaptop = NewEvent("StopToLaptop");
                stopToPhone = NewEvent("StopToPhone");
                aimToLaptop = NewEvent("AimToLaptop");
                lastToLaptopOk = NewEvent("LastToLaptopOk");
                lastUnsettled = NewEvent("LastUnsettled");
            }
            catch
            {
                CloseAll();
                throw;
            }
            EventWaitHandle stopMine = toLaptop ? stopToLaptop : stopToPhone;
            EventWaitHandle stopOther = toLaptop ? stopToPhone : stopToLaptop;
            StopThis = stopMine;

            // Arrival, done under a short lock so two commands arriving together can't both end
            // up stopped or both left running.
            bool arrived = Lock(arriveLock, 500);
            try
            {
                stopMine.Reset();
                stopOther.Set();
            }
            finally { if (arrived) arriveLock.ReleaseMutex(); }

            bool abandoned = false;
            Acquired = Lock(switchLock, 0, ref abandoned);
            if (!Acquired)
            {
                Waited = true;
                HolderSameAim = aimToLaptop.WaitOne(0) == toLaptop;
                int index;
                try { index = WaitHandle.WaitAny(new WaitHandle[] { stopMine, switchLock }, waitMs); }
                catch (AbandonedMutexException) { index = 1; abandoned = true; }   // the holder was killed; the lock is ours
                if (index == 0) Replaced = true;
                else if (index == 1) Acquired = true;
            }
            if (Acquired && stopMine.WaitOne(0))
            {
                // A newer opposite command arrived just now. Leave the shared flags to it, and
                // pass on that the holder before was killed.
                if (abandoned) lastUnsettled.Set();
                switchLock.ReleaseMutex();
                Acquired = false;
                Replaced = true;
            }
            if (Acquired)
            {
                PreviousLaptopOk = lastToLaptopOk.WaitOne(0);
                Settle = abandoned || (Waited && lastUnsettled.WaitOne(0));
                lastToLaptopOk.Reset();
                lastUnsettled.Reset();
                if (toLaptop) aimToLaptop.Set(); else aimToLaptop.Reset();
            }
        }

        private static EventWaitHandle NewEvent(string name)
        {
            return new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\DualConnect." + name);
        }

        private static bool Lock(Mutex m, int waitMs)
        {
            bool abandoned = false;
            return Lock(m, waitMs, ref abandoned);
        }

        private static bool Lock(Mutex m, int waitMs, ref bool abandoned)
        {
            try { return m.WaitOne(waitMs); }
            catch (AbandonedMutexException) { abandoned = true; return true; }
        }

        // Called by the holder before it lets go, so the next command knows how this one ended.
        public void Record(Result r)
        {
            if (!Acquired || released) return;
            if (r.ExitCode == Core.ExitOk && toLaptop) lastToLaptopOk.Set();
            // A holder that inherited an unsettled state and was stopped before it sent anything
            // passes that state on.
            if ((r.Sent || Settle) && (r.ExitCode == Core.ExitRefused || r.ExitCode == Core.ExitTimeout)) lastUnsettled.Set();
        }

        public void Dispose()
        {
            if (Acquired && !released)
            {
                released = true;
                switchLock.ReleaseMutex();
            }
            CloseAll();
        }

        private void CloseAll()
        {
            foreach (WaitHandle h in new WaitHandle[] { switchLock, arriveLock, stopToLaptop, stopToPhone, aimToLaptop, lastToLaptopOk, lastUnsettled })
                if (h != null) h.Close();
        }
    }

    // ---------------------------------------------------------------- command line

    public static class Program
    {
        private const string Usage =
            "Usage: DualConnect.exe <status|connect|disconnect|take|give> [--name <text>] [--container <id>] [--json] [--timeout <seconds>] [--no-log]\n" +
            "  status      list matching Bluetooth audio endpoints (changes nothing)\n" +
            "  connect     ask Windows to connect the headset's audio\n" +
            "  disconnect  ask Windows to disconnect it (the pairing stays)\n" +
            "  take        sound to this laptop: disconnect first if Windows holds the link, then connect\n" +
            "  give        give the headset back: same as disconnect\n" +
            "  --name      part of the device name to match (default: AirPods)\n" +
            "  --container containerId of one device, from status, when two paired devices match the name\n" +
            "  --timeout   seconds to wait for each change (default: 20; take may use it twice)\n";

        private const int LockWaitMs = 5000;
        private const int StatusWatchdogMs = 15000;
        private const int WatchdogGraceMs = 8000;

        private static int finished;   // 0 until the main thread or the watchdog has claimed the output

        [MTAThread]
        public static int Main(string[] args)
        {
            var clock = Stopwatch.StartNew();
            string command = null, name = "AirPods", container = null;
            bool json = Array.IndexOf(args, "--json") >= 0, log = true;
            int timeout = 20;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--json") continue;
                else if (a == "--no-log") log = false;
                else if (a == "--name")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        return Fail(json, "--name needs a value");
                    name = args[++i];
                    if (name.Trim().Length == 0) return Fail(json, "--name must not be empty");
                }
                else if (a == "--container")
                {
                    if (i + 1 >= args.Length) return Fail(json, "--container needs a value");
                    Guid g;
                    if (!Guid.TryParse(args[++i], out g)) return Fail(json, "--container must be a containerId as shown by status");
                    container = g.ToString("D");
                }
                else if (a == "--timeout")
                {
                    if (i + 1 >= args.Length) return Fail(json, "--timeout needs a value");
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout) || timeout < 1 || timeout > 120)
                        return Fail(json, "--timeout must be 1 to 120 seconds");
                }
                else if (command == null && !a.StartsWith("--", StringComparison.Ordinal)) command = a.ToLowerInvariant();
                else return Fail(json, "unknown argument: " + a);
            }
            if (command == null) return Fail(json, "no command given");
            if (command != "status" && command != "connect" && command != "disconnect" && command != "take" && command != "give")
                return Fail(json, "unknown command: " + command);

            bool switching = command != "status";
            bool toLaptop = command == "connect" || command == "take";

            // A Windows call that never returns must not leave the process hanging: report and
            // exit a few seconds after the longest normal run. (QuickPods isolates the same call
            // for this reason.) Armed before the lock, so the busy path's read is covered too.
            string watchdogCommand = command;
            var watchdog = new Timer(delegate(object state)
            {
                if (Interlocked.CompareExchange(ref finished, 1, 0) != 0) return;
                var w = new Result();
                w.Command = watchdogCommand;
                w.ExitCode = Core.ExitRefused;
                w.ElapsedMs = clock.ElapsedMilliseconds;
                w.Message = "a Windows audio call did not return; gave up";
                PrintAndLog(w, json, log, switching);
                Environment.Exit(Core.ExitRefused);
            }, null, switching ? LockWaitMs + StatusWatchdogMs : StatusWatchdogMs, Timeout.Infinite);

            SwitchGate gate = null;
            if (switching)
            {
                try
                {
                    gate = new SwitchGate(toLaptop, LockWaitMs);
                }
                catch (Exception ex)
                {
                    // For example an elevated DualConnect created the objects with rights this one lacks.
                    var locked = new Result();
                    locked.Command = command;
                    locked.ExitCode = Core.ExitRefused;
                    locked.Message = "could not open the DualConnect lock: " + ex.Message + "; nothing was sent";
                    return Complete(locked, json, log, switching, clock, watchdog, null);
                }
                if (!gate.Acquired)
                {
                    var busy = new Result();
                    busy.Command = command;
                    busy.ExitCode = Core.ExitTimeout;
                    busy.Message = gate.Replaced
                        ? "replaced by a newer DualConnect command; nothing was sent"
                        : gate.HolderSameAim
                            ? "an earlier DualConnect command moving the sound the same way is still running; nothing was sent"
                            : "another DualConnect command is still running and did not stop; nothing was sent";
                    gate.Dispose();
                    // Read only, as status does, so the caller still sees the headset's state.
                    try { busy.Endpoints = Core.FindEndpoints(name, container, true); }
                    catch (Exception) { }
                    return Complete(busy, json, log, switching, clock, watchdog, null);
                }
                watchdog.Change((command == "take" ? 2 * timeout : timeout) * 1000 + WatchdogGraceMs, Timeout.Infinite);
            }

            Result r;
            try
            {
                r = Core.RunCommand(command, name, container, timeout, gate != null ? gate.StopThis : null,
                    gate != null && gate.Waited && gate.PreviousLaptopOk,
                    gate != null && gate.Settle);
            }
            catch (Exception ex)
            {
                r = new Result();
                r.Command = command;
                r.ExitCode = Core.ExitRefused;
                r.Message = ex.GetType().Name + ": " + ex.Message;
                r.Sent = true;   // unknown how far it got, so the next command is careful
                var com = ex as COMException;
                if (com != null) r.Message += string.Format(CultureInfo.InvariantCulture, " (hr=0x{0:X8})", com.ErrorCode);
            }
            return Complete(r, json, log, switching, clock, watchdog, gate);
        }

        private static int Complete(Result r, bool json, bool log, bool switching, Stopwatch clock, Timer watchdog, SwitchGate gate)
        {
            // The watchdog fired while the last call was returning: it has printed and is exiting.
            if (Interlocked.CompareExchange(ref finished, 1, 0) != 0) Thread.Sleep(Timeout.Infinite);
            watchdog.Dispose();
            r.ElapsedMs = clock.ElapsedMilliseconds;
            int code = Output(r, json, log, switching);
            if (gate != null)
            {
                gate.Record(r);
                gate.Dispose();
            }
            return code;
        }

        private static int Output(Result r, bool json, bool log, bool switching)
        {
            r.Ok = r.ExitCode == Core.ExitOk;
            PrintAndLog(r, json, log, switching);
            return r.ExitCode;
        }

        private static void PrintAndLog(Result r, bool json, bool log, bool switching)
        {
            if (log && switching)
            {
                string logError = Core.AppendActionLog(r);
                if (logError != null) r.Message += " (actions.csv not written: " + logError + ")";
            }
            Console.WriteLine(json ? Core.ToJson(r) : Core.ToText(r));
            Console.Out.Flush();
        }

        private static int Fail(bool json, string message)
        {
            if (json)
            {
                var r = new Result();
                r.Command = "usage";
                r.ExitCode = Core.ExitUsage;
                r.Message = message;
                Console.WriteLine(Core.ToJson(r));
            }
            else
            {
                Console.Error.WriteLine(message);
                Console.Error.Write(Usage);
            }
            return Core.ExitUsage;
        }
    }
}
