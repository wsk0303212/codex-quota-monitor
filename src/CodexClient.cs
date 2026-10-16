using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexQuotaMonitor {
    public sealed class CodexClient : IDisposable {
        Process process;
        int serial;
        readonly ConcurrentDictionary<int,TaskCompletionSource<object>> pending=new ConcurrentDictionary<int,TaskCompletionSource<object>>();
        readonly SemaphoreSlim writing=new SemaphoreSlim(1,1);
        public event Action LimitsChanged;
        public event Action AccountChanged;
        public string Executable;
        public static string FindExecutable() {
            var local=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots=new[]{Path.Combine(local,".local","bin","node_modules","@openai","codex","node_modules","@openai","codex-win32-x64","vendor"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai","codex","node_modules","@openai","codex-win32-x64","vendor")};
            foreach(var root in roots)if(Directory.Exists(root)){var found=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).FirstOrDefault();if(found!=null)return found;}
            foreach(var folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{var path=Path.Combine(folder.Trim('"'),"codex.exe");if(File.Exists(path))return path;}catch{}}
            return null;
        }
        public async Task ConnectAsync() {
            if(process!=null && !process.HasExited)return;
            DisposeProcess();
            if(string.IsNullOrEmpty(Executable)||!File.Exists(Executable))throw new InvalidOperationException("CLI_MISSING");
            var p=new Process {StartInfo=new ProcessStartInfo(Executable,"app-server") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory},EnableRaisingEvents=true};
            p.OutputDataReceived+=(s,e)=>Receive(e.Data);
            p.ErrorDataReceived+=(s,e)=>{}; // Drain stderr; never persist raw protocol/auth errors.
            p.Exited+=(s,e)=>FailPending();
            p.Start();process=p;p.BeginOutputReadLine();p.BeginErrorReadLine();
            await CallAsync("initialize",new {clientInfo=new {name="codex_quota_monitor",title="Codex Quota Monitor",version="1.1.0"}});
            await WriteAsync(new {method="initialized",@params=new {}});
        }
        void Receive(string line) {
            if(string.IsNullOrWhiteSpace(line))return;
            try {
                var msg=Json.Parse(line);var id=Json.Number(msg,"id");TaskCompletionSource<object> tcs;
                if(id.HasValue&&pending.TryRemove((int)id.Value,out tcs)) {
                    if(Json.Get(msg,"error")!=null)tcs.TrySetException(new InvalidOperationException("RPC_"+Json.Text(Json.Get(msg,"error"),"code")));
                    else tcs.TrySetResult(Json.Get(msg,"result"));
                } else if(id.HasValue && Json.Text(msg,"method")!=null) { // This read-only client cannot fulfill server-initiated auth/tool requests.
                    var ignored=WriteAsync(new {id=(int)id.Value,error=new {code=-32601,message="Read-only client does not handle this request"}});
                } else if(Json.Text(msg,"method")=="account/rateLimits/updated") {var callback=LimitsChanged;if(callback!=null)callback();}
                else if(Json.Text(msg,"method")=="account/updated") {var callback=AccountChanged;if(callback!=null)callback();}
            }catch{} // Malformed unsolicited messages cannot become UI data.
        }
        async Task WriteAsync(object message) {
            await writing.WaitAsync();try{if(process==null||process.HasExited)throw new IOException("CLI_CLOSED");await process.StandardInput.WriteLineAsync(Json.Encode(message));await process.StandardInput.FlushAsync();}finally{writing.Release();}
        }
        public async Task<object> CallAsync(string method,object parameters=null) {
            // A whitelist prevents accidentally invoking inference, reset, purchase, logout or message APIs.
            if(!new[]{"initialize","account/read","account/rateLimits/read","account/usage/read","account/login/start","account/login/cancel"}.Contains(method))throw new InvalidOperationException("METHOD_NOT_ALLOWED");
            int id=Interlocked.Increment(ref serial);var source=new TaskCompletionSource<object>();pending[id]=source;
            try {
                var msg=new Dictionary<string,object>{{"id",id},{"method",method}};if(parameters!=null)msg["params"]=parameters;
                await WriteAsync(msg);
                if(await Task.WhenAny(source.Task,Task.Delay(25000))!=source.Task){DisposeProcess();throw new TimeoutException("CLI_TIMEOUT");}
                return await source.Task;
            }finally{TaskCompletionSource<object> removed;pending.TryRemove(id,out removed);}
        }
        void FailPending(){foreach(var pair in pending){TaskCompletionSource<object> tcs;if(pending.TryRemove(pair.Key,out tcs))tcs.TrySetException(new IOException("CLI_CLOSED"));}}
        void DisposeProcess(){var p=process;process=null;if(p!=null){try{p.StandardInput.Close();if(!p.WaitForExit(1000)){p.Kill();p.WaitForExit(1000);}}catch{}p.Dispose();}FailPending();}
        public void Reconnect(){DisposeProcess();}
        public void Dispose(){DisposeProcess();}
    }
}
