using System;
using System.Linq;

namespace CodexQuotaMonitor {
    // Keep quota restoration and reset-credit expiration as separate source fields.
    public sealed class TaskbarContent {
        public string Plan,Quota,CreditsLabel,CreditsValue,ResetLabel,ResetExpiry,Tooltip,YesterdayTokens,TotalTokens;
        public bool Warning,Stale,TokensStale;
        public string FirstLine {get{return Plan+" · "+CreditsLabel+" · "+ResetLabel+" · "+YesterdayTokens;}}
        public string SecondLine {get{return Quota+" · "+CreditsValue+" · "+ResetExpiry+" · "+TotalTokens;}}
        public static TaskbarContent Create(Snapshot snapshot,bool stale,int threshold=20){
            var content=new TaskbarContent {Plan="Codex",Quota="额度暂不可用",CreditsLabel="Credits",CreditsValue="—",ResetLabel="重置暂不可用",ResetExpiry="到期暂不可用",YesterdayTokens="昨日 —",TotalTokens="累计 —",Stale=stale,Warning=stale};
            if(snapshot==null){if(stale)content.Plan="更新失败";content.Tooltip=(stale?"更新失败，":"")+"额度暂不可用；点击查看详情";return content;}
            var windows=snapshot.Windows.Where(w=>w.Bucket=="codex").ToList();if(windows.Count==0)windows=snapshot.Windows;
            var main=windows.FirstOrDefault();
            content.Plan=stale?"更新失败":snapshot.Title;
            content.Quota=stale?"上次额度 "+(main==null?"—":main.Percent):main==null?"额度暂不可用":main.Label.Replace("额度","")+"剩余 "+main.Percent;
            content.CreditsLabel=stale?"旧 Credits":"Credits";content.CreditsValue=snapshot.Credits??"暂不可用";
            content.ResetLabel=(stale?"旧重置 ":"重置 ")+(snapshot.Resets.HasValue?snapshot.Resets+" 次":"暂不可用");
            content.ResetExpiry=Expiry(snapshot);
            content.TokensStale=stale||snapshot.UsageStale;
            if(snapshot.Usage!=null){
                var yesterday=snapshot.Usage.ForDate(TokenUsage.Yesterday);
                content.YesterdayTokens=(content.TokensStale?"旧昨日 ":"昨日 ")+(yesterday.HasValue?TokenUsage.Compact(yesterday):"暂未更新");
                content.TotalTokens=(content.TokensStale?"旧累计 ":"累计 ")+TokenUsage.Compact(snapshot.Usage.LifetimeTokens);
            }
            content.Warning=stale||windows.Any(w=>w.Remaining.HasValue&&w.Remaining.Value<=threshold);
            string expires=snapshot.Resets==0?"暂无可用重置":snapshot.ResetExpirations.Count==0?"重置到期：暂不可用":string.Join("\n",snapshot.ResetExpirations.OrderBy(x=>x.HasValue?0:1).ThenBy(x=>x).Select(x=>"重置到期："+Snapshot.Time(x)));
            bool partial=snapshot.Resets.HasValue&&snapshot.Resets>snapshot.ResetExpirations.Count||snapshot.ResetExpirations.Any(x=>!x.HasValue);
            content.Tooltip=snapshot.Title+"\n"+string.Join("\n",snapshot.Windows.Select(w=>(w.BucketName??w.Bucket)+" · "+w.Label+"剩余 "+w.Percent+" · 额度恢复 "+Snapshot.Time(w.ResetsAt)))+"\nCredits "+(snapshot.Credits??"暂不可用")+"\n可用重置："+(snapshot.Resets.HasValue?snapshot.Resets+" 次":"暂不可用")+"\n"+expires+(partial?"\n部分重置到期时间暂未提供。":"")+"\n"+(stale?"更新失败，以上为旧数据；上次成功 ":"更新于 ")+snapshot.ReadAt.ToString("M月d日 HH:mm:ss")+"\n点击打开详情；右键显示菜单";
            content.Tooltip+="\n\n"+(snapshot.Usage==null?"Token 统计暂不可用；额度刷新独立运行。":snapshot.Usage.Detail(content.TokensStale,TokenUsage.Yesterday));
            return content;
        }
        public static string Expiry(Snapshot snapshot){
            if(snapshot.Resets==0)return "暂无可用重置";
            var earliest=snapshot.ResetExpirations.Where(x=>x.HasValue).OrderBy(x=>x).FirstOrDefault();
            if(!earliest.HasValue)return "到期暂不可用";
            try {var local=DateTimeOffset.FromUnixTimeSeconds(earliest.Value).ToLocalTime();return "到期 "+local.ToString(local.Year==DateTime.Now.Year?"MM/dd HH:mm":"yyyy/MM/dd HH:mm");}catch{return "到期暂不可用";}
        }
    }
}
