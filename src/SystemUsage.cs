using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CodexQuotaMonitor {
    public sealed class TaskbarSelection {
        public bool Quota,Credits,Resets,Yesterday,Total,Upload,Download,Cpu,Memory,Gpu,Disk;
        public bool AnySystem {get{return Upload||Download||Cpu||Memory||Gpu||Disk;}}
        public bool AnyCodex {get{return Quota||Credits||Resets||Yesterday||Total;}}
        public static TaskbarSelection From(Settings s){return new TaskbarSelection {Quota=s.ShowQuota,Credits=s.ShowCredits,Resets=s.ShowResets,Yesterday=s.ShowYesterdayTokens,Total=s.ShowTotalTokens,Upload=s.ShowUpload,Download=s.ShowDownload,Cpu=s.ShowCpu,Memory=s.ShowMemory,Gpu=s.ShowGpu,Disk=s.ShowDisk};}
    }
    public sealed class SystemUsage {
        public double? Upload,Download,Cpu,Memory,Gpu,Disk;
        public DateTime ReadAt;public double SampleMilliseconds;public int GpuInstances,DiskInstances,NetworkAdapters;
        public string NetworkSource="";
        public static double? CpuPercent(ulong idle,ulong kernel,ulong user,ulong oldIdle,ulong oldKernel,ulong oldUser){
            if(idle<oldIdle||kernel<oldKernel||user<oldUser)return null;
            double total=(double)(kernel-oldKernel)+(user-oldUser);double wait=idle-oldIdle;
            return total<=0||wait>total?null:(double?)Math.Max(0,Math.Min(100,(total-wait)*100/total));
        }
        public static double? Rate(long current,long previous,double seconds){return seconds>0&&current>=previous?(double?)((current-previous)/seconds):null;}
        public static string Speed(double? bytes){
            if(!bytes.HasValue||double.IsNaN(bytes.Value)||double.IsInfinity(bytes.Value)||bytes<0)return "—";
            double value=bytes.Value/1024;string unit="KB/s";if(value>=1024){value/=1024;unit="MB/s";}if(value>=1024){value/=1024;unit="GB/s";}
            return value.ToString(value>=100?"0":value>=10?"0.0":"0.00",CultureInfo.InvariantCulture)+" "+unit;
        }
        public static string Percent(double? value){return !value.HasValue||double.IsNaN(value.Value)||double.IsInfinity(value.Value)?"—":Math.Max(0,Math.Min(100,value.Value)).ToString("0",CultureInfo.InvariantCulture)+" %";}
        public static double? BusiestGpu(IEnumerable<KeyValuePair<string,double>> values){
            var engines=new Dictionary<string,double>();
            foreach(var item in values){if(double.IsNaN(item.Value)||double.IsInfinity(item.Value)||item.Value<0)continue;int start=item.Key.IndexOf("luid_",StringComparison.OrdinalIgnoreCase);int engine=item.Key.IndexOf("_eng_",StringComparison.OrdinalIgnoreCase);if(start<0||engine<start)continue;string key=item.Key.Substring(start);double previous;engines.TryGetValue(key,out previous);engines[key]=previous+item.Value;}
            return engines.Count==0?null:(double?)Math.Min(100,engines.Values.Max());
        }
        public string Detail(TaskbarSelection choice){var lines=new List<string> {"本机系统指标 · 每秒采样"};
            if(choice.Upload)lines.Add("上传："+Speed(Upload));if(choice.Download)lines.Add("下载："+Speed(Download));
            if(choice.Cpu)lines.Add("CPU 使用率："+Percent(Cpu));if(choice.Memory)lines.Add("物理内存使用率："+Percent(Memory));
            if(choice.Gpu)lines.Add("显卡（最忙引擎）："+Percent(Gpu));if(choice.Disk)lines.Add("硬盘（最忙物理盘活动时间）："+Percent(Disk));
            if(choice.Upload||choice.Download)lines.Add("网速：活动以太网 / Wi-Fi，排除已识别的虚拟网卡；KB=1024 字节。");
            lines.Add("— 表示正在采样或系统未提供数据；不把未知当作 0。");return string.Join("\n",lines);
        }
    }
    sealed class SystemSampler:IDisposable {
        ulong oldIdle,oldKernel,oldUser;bool cpuReady;
        long previousTick;DateTime discovered=DateTime.MinValue;
        NetworkInterface[] adapters=new NetworkInterface[0];readonly Dictionary<string,long[]> previousNetwork=new Dictionary<string,long[]>();
        IntPtr query,gpu,disk;bool queryGpu,queryDisk;
        long lastSampleTick;
        static bool Physical(NetworkInterface adapter){string text=(adapter.Name+" "+adapter.Description).ToLowerInvariant();return adapter.OperationalStatus==OperationalStatus.Up&&(adapter.NetworkInterfaceType==NetworkInterfaceType.Ethernet||adapter.NetworkInterfaceType==NetworkInterfaceType.Wireless80211)&&!new[]{"virtual","虚拟","hyper-v","vethernet","vmware","vbox","vpn","tap-","wireguard","loopback"}.Any(text.Contains);}
        void Configure(TaskbarSelection choice){if(query!=IntPtr.Zero&&queryGpu==choice.Gpu&&queryDisk==choice.Disk)return;CloseQuery();queryGpu=choice.Gpu;queryDisk=choice.Disk;
            if((choice.Gpu||choice.Disk)&&Native.PdhOpenQuery(null,UIntPtr.Zero,out query)==0){if(choice.Gpu&&Native.PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",UIntPtr.Zero,out gpu)!=0)gpu=IntPtr.Zero;if(choice.Disk&&Native.PdhAddEnglishCounter(query,@"\PhysicalDisk(*)\% Idle Time",UIntPtr.Zero,out disk)!=0)disk=IntPtr.Zero;}
        }
        public SystemUsage Sample(TaskbarSelection choice){var watch=Stopwatch.StartNew();var result=new SystemUsage {ReadAt=DateTime.Now};long now=Stopwatch.GetTimestamp();if(lastSampleTick!=0&&(now-lastSampleTick)/(double)Stopwatch.Frequency>3){cpuReady=false;previousNetwork.Clear();previousTick=0;CloseQuery();}lastSampleTick=now;
            if(choice.Cpu){Native.FileTime idle,kernel,user;if(Native.GetSystemTimes(out idle,out kernel,out user)){if(cpuReady)result.Cpu=SystemUsage.CpuPercent(idle.Value,kernel.Value,user.Value,oldIdle,oldKernel,oldUser);oldIdle=idle.Value;oldKernel=kernel.Value;oldUser=user.Value;cpuReady=true;}}else cpuReady=false;
            if(choice.Memory){var memory=new Native.MemoryStatus {Length=(uint)Marshal.SizeOf(typeof(Native.MemoryStatus))};if(Native.GlobalMemoryStatusEx(ref memory)&&memory.TotalPhysical>0)result.Memory=(memory.TotalPhysical-memory.AvailablePhysical)*100.0/memory.TotalPhysical;}
            if(choice.Upload||choice.Download){try{
                if((DateTime.UtcNow-discovered).TotalSeconds>=10){adapters=NetworkInterface.GetAllNetworkInterfaces().Where(Physical).ToArray();discovered=DateTime.UtcNow;}
                long tick=Stopwatch.GetTimestamp();double seconds=previousTick==0?0:(tick-previousTick)/(double)Stopwatch.Frequency;previousTick=tick;double upload=0,download=0;bool valid=adapters.Length==0;var active=new HashSet<string>();
                foreach(var adapter in adapters){try{var stats=adapter.GetIPv4Statistics();long[] old;active.Add(adapter.Id);if(previousNetwork.TryGetValue(adapter.Id,out old)){var up=SystemUsage.Rate(stats.BytesSent,old[0],seconds);var down=SystemUsage.Rate(stats.BytesReceived,old[1],seconds);if(up.HasValue&&down.HasValue){upload+=up.Value;download+=down.Value;valid=true;}}previousNetwork[adapter.Id]=new[]{stats.BytesSent,stats.BytesReceived};}catch{previousNetwork.Remove(adapter.Id);}}
                foreach(var key in previousNetwork.Keys.Where(k=>!active.Contains(k)).ToArray())previousNetwork.Remove(key);
                result.NetworkAdapters=active.Count;result.NetworkSource=string.Join(", ",adapters.Select(a=>a.Name));if(valid){result.Upload=upload;result.Download=download;}
            }catch{previousNetwork.Clear();previousTick=0;discovered=DateTime.MinValue;}}else{previousNetwork.Clear();previousTick=0;}
            try{Configure(choice);if(query!=IntPtr.Zero){Native.PdhCollectQueryData(query);var gpuValues=Read(gpu);result.GpuInstances=gpuValues.Count;if(choice.Gpu)result.Gpu=SystemUsage.BusiestGpu(gpuValues);var disks=Read(disk).Where(p=>p.Key!="_Total").ToArray();result.DiskInstances=disks.Length;if(choice.Disk&&disks.Length>0)result.Disk=disks.Max(p=>Math.Max(0,100-Math.Min(100,p.Value)));}}catch{CloseQuery();}
            result.SampleMilliseconds=watch.Elapsed.TotalMilliseconds;return result;
        }
        static List<KeyValuePair<string,double>> Read(IntPtr counter){var result=new List<KeyValuePair<string,double>>();if(counter==IntPtr.Zero)return result;uint bytes=0,count=0;const uint format=0x200|0x8000;
            uint status=Native.PdhGetFormattedCounterArray(counter,format,ref bytes,out count,IntPtr.Zero);if(status!=0x800007D2||bytes==0||bytes>16*1024*1024)return result;var buffer=Marshal.AllocHGlobal((int)bytes);
            try{if(Native.PdhGetFormattedCounterArray(counter,format,ref bytes,out count,buffer)!=0)return result;int size=Marshal.SizeOf(typeof(Native.CounterItem));if((ulong)count*(uint)size>bytes)return result;for(int i=0;i<count;i++){var item=(Native.CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(Native.CounterItem));if((item.Value.Status==0||item.Value.Status==1)&&!double.IsNaN(item.Value.Number)&&!double.IsInfinity(item.Value.Number)&&item.Value.Number>=0)result.Add(new KeyValuePair<string,double>(Marshal.PtrToStringUni(item.Name),item.Value.Number));}}finally{Marshal.FreeHGlobal(buffer);}return result;
        }
        void CloseQuery(){if(query!=IntPtr.Zero)Native.PdhCloseQuery(query);query=gpu=disk=IntPtr.Zero;}
        public void Dispose(){CloseQuery();}
        static class Native {
            [StructLayout(LayoutKind.Sequential)] internal struct FileTime {public uint Low,High;public ulong Value {get{return ((ulong)High<<32)|Low;}}}
            [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus {public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtended;}
            [StructLayout(LayoutKind.Explicit,Size=16)] internal struct CounterValue {[FieldOffset(0)] public uint Status;[FieldOffset(8)] public double Number;}
            [StructLayout(LayoutKind.Sequential)] internal struct CounterItem {public IntPtr Name;public CounterValue Value;}
            [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
            [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
            [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhOpenQueryW")] internal static extern uint PdhOpenQuery(string source,UIntPtr data,out IntPtr query);
            [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")] internal static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
            [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(IntPtr query);
            [DllImport("pdh.dll",EntryPoint="PdhGetFormattedCounterArrayW")] internal static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint bytes,out uint count,IntPtr buffer);
            [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(IntPtr query);
        }
    }
}
