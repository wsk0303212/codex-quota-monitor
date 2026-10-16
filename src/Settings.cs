using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;

namespace CodexQuotaMonitor {
    public class Settings {
        public int Interval=5,Warning=20,Critical=10;
        public bool Topmost=false,Notify=true,Mini=false;
        public double Scale=0.75;
        public bool DockBottomRight=true;
        public bool TaskbarMode=false;
        public bool TaskbarLeft=true;
        public bool ShowQuota=true,ShowCredits=true,ShowResets=true,ShowYesterdayTokens=true,ShowTotalTokens=true;
        public bool ShowUpload=true,ShowDownload=true,ShowCpu=true,ShowMemory=true,ShowGpu=true,ShowDisk=true;
        public string Theme="light",Executable=null;
        public double? Left=null,Top=null;
        public string[] Alerts=new string[0];
        public Dictionary<string,long> ResetAnchors=new Dictionary<string,long>();
        public static string DirectoryPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaMonitor");}}
        public static string FilePath {get{return Path.Combine(DirectoryPath,"settings.json");}}
        public static double NormalizeScale(double scale){return double.IsNaN(scale)||double.IsInfinity(scale)?0.75:Math.Max(0.5,Math.Min(1.2,Math.Round(scale*20)/20));}
        public static Settings Load(){try{if(File.Exists(FilePath)) {var settings=Json.Serializer.Deserialize<Settings>(File.ReadAllText(FilePath));settings.Interval=new[]{1,5,15}.ContainsValue(settings.Interval)?settings.Interval:5;settings.Scale=NormalizeScale(settings.Scale);settings.Warning=Math.Max(1,Math.Min(100,settings.Warning));settings.Critical=Math.Max(0,Math.Min(settings.Warning-1,settings.Critical));if(settings.Alerts==null)settings.Alerts=new string[0];return settings;}}catch{}return new Settings();}
        public void Save(){Directory.CreateDirectory(DirectoryPath);var tmp=FilePath+".tmp";File.WriteAllText(tmp,Json.Encode(this));if(File.Exists(FilePath))File.Replace(tmp,FilePath,FilePath+".bak");else File.Move(tmp,FilePath);}
        public static bool AutoStart {get{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){return key!=null&&key.GetValue("CodexQuotaMonitor")!=null;}}}
        public static void SetAutoStart(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)key.SetValue("CodexQuotaMonitor","\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --tray");else key.DeleteValue("CodexQuotaMonitor",false);}}
    }
    static class Values { public static bool ContainsValue<T>(this T[] values,T value){return System.Array.IndexOf(values,value)>=0;} }
}
