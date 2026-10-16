using System.Collections.Generic;
using System.Linq;

namespace CodexQuotaMonitor {
    sealed class TaskbarColumn {public string Header,Value;public int Width;public bool Bold,QuotaWarning;}
    static class TaskbarColumns {
        public const int Gap=4;
        public static List<TaskbarColumn> Create(TaskbarContent data,SystemUsage usage,TaskbarSelection s){var columns=new List<TaskbarColumn>();
            var metrics=new List<KeyValuePair<string,int>>();usage=usage??new SystemUsage();
            if(s.Upload)metrics.Add(new KeyValuePair<string,int>("↑ "+SystemUsage.Speed(usage.Upload),76));if(s.Download)metrics.Add(new KeyValuePair<string,int>("↓ "+SystemUsage.Speed(usage.Download),76));
            if(s.Cpu)metrics.Add(new KeyValuePair<string,int>("CPU: "+SystemUsage.Percent(usage.Cpu),72));if(s.Memory)metrics.Add(new KeyValuePair<string,int>("内存: "+SystemUsage.Percent(usage.Memory),72));
            if(s.Gpu)metrics.Add(new KeyValuePair<string,int>("显卡: "+SystemUsage.Percent(usage.Gpu),72));if(s.Disk)metrics.Add(new KeyValuePair<string,int>("硬盘: "+SystemUsage.Percent(usage.Disk),72));
            for(int i=0;i<metrics.Count;i+=2)columns.Add(new TaskbarColumn {Header=metrics[i].Key,Value=i+1<metrics.Count?metrics[i+1].Key:"",Width=i+1<metrics.Count?System.Math.Max(metrics[i].Value,metrics[i+1].Value):metrics[i].Value});
            if(s.Quota)columns.Add(new TaskbarColumn {Header=data.Plan,Value=data.Quota,Width=86,Bold=true,QuotaWarning=data.Warning});
            if(s.Credits)columns.Add(new TaskbarColumn {Header=data.CreditsLabel,Value=data.CreditsValue,Width=60});
            if(s.Resets)columns.Add(new TaskbarColumn {Header=data.ResetLabel,Value=data.ResetExpiry,Width=100});
            if(s.Yesterday||s.Total)columns.Add(new TaskbarColumn {Header=s.Yesterday?data.YesterdayTokens:data.TotalTokens,Value=s.Yesterday&&s.Total?data.TotalTokens:"",Width=100});
            return columns;
        }
        public static int Width(List<TaskbarColumn> columns){return columns.Count==0?0:8+columns.Sum(c=>c.Width)+Gap*(columns.Count-1);}
    }
}
