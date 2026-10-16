using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace CodexQuotaMonitor {
    public static class Json {
        public static JavaScriptSerializer Serializer {get{return new JavaScriptSerializer { MaxJsonLength = 2097152 };}}
        public static Dictionary<string,object> Object(object value) { return value as Dictionary<string,object> ?? new Dictionary<string,object>(); }
        public static object Get(object obj, string key) { object value; return Object(obj).TryGetValue(key, out value) ? value : null; }
        public static string Text(object obj, string key) { var value=Get(obj,key); return value == null ? null : Convert.ToString(value,CultureInfo.InvariantCulture); }
        public static double? Number(object obj,string key) { double n; return double.TryParse(Text(obj,key),NumberStyles.Float,CultureInfo.InvariantCulture,out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? (double?)n : null; }
        public static IEnumerable<object> Array(object obj) { return (obj as IEnumerable ?? new object[0]).Cast<object>(); }
        public static Dictionary<string,object> Parse(string value) { return Object(Serializer.DeserializeObject(value)); }
        public static string Encode(object value) { return Serializer.Serialize(value); }
    }
    public class QuotaWindow {
        public string Bucket, BucketName, Slot;
        public double? Remaining;
        public double? Minutes;
        public long? ResetsAt;
        public string Label { get { if(Minutes == 10080) return "周额度"; if(Minutes == 300) return "5 小时额度"; if(Minutes == 1440) return "日额度"; return Minutes.HasValue ? (Minutes.Value % 60 == 0 ? (Minutes.Value/60).ToString("0.#")+" 小时额度" : Minutes.Value.ToString("0")+" 分钟额度") : "额度窗口"; } }
        public string Key { get { return Bucket+"/"+Slot+"/"+ResetsAt; } }
        public string Percent { get { return Remaining.HasValue ? Remaining.Value.ToString("0.#",CultureInfo.InvariantCulture)+"%" : "暂不可用"; } }
    }
    public class Snapshot {
        public string Plan;
        public List<QuotaWindow> Windows = new List<QuotaWindow>();
        public string Credits;
        public int? Resets;
        public List<long?> ResetExpirations = new List<long?>();
        public DateTime ReadAt;
        public TokenUsage Usage;
        public bool UsageStale;
        public string Title { get { switch((Plan??"").ToLowerInvariant()) {case "pro":return "ChatGPT Pro";case "plus":return "ChatGPT Plus";case "free":return "ChatGPT Free";case "team":return "ChatGPT Team";case "business":return "ChatGPT Business";case "enterprise":return "ChatGPT Enterprise";case "edu":return "ChatGPT Edu";default:return string.IsNullOrWhiteSpace(Plan)?"订阅套餐暂不可用":"ChatGPT "+Plan;} } }
        public static string Time(long? seconds) { if(!seconds.HasValue)return "暂不可用"; try{return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).ToLocalTime().ToString("M月d日 HH:mm");}catch{return "暂不可用";} }
        public static Snapshot Parse(object root, string accountPlan) {
            var result=new Snapshot {Plan=accountPlan,ReadAt=DateTime.Now};
            var buckets=Json.Object(Json.Get(root,"rateLimitsByLimitId"));
            if(buckets.Count==0) {var legacy=Json.Get(root,"rateLimits");if(legacy!=null)buckets[Json.Text(legacy,"limitId")??"codex"]=legacy;}
            foreach(var pair in buckets.OrderBy(p=>p.Key=="codex"?0:1).ThenBy(p=>p.Key)) {
                var bucket=pair.Value;
                if(string.IsNullOrEmpty(result.Plan))result.Plan=Json.Text(bucket,"planType");
                foreach(var slot in new[]{"primary","secondary"}) {
                    var window=Json.Get(bucket,slot);if(window==null)continue;
                    var used=Json.Number(window,"usedPercent");var resets=Json.Number(window,"resetsAt");
                    result.Windows.Add(new QuotaWindow {Bucket=pair.Key,BucketName=Json.Text(bucket,"limitName"),Slot=slot,Minutes=Json.Number(window,"windowDurationMins"),Remaining=used.HasValue?(double?)Math.Max(0,Math.Min(100,100-used.Value)):null,ResetsAt=resets.HasValue?(long?)resets.Value:null});
                }
                if(pair.Key=="codex" || result.Credits==null) {
                    var credits=Json.Get(bucket,"credits");decimal balance;
                    if(Json.Get(credits,"unlimited") is bool && (bool)Json.Get(credits,"unlimited"))result.Credits="不限额";
                    else if(decimal.TryParse(Json.Text(credits,"balance"),NumberStyles.Float,CultureInfo.InvariantCulture,out balance))result.Credits=balance.ToString("N2",CultureInfo.InvariantCulture);
                }
            }
            var resetCredits=Json.Get(root,"rateLimitResetCredits");var count=Json.Number(resetCredits,"availableCount");
            if(count.HasValue)result.Resets=(int)Math.Max(0,count.Value);
            foreach(var credit in Json.Array(Json.Get(resetCredits,"credits"))) { if(Json.Text(credit,"status")!="available")continue;var expiry=Json.Number(credit,"expiresAt");result.ResetExpirations.Add(expiry.HasValue?(long?)expiry.Value:null); }
            return result;
        }
    }
    public class AlertState {
        public HashSet<string> Seen = new HashSet<string>();
        public Dictionary<string,long> ResetAnchors=new Dictionary<string,long>();
        public List<string> Update(Snapshot previous,Snapshot current,string accountKey,int warning,int critical) {
            var notices=new List<string>();
            foreach(var window in current.Windows) {
                if(!window.Remaining.HasValue || !window.ResetsAt.HasValue)continue;
                var anchorKey=accountKey+"/"+window.Bucket+"/"+window.Slot;long anchor;
                if(!ResetAnchors.TryGetValue(anchorKey,out anchor)||Math.Abs(anchor-window.ResetsAt.Value)>120){anchor=window.ResetsAt.Value;ResetAnchors[anchorKey]=anchor;}
                var prefix=anchorKey+"/"+anchor+"/";
                var old=previous==null?null:previous.Windows.FirstOrDefault(w=>w.Bucket==window.Bucket&&w.Slot==window.Slot);
                if(old!=null && old.Remaining.HasValue && old.Remaining.Value<=0 && window.Remaining.Value>critical && Seen.Add(prefix+"restored"))notices.Add(window.Label+"已恢复，剩余 "+window.Percent);
                int level=window.Remaining.Value<=0?0:window.Remaining.Value<=critical?critical:window.Remaining.Value<=warning?warning:-1;
                if(level<0)continue;
                bool fresh=Seen.Add(prefix+level);
                if(level<=critical)Seen.Add(prefix+warning);
                if(level==0)Seen.Add(prefix+critical);
                if(fresh)notices.Add(window.Label+(level==0?"已用完；请查看 Credits 与恢复时间。":"剩余 "+window.Percent+"，请留意恢复时间。"));
            }
            if(Seen.Count>1024)Seen=new HashSet<string>(Seen.OrderBy(s=>s).Take(512));
            return notices;
        }
    }
}
