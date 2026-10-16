using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CodexQuotaMonitor {
    // Account service totals are authoritative. Missing daily buckets never mean zero.
    public sealed class TokenUsage {
        public long? LifetimeTokens;
        public Dictionary<string,long> Daily=new Dictionary<string,long>();
        public DateTime ReadAt;
        public static string Yesterday {get{return PreviousDate(DateTimeOffset.UtcNow);}}
        public static string PreviousDate(DateTimeOffset now){return TimeZoneInfo.ConvertTimeFromUtc(now.UtcDateTime,TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        public string LatestDate {get{return Daily.Keys.OrderByDescending(x=>x,StringComparer.Ordinal).FirstOrDefault();}}
        public long? ForDate(string date){long value;return Daily.TryGetValue(date,out value)?(long?)value:null;}
        public static long? Count(object root,string key){long value;return long.TryParse(Json.Text(root,key),NumberStyles.Integer,CultureInfo.InvariantCulture,out value)&&value>=0?(long?)value:null;}
        public static TokenUsage Parse(object root){
            if(Json.Get(root,"summary")==null)throw new InvalidOperationException("USAGE_INVALID");
            var usage=new TokenUsage {LifetimeTokens=Count(Json.Get(root,"summary"),"lifetimeTokens"),ReadAt=DateTime.Now};
            foreach(var bucket in Json.Array(Json.Get(root,"dailyUsageBuckets"))){
                string date=Json.Text(bucket,"startDate");DateTime parsed;var count=Count(bucket,"tokens");
                if(date!=null&&DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed)&&count.HasValue){
                    // Identical duplicate rows are harmless; conflicting rows are unknown.
                    long existing;if(usage.Daily.TryGetValue(date,out existing)&&existing!=count.Value)throw new InvalidOperationException("USAGE_CONFLICT");
                    usage.Daily[date]=count.Value;
                }
            }
            return usage;
        }
        public static async Task RefreshAsync(Snapshot snapshot,Func<Task<object>> fetch){
            snapshot.UsageStale=true;
            try {snapshot.Usage=Parse(await fetch());snapshot.UsageStale=false;}catch { /* Quota data and last valid usage remain usable independently. */ }
        }
        public static string Full(long? value){return value.HasValue?value.Value.ToString("N0",CultureInfo.InvariantCulture):"暂不可用";}
        public static string Compact(long? value){
            if(!value.HasValue)return "—";
            decimal count=value.Value;
            if(count<10000m)return count.ToString("0",CultureInfo.InvariantCulture);
            decimal divisor=count>=1000000000000m?1000000000000m:count>=100000000m?100000000m:10000m;
            decimal rounded=Math.Round(count/divisor,2,MidpointRounding.AwayFromZero);
            // Promote a rounded boundary so 99,999,999 is shown as about 1亿, not 10000万.
            if(rounded>=10000m&&divisor<1000000000000m){divisor*=10000m;rounded=Math.Round(count/divisor,2,MidpointRounding.AwayFromZero);}
            string unit=divisor==10000m?"万":divisor==100000000m?"亿":"万亿";
            return (rounded*divisor==count?"":"≈")+rounded.ToString("0.##",CultureInfo.InvariantCulture)+unit;
        }
        public string Detail(bool stale,string yesterday){
            var current=ForDate(yesterday);string latest=LatestDate;
            return "账户 token 活动 · 官方统计\n历史累计："+Full(LifetimeTokens)+" token\n昨日（"+yesterday+"）："+(current.HasValue?Full(current)+" token":"官方暂未返回该日数据")+
                (latest==null?"\n每日数据暂不可用":"\n最新日期："+latest+" · "+Full(ForDate(latest))+" token")+
                "\n"+(stale?"Token 更新失败，以上为旧数据；上次成功 ":"Token 读取于 ")+ReadAt.ToString("M月d日 HH:mm:ss")+
                "\n昨日按北京时间前一天的日期匹配；每日分桶与更新延迟以官方服务为准。\n显示的 ≈ 表示四舍五入；本提示保留完整统计值。\n1万=10,000；1亿=100,000,000；1万亿=1,000,000,000,000。";
        }
    }
}
