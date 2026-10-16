using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace CodexQuotaMonitor {
    public class Program {
        [STAThread] public static int Main(string[] args) {
            if(args.Contains("--self-test"))return Tests.Run(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.json"));
            if(args.Contains("--probe"))return Probe().GetAwaiter().GetResult();
            if(args.Contains("--system-probe"))return SystemProbe().GetAwaiter().GetResult();
            bool smoke=args.Contains("--smoke");bool created;
            using(var mutex=new Mutex(true,"Local\\CodexQuotaMonitor."+Environment.UserName+(smoke?".Smoke":""),out created)) {
                if(!created){try{using(var signal=EventWaitHandle.OpenExisting("Local\\CodexQuotaMonitor.Open."+Environment.UserName))signal.Set();}catch{}return 0;}
                var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var monitor=new Monitor(app,smoke,args.Contains("--tray"),args.Contains("--taskbar"));
                app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;if(smoke){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","evidence","smoke-error.txt"),e.Exception.GetType().Name+": "+e.Exception.Message);monitor.Stop();}else{Forms.MessageBox.Show("程序遇到异常，请退出后重新打开。账户登录和数据不会被修改。","额度监控");}};
                app.Run();mutex.ReleaseMutex();return smoke&&monitor.SmokeFailed?1:0;
            }
        }
        static async Task<int> Probe(){try{using(var client=new CodexClient {Executable=CodexClient.FindExecutable()}){await client.ConnectAsync();var account=await client.CallAsync("account/read",new {refreshToken=false});var snapshot=Snapshot.Parse(await client.CallAsync("account/rateLimits/read"),Json.Text(Json.Get(account,"account"),"planType"));await TokenUsage.RefreshAsync(snapshot,()=>client.CallAsync("account/usage/read"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe-result.json"),Json.Encode(snapshot));return 0;}}catch{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe-result.json"),"{\"error\":\"read_failed\"}");return 1;}}
        static async Task<int> SystemProbe(){using(var sampler=new SystemSampler()){var values=new List<SystemUsage>();var choice=TaskbarSelection.From(new Settings());for(int i=0;i<4;i++){values.Add(sampler.Sample(choice));if(i<3)await Task.Delay(1000);}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","evidence","system-samples.json"),Json.Encode(values));return values.Last().Cpu.HasValue&&values.Last().Memory.HasValue?0:1;}}
    }
    public sealed class Monitor {
        readonly Application app;readonly bool smoke;
        readonly Window window;readonly Settings settings;
        readonly CodexClient client;readonly Forms.NotifyIcon tray;
        readonly System.Drawing.Icon applicationIcon;
        readonly TaskbarHost taskbar;readonly Forms.ToolStripMenuItem taskbarItem,taskbarLeftItem;
        readonly DispatcherTimer timer=new DispatcherTimer();
        readonly AlertState alerts;
        readonly DisplayLayout displayLayout=new DisplayLayout();
        Snapshot snapshot;string accountKey="";bool busy,quitting,retryNotification,needsLogin,smokeFixtures;int failures,accountRevision;
        Window settingsWindow;
        EventWaitHandle openSignal;RegisteredWaitHandle openWait;
        public bool SmokeFailed;
        public Monitor(Application application,bool testing,bool startHidden,bool startTaskbar) {
            app=application;smoke=testing;settings=smoke?new Settings {ShowUpload=false,ShowDownload=false,ShowCpu=false,ShowMemory=false,ShowGpu=false,ShowDisk=false}:Settings.Load();alerts=new AlertState {Seen=new HashSet<string>(settings.Alerts),ResetAnchors=settings.ResetAnchors??new Dictionary<string,long>()};
            if(startTaskbar&&!smoke)settings.TaskbarMode=true;
            using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(stream);
            using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.ico")){
                window.Icon=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame=>frame.PixelWidth).First();
                stream.Position=0;using(var icon=new System.Drawing.Icon(stream,16,16))applicationIcon=(System.Drawing.Icon)icon.Clone();
            }
            client=new CodexClient {Executable=string.IsNullOrWhiteSpace(settings.Executable)?CodexClient.FindExecutable():settings.Executable};
            client.LimitsChanged+=()=>window.Dispatcher.BeginInvoke(new Action(()=>{if(smoke&&smokeFixtures)return;if(busy)retryNotification=true;else Refresh(false);}));
            client.AccountChanged+=()=>window.Dispatcher.BeginInvoke(new Action(()=>{if(smoke&&smokeFixtures)return;accountRevision++;snapshot=null;accountKey="";Render();if(busy)retryNotification=true;else Refresh(false);}));
            tray=new Forms.NotifyIcon {Icon=applicationIcon,Text="Codex 额度监控",Visible=true};
            tray.DoubleClick+=(s,e)=>Show();var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("打开",null,(s,e)=>Show());menu.Items.Add("刷新额度",null,(s,e)=>Refresh(true));menu.Items.Add("设置",null,(s,e)=>OpenSettings());menu.Items.Add("退出",null,(s,e)=>Quit());tray.ContextMenuStrip=menu;
            menu.Items.Insert(3,new Forms.ToolStripMenuItem("放到右下角",null,(s,e)=>{settings.DockBottomRight=true;RestorePosition();Save();Show();}));
            taskbarItem=new Forms.ToolStripMenuItem("任务栏显示",null,(s,e)=>SetTaskbarMode(!settings.TaskbarMode));menu.Items.Insert(3,taskbarItem);taskbar=new TaskbarHost(Show,menu);
            taskbar.SetSelection(TaskbarSelection.From(settings));
            taskbarLeftItem=new Forms.ToolStripMenuItem("任务栏靠左",null,async(s,e)=>{settings.TaskbarLeft=!settings.TaskbarLeft;taskbarLeftItem.Checked=settings.TaskbarLeft;taskbar.PreferLeft=settings.TaskbarLeft;Save();await taskbar.UpdatePlacement();});menu.Items.Insert(4,taskbarLeftItem);
            Button("PinButton").Click+=(s,e)=>{settings.Topmost=!settings.Topmost;window.Topmost=settings.Topmost;UpdatePin();Save();};
            Button("SettingsButton").Click+=(s,e)=>OpenSettings();Button("HideButton").Click+=(s,e)=>window.Hide();
            Button("RefreshButton").Click+=(s,e)=>{if(needsLogin)Login();else Refresh(true);};
            Button("CollapseButton").Click+=(s,e)=>SetMini(true);Button("ExpandButton").Click+=(s,e)=>SetMini(false);
            Button("ResetDetailsButton").Click+=(s,e)=>Element<StackPanel>("ResetDetails").Visibility=Element<StackPanel>("ResetDetails").Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            Element<Grid>("Header").ToolTip="拖动可移动窗口；Ctrl + 滚轮按比例缩放";
            Text("PlanTitle").MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed){try{settings.DockBottomRight=false;window.DragMove();SavePosition();}catch{}}};
            window.Closing+=(s,e)=>{if(!quitting){e.Cancel=true;window.Hide();SavePosition();}};
            window.LocationChanged+=(s,e)=>{if(window.IsLoaded){settings.Left=window.Left;settings.Top=window.Top;}};
            window.KeyDown+=(s,e)=>{if(e.Key==Key.Escape)window.Hide();if(e.Key==Key.F5)Refresh(true);if((Keyboard.Modifiers&ModifierKeys.Control)!=0){if(e.Key==Key.OemPlus||e.Key==Key.Add){SetScale(settings.Scale+0.05);e.Handled=true;}else if(e.Key==Key.OemMinus||e.Key==Key.Subtract){SetScale(settings.Scale-0.05);e.Handled=true;}}};
            window.PreviewMouseWheel+=(s,e)=>{if((Keyboard.Modifiers&ModifierKeys.Control)!=0){SetScale(settings.Scale+(e.Delta>0?0.05:-0.05));e.Handled=true;}};
            window.Topmost=settings.Topmost;SetMini(settings.Mini);ApplyTheme();UpdatePin();
            if(!smoke){openSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexQuotaMonitor.Open."+Environment.UserName);openWait=ThreadPool.RegisterWaitForSingleObject(openSignal,(s,t)=>window.Dispatcher.BeginInvoke(new Action(Show)),null,-1,false);}
            timer.Tick+=(s,e)=>Refresh(false);timer.Interval=TimeSpan.FromMinutes(settings.Interval);timer.Start();
            window.Loaded+=async(s,e)=>{if(smoke)await Smoke();else{SetTaskbarMode(settings.TaskbarMode);if(startHidden||settings.TaskbarMode)window.Hide();Refresh(false);}};
            window.Show();
        }
        T Element<T>(string name) where T:class{return window.FindName(name) as T;}
        Button Button(string name){return Element<Button>(name);}
        TextBlock Text(string name){return Element<TextBlock>(name);}
        double PixelTolerance {get{var source=PresentationSource.FromVisual(window);return 1/(source==null||source.CompositionTarget==null?1:source.CompositionTarget.TransformToDevice.M11)+0.00001;}}
        void Save(){if(smoke)return;try{settings.Alerts=alerts.Seen.ToArray();settings.ResetAnchors=alerts.ResetAnchors;settings.Save();}catch{Text("UpdatedText").Text="设置未能保存；当前会话仍可使用";}}
        void SavePosition(){settings.Left=window.Left;settings.Top=window.Top;Save();}
        void UpdatePin(){Button("PinButton").ToolTip=settings.Topmost?"取消置顶":"置顶";Button("PinButton").Opacity=settings.Topmost?1:0.6;}
        void SetMini(bool mini){settings.Mini=mini;Element<Grid>("DetailPanel").Visibility=mini?Visibility.Collapsed:Visibility.Visible;Element<StackPanel>("MiniPanel").Visibility=mini?Visibility.Visible:Visibility.Collapsed;ApplyWindowSize();Save();}
        void SetScale(double scale){settings.Scale=Settings.NormalizeScale(scale);ApplyWindowSize();Save();}
        void ApplyWindowSize(){settings.Scale=Settings.NormalizeScale(settings.Scale);displayLayout.Apply(window,settings.Scale,settings.Mini);RestorePosition();}
        void RestorePosition(){var work=SystemParameters.WorkArea;double right=Math.Max(work.Left,work.Right-window.Width-16),bottom=Math.Max(work.Top,work.Bottom-window.Height-16);if(settings.DockBottomRight){window.Left=right;window.Top=bottom;return;}double left=settings.Left??right,top=settings.Top??bottom;var rect=new System.Drawing.Rectangle((int)left,(int)top,(int)Math.Ceiling(window.Width),(int)Math.Ceiling(window.Height));bool visible=Forms.Screen.AllScreens.Any(screen=>screen.WorkingArea.Contains(rect));window.Left=visible?left:right;window.Top=visible?top:bottom;}
        void Show(){window.Show();window.Activate();RestorePosition();ApplyTheme();}
        void SetTaskbarMode(bool enabled){settings.TaskbarMode=enabled;taskbarItem.Checked=enabled;taskbarLeftItem.Checked=settings.TaskbarLeft;taskbar.PreferLeft=settings.TaskbarLeft;taskbar.Enable(enabled);taskbar.Render(snapshot,failures>0,settings.Warning);Save();if(enabled)window.Hide();else Show();}
        void Quit(){if(quitting)return;quitting=true;timer.Stop();taskbar.Dispose();if(openWait!=null)openWait.Unregister(null);if(openSignal!=null)openSignal.Dispose();SavePosition();if(settingsWindow!=null)settingsWindow.Close();client.Dispose();tray.Visible=false;tray.Dispose();applicationIcon.Dispose();window.Close();app.Shutdown();}
        public void Stop(){if(smoke)SmokeFailed=true;Quit();}
        void Schedule(){timer.Stop();timer.Interval=TimeSpan.FromMinutes(Math.Min(30,settings.Interval*Math.Pow(2,Math.Min(failures,3))));if(!quitting)timer.Start();}
        async void Refresh(bool manual){await RefreshAsync(manual);}
        async Task RefreshAsync(bool manual){
            if(busy||quitting)return;busy=true;Button("RefreshButton").IsEnabled=false;Text("RefreshLabel").Text="正在读取…";
            try {
                await client.ConnectAsync();int readRevision=accountRevision;var root=await client.CallAsync("account/read",new {refreshToken=false});
                if(readRevision!=accountRevision||quitting)return;
                var account=Json.Get(root,"account");
                if(account==null || Json.Text(account,"type")!="chatgpt") {snapshot=null;needsLogin=true;Render();Text("StatusText").Text=account==null?"需要登录 Codex，才能查看订阅额度。":"当前 CLI 使用 API Key 登录，无法读取订阅额度。";Text("UpdatedText").Text="订阅额度暂不可用";return;}
                var identity=Json.Text(account,"id")??Json.Text(account,"email")??"current";string key;
                using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
                if(key!=accountKey){accountRevision++;snapshot=null;accountKey=key;}
                int revision=accountRevision;
                var fresh=Snapshot.Parse(await client.CallAsync("account/rateLimits/read"),Json.Text(account,"planType"));
                if(revision!=accountRevision||quitting)return;
                fresh.Usage=snapshot==null?null:snapshot.Usage;fresh.UsageStale=true;
                var messages=alerts.Update(snapshot,fresh,accountKey,settings.Warning,settings.Critical);
                snapshot=fresh;needsLogin=false;failures=0;Render();Save();
                if(settings.Notify&&!smoke&&messages.Count>0){tray.BalloonTipTitle="Codex 额度变化";tray.BalloonTipText=string.Join("\n",messages.Take(3));tray.ShowBalloonTip(8000);}
                await TokenUsage.RefreshAsync(fresh,()=>client.CallAsync("account/usage/read"));
                if(revision==accountRevision&&!quitting)Render();
            }catch(Exception ex){failures++;Render();Text("StatusText").Visibility=Visibility.Visible;Text("StatusText").Text=ex is TimeoutException?"读取超时，稍后自动重试。":ex.Message=="CLI_MISSING"?"未找到 Codex CLI，请在设置中选择 codex.exe。":"更新失败，请检查网络或 Codex 登录；稍后自动重试。";Text("UpdatedText").Text=snapshot==null?"尚未成功读取": "上次成功 "+snapshot.ReadAt.ToString("M月d日 HH:mm:ss");Text("MiniText").Text="Codex · 更新失败";}
            finally{busy=false;Button("RefreshButton").IsEnabled=true;Text("RefreshLabel").Text=needsLogin?"登录 Codex":"刷新额度";Schedule();if(retryNotification){retryNotification=false;var queued=window.Dispatcher.BeginInvoke(new Action(()=>Refresh(false)));}}
        }
        async void Login(){if(busy)return;busy=true;try{await client.ConnectAsync();var result=await client.CallAsync("account/login/start",new {type="chatgpt"});var url=Json.Text(result,"authUrl");Uri uri;if(Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="auth.openai.com"||uri.Host=="chatgpt.com"||uri.Host.EndsWith(".openai.com",StringComparison.OrdinalIgnoreCase))){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});Text("StatusText").Text="请在浏览器完成 Codex 登录，然后刷新。";}else Text("StatusText").Text="无法打开登录入口，请通过 Codex CLI 登录。";}catch{Text("StatusText").Text="登录入口不可用，请通过 Codex CLI 登录。";}finally{busy=false;}}
        TextBlock MakeText(string text,double size=14){var block=new TextBlock {Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};block.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return block;}
        void Render(){
            taskbar.Render(snapshot,failures>0,settings.Warning);
            RenderTokens();
            Text("PlanTitle").Text=snapshot==null?"订阅套餐暂不可用":snapshot.Title;displayLayout.Font(Text("PlanTitle"),Text("PlanTitle").Text.Length>16?18:23);
            var rows=Element<StackPanel>("QuotaRows");rows.Children.Clear();
            if(snapshot==null){Text("StatusText").Visibility=Visibility.Visible;Text("StatusText").Text="正在确认 Codex 登录与额度…";Text("UpdatedText").Text="尚未成功读取";Element<StackPanel>("ResetDetails").Children.Clear();Button("ResetDetailsButton").IsEnabled=false;rows.Children.Add(MakeText("额度暂不可用",22));Text("CreditsText").Text=Text("ResetText").Text=Text("ExpiryText").Text="暂不可用";Text("MiniText").Text="Codex · 额度暂不可用";displayLayout.Apply(window,settings.Scale,settings.Mini);return;}
            Button("ResetDetailsButton").IsEnabled=true;
            Text("StatusText").Visibility=Visibility.Collapsed;
            var main=snapshot.Windows.FirstOrDefault();
            foreach(var quota in snapshot.Windows){
                bool hero=quota==main;var panel=new StackPanel {Margin=new Thickness(0,0,0,hero?4:8)};
                string bucketName=quota.Bucket=="codex"?"":(quota.BucketName??quota.Bucket)+" · ";
                var label=MakeText(bucketName+quota.Label+"剩余",hero?16:14);if(hero)label.FontWeight=FontWeights.SemiBold;panel.Children.Add(label);
                var valuePanel=new StackPanel {Orientation=Orientation.Horizontal};var percent=MakeText(quota.Percent,hero?44:24);if(hero)percent.FontWeight=FontWeights.SemiBold;valuePanel.Children.Add(percent);
                if(quota.Remaining.HasValue&&quota.Remaining.Value<=settings.Warning){var tag=MakeText(quota.Remaining.Value<=0?quota.Label+"已用完":"额度偏低",12);tag.SetResourceReference(TextBlock.ForegroundProperty,"Warning");var badge=new Border {CornerRadius=new CornerRadius(7),Padding=new Thickness(9,5,9,5),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),Child=tag};badge.SetResourceReference(Border.BackgroundProperty,"WarningBg");valuePanel.Children.Add(badge);}
                panel.Children.Add(valuePanel);
                if(hero){var creditStatus=MakeText(snapshot.Credits!=null&&snapshot.Credits!="0.00"?"Credits 仍有余额。":"额度和余额以服务返回为准。",12);creditStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");creditStatus.Margin=new Thickness(0,0,0,4);panel.Children.Add(creditStatus);}
                var track=new Grid {Height=5,Margin=new Thickness(0,2,0,6)};var bg=new Border {CornerRadius=new CornerRadius(3)};bg.SetResourceReference(Border.BackgroundProperty,"Track");track.Children.Add(bg);
                if(quota.Remaining.HasValue&&quota.Remaining.Value>0){track.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(quota.Remaining.Value,GridUnitType.Star)});track.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(100-quota.Remaining.Value,GridUnitType.Star)});var fill=new Border {CornerRadius=new CornerRadius(3)};fill.SetResourceReference(Border.BackgroundProperty,"Accent");track.Children.Add(fill);}
                panel.Children.Add(track);var reset=MakeText("下次恢复："+Snapshot.Time(quota.ResetsAt),12);reset.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(reset);rows.Children.Add(panel);
            }
            if(main==null)rows.Children.Add(MakeText("额度窗口暂不可用",20));
            Text("CreditsText").Text=snapshot.Credits??"暂不可用";Text("ResetText").Text=snapshot.Resets.HasValue?snapshot.Resets+" 次":"暂不可用";
            var earliest=snapshot.ResetExpirations.Where(n=>n.HasValue).OrderBy(n=>n).FirstOrDefault();Text("ExpiryText").Text=snapshot.Resets==0?"—":Snapshot.Time(earliest);
            var details=Element<StackPanel>("ResetDetails");details.Children.Clear();
            if(snapshot.ResetExpirations.Count==0)details.Children.Add(MakeText(snapshot.Resets==0?"暂无可用重置。":"服务暂未提供重置明细。",12));
            foreach(var expiry in snapshot.ResetExpirations)details.Children.Add(MakeText(expiry.HasValue?"全额重置 · "+Snapshot.Time(expiry)+" 到期":"全额重置 · 到期时间暂不可用",12));
            if(snapshot.Resets.HasValue&&snapshot.Resets>snapshot.ResetExpirations.Count)details.Children.Add(MakeText("另有重置明细暂未提供。",12));
            Text("UpdatedText").Text="更新于 "+snapshot.ReadAt.ToString("M月d日 HH:mm:ss");Text("StatusText").Text=snapshot.Credits!=null&&snapshot.Credits!="0.00"?"Credits 仍有余额。":"额度和余额以服务返回为准。";
            Text("MiniText").Text="Codex · "+(main==null?"额度暂不可用":main.Label+"剩余 "+main.Percent);tray.Text=(snapshot.Title+" · "+(main==null?"暂不可用":main.Percent)).Substring(0,Math.Min(63,(snapshot.Title+" · "+(main==null?"暂不可用":main.Percent)).Length));
            displayLayout.Apply(window,settings.Scale,settings.Mini);
        }
        void RenderTokens(){
            var usage=snapshot==null?null:snapshot.Usage;bool stale=snapshot!=null&&(snapshot.UsageStale||failures>0);
            Text("YesterdayTokenText").Text=usage==null?"暂不可用":usage.ForDate(TokenUsage.Yesterday).HasValue?TokenUsage.Compact(usage.ForDate(TokenUsage.Yesterday)):"暂未更新";
            Text("TotalTokenText").Text=usage==null||!usage.LifetimeTokens.HasValue?"暂不可用":TokenUsage.Compact(usage.LifetimeTokens);
            if(stale&&usage!=null){Text("YesterdayTokenText").Text="旧值 · "+Text("YesterdayTokenText").Text;Text("TotalTokenText").Text="旧值 · "+Text("TotalTokenText").Text;}
            Text("TokenNoteText").Text=usage==null?"官方 token 统计暂不可用。":(stale?"Token 更新失败 · ":"官方统计 · ")+"统计日期 "+TokenUsage.Yesterday;
            Text("TokenNoteText").ToolTip=usage==null?"Token 统计与额度独立刷新。":usage.Detail(stale,TokenUsage.Yesterday);
            Text("YesterdayTokenText").ToolTip=Text("TotalTokenText").ToolTip=Text("TokenNoteText").ToolTip;
            Text("YesterdayTokenText").SetResourceReference(TextBlock.ForegroundProperty,"Ink");Text("TotalTokenText").SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        }
        void ApplyTheme(){bool dark=settings.Theme=="dark";if(settings.Theme=="system"){using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){dark=key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}}
            string[] keys={"Surface","Ink","Muted","Line","Track","Accent","AccentInk","Warning","WarningBg"};string[] light={"#FAFBFB","#171B20","#69737D","#DFE3E5","#E2E5E8","#536967","#FFFFFF","#965C12","#FFF0D7"};string[] night={"#23282D","#F1F2F3","#B0B9C2","#41474E","#41474E","#A6C3BE","#182321","#F5BE67","#433824"};for(int i=0;i<keys.Length;i++)window.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString((dark?night:light)[i]));}
        void OpenSettings(){if(settingsWindow!=null){settingsWindow.Activate();return;}
            Window dialog;using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsWindow.xaml"))dialog=(Window)XamlReader.Load(stream);
            dialog.Owner=window;dialog.Icon=window.Icon;settingsWindow=dialog;
            foreach(string key in new[]{"Ink","Muted","Line","Accent","AccentInk"})dialog.Resources[key]=window.Resources[key];
            bool dark=((SolidColorBrush)window.Resources["Surface"]).Color.R<128;
            if(dark){dialog.Resources["SettingsSurface"]=new SolidColorBrush(Color.FromRgb(28,33,37));dialog.Resources["Card"]=new SolidColorBrush(Color.FromRgb(35,40,45));dialog.Resources["Hover"]=new SolidColorBrush(Color.FromRgb(54,64,66));}
            var interval=(ComboBox)dialog.FindName("IntervalChoice");interval.ItemsSource=new[]{"1 分钟","5 分钟","15 分钟"};interval.SelectedIndex=settings.Interval==1?0:settings.Interval==15?2:1;
            var theme=(ComboBox)dialog.FindName("ThemeChoice");theme.ItemsSource=new[]{"跟随系统","浅色","深色"};theme.SelectedIndex=settings.Theme=="light"?1:settings.Theme=="dark"?2:0;
            var scaleLabel=(TextBlock)dialog.FindName("ScaleLabel");var scale=(Slider)dialog.FindName("ScaleChoice");scale.Value=settings.Scale;scaleLabel.Text=(scale.Value*100).ToString("0")+"%";scale.ValueChanged+=(s,e)=>scaleLabel.Text=(scale.Value*100).ToString("0")+"%";
            var dock=(CheckBox)dialog.FindName("DockChoice");dock.IsChecked=settings.DockBottomRight;
            var taskbarChoice=(CheckBox)dialog.FindName("TaskbarChoice");taskbarChoice.IsChecked=settings.TaskbarMode;
            var taskbarLeft=(CheckBox)dialog.FindName("TaskbarLeftChoice");taskbarLeft.IsChecked=settings.TaskbarLeft;
            var notify=(CheckBox)dialog.FindName("NotifyChoice");notify.IsChecked=settings.Notify;
            var warning=(TextBox)dialog.FindName("WarningChoice");warning.Text=settings.Warning.ToString();
            var critical=(TextBox)dialog.FindName("CriticalChoice");critical.Text=settings.Critical.ToString();
            var topmost=(CheckBox)dialog.FindName("TopmostChoice");topmost.IsChecked=settings.Topmost;
            var startup=(CheckBox)dialog.FindName("StartupChoice");startup.IsChecked=Settings.AutoStart;
            string[] displayFields={"ShowQuota","ShowCredits","ShowResets","ShowYesterdayTokens","ShowTotalTokens","ShowUpload","ShowDownload","ShowCpu","ShowMemory","ShowGpu","ShowDisk"};
            var displayChoices=new Dictionary<string,CheckBox>();foreach(var name in displayFields){var checkbox=(CheckBox)dialog.FindName(name+"Choice");checkbox.IsChecked=(bool)typeof(Settings).GetField(name).GetValue(settings);displayChoices[name]=checkbox;}
            Action preview=()=>{var draft=new Settings();foreach(var pair in displayChoices)typeof(Settings).GetField(pair.Key).SetValue(draft,pair.Value.IsChecked==true);int width=TaskbarColumns.Width(TaskbarColumns.Create(TaskbarContent.Create(null,false),null,TaskbarSelection.From(draft)));((TextBlock)dialog.FindName("TaskbarPreview")).Text="已选择 "+displayChoices.Count(p=>p.Value.IsChecked==true)+" 项 · 预计宽度 "+width+" px";};
            foreach(var checkbox in displayChoices.Values){checkbox.Checked+=(s,e)=>preview();checkbox.Unchecked+=(s,e)=>preview();}preview();
            Action<bool> tab=system=>{((ScrollViewer)dialog.FindName("SettingsScroll")).Visibility=system?Visibility.Collapsed:Visibility.Visible;((ScrollViewer)dialog.FindName("TaskbarSettingsScroll")).Visibility=system?Visibility.Visible:Visibility.Collapsed;foreach(var name in new[]{"GeneralTab","TaskbarTab"}){bool active=(name=="TaskbarTab")==system;var button=(Button)dialog.FindName(name);button.SetResourceReference(Control.BackgroundProperty,active?"Accent":"Card");button.SetResourceReference(Control.ForegroundProperty,active?"AccentInk":"Ink");}};
            ((Button)dialog.FindName("GeneralTab")).Click+=(s,e)=>tab(false);((Button)dialog.FindName("TaskbarTab")).Click+=(s,e)=>tab(true);tab(false);
            string selectedExecutable=client.Executable??"";var cliStatus=(TextBlock)dialog.FindName("CliStatus");Action updatePath=()=>{cliStatus.Text=File.Exists(selectedExecutable)?"已检测到 "+Path.GetFileName(selectedExecutable):"未检测到，请选择 codex.exe";cliStatus.ToolTip=selectedExecutable;};updatePath();
            ((Button)dialog.FindName("ChooseCli")).Click+=(s,e)=>{var picker=new Microsoft.Win32.OpenFileDialog {Filter="Codex CLI (codex.exe)|codex.exe"};if(picker.ShowDialog(dialog)==true){selectedExecutable=picker.FileName;updatePath();}};
            var header=(Grid)dialog.FindName("SettingsHeader");((StackPanel)header.Children[0]).MouseLeftButtonDown+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed)dialog.DragMove();};
            ((Button)dialog.FindName("CancelSettings")).Click+=(s,e)=>dialog.Close();((Button)dialog.FindName("CloseSettings")).Click+=(s,e)=>dialog.Close();dialog.KeyDown+=(s,e)=>{if(e.Key==Key.Escape)dialog.Close();};
            var save=(Button)dialog.FindName("SaveSettings");
            save.Click+=(s,e)=>{int w,c;if(!int.TryParse(warning.Text,out w)||!int.TryParse(critical.Text,out c)||w>100||w<1||c<0||c>=w){MessageBox.Show(dialog,"阈值需满足 0 ≤ 较低阈值 < 较高阈值 ≤ 100。","请检查阈值");return;}try{if(!smoke&&startup.IsChecked==true!=Settings.AutoStart)Settings.SetAutoStart(startup.IsChecked==true);}catch{MessageBox.Show(dialog,"开机启动设置未能保存。","设置未完成");return;}settings.Interval=new[]{1,5,15}[interval.SelectedIndex];settings.Theme=new[]{"system","light","dark"}[theme.SelectedIndex];settings.Scale=Settings.NormalizeScale(scale.Value);settings.DockBottomRight=dock.IsChecked==true;settings.Notify=notify.IsChecked==true;settings.Warning=w;settings.Critical=c;settings.Topmost=topmost.IsChecked==true;window.Topmost=settings.Topmost;foreach(var pair in displayChoices)typeof(Settings).GetField(pair.Key).SetValue(settings,pair.Value.IsChecked==true);taskbar.SetSelection(TaskbarSelection.From(settings));if(selectedExecutable!=client.Executable){settings.Executable=selectedExecutable;client.Executable=selectedExecutable;client.Reconnect();}ApplyTheme();ApplyWindowSize();UpdatePin();Render();settings.TaskbarLeft=taskbarLeft.IsChecked==true;SetTaskbarMode(taskbarChoice.IsChecked==true);Save();Schedule();dialog.Close();Refresh(true);};
            dialog.Closed+=(s,e)=>settingsWindow=null;dialog.Show();
        }
        void Capture(string path,double scale=1){CaptureWindow(window,path,scale);}
        void CaptureWindow(Window target,string path,double scale=1){target.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth*scale),(int)Math.Ceiling(target.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);}
        async Task CaptureScreen(Window target,string path){
            target.UpdateLayout();target.Activate();await Task.Delay(150);
            var topLeft=target.PointToScreen(new Point(0,0));var bottomRight=target.PointToScreen(new Point(target.ActualWidth,target.ActualHeight));
            using(PixelCoordinates.Physical())using(var bitmap=new System.Drawing.Bitmap((int)Math.Round(bottomRight.X-topLeft.X),(int)Math.Round(bottomRight.Y-topLeft.Y)))using(var graphics=System.Drawing.Graphics.FromImage(bitmap)){
                graphics.CopyFromScreen((int)Math.Round(topLeft.X),(int)Math.Round(topLeft.Y),0,0,bitmap.Size);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        void CheckSummaryFit(string state,List<string> checks,List<object> geometry){
            var scroll=Element<ScrollViewer>("SummaryScroll");scroll.ScrollToTop();window.UpdateLayout();
            var expiry=Text("ExpiryText");var button=Button("ResetDetailsButton");
            var expiryBounds=expiry.TransformToAncestor(scroll).TransformBounds(new Rect(expiry.RenderSize));
            var buttonBounds=button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
            bool fits=scroll.ExtentHeight<=scroll.ViewportHeight+1&&expiryBounds.Bottom<=scroll.ActualHeight+1&&buttonBounds.Bottom<=scroll.ActualHeight+1;
            checks.Add("summary-fit-"+state+(fits?":passed":":failed"));
            geometry.Add(new {state,settings.Scale,scroll.ExtentHeight,scroll.ViewportHeight,scroll.ActualHeight,ExpiryBottom=expiryBounds.Bottom,DetailsBottom=buttonBounds.Bottom});
        }
        async Task Smoke(){
            var evidence=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","evidence");Directory.CreateDirectory(evidence);var checks=new List<string>();
            var geometry=new List<object>();var summaryGeometry=new List<object>();
            checks.Add(window.Icon!=null&&window.Icon.Width==256&&tray.Icon.Width==16?"application-icons:passed":"application-icons:failed");using(var bitmap=tray.Icon.ToBitmap())bitmap.Save(Path.Combine(evidence,"tray-icon.png"),System.Drawing.Imaging.ImageFormat.Png);
            await RefreshAsync(true);
            // Initial account notifications can invalidate the first response; allow the queued read to finish.
            for(int attempt=0;attempt<3&&snapshot==null&&!needsLogin&&failures==0;attempt++){await Task.Delay(100);while(busy)await Task.Delay(30);if(snapshot==null)await RefreshAsync(true);}
            checks.Add(snapshot!=null?"live-read:passed":"live-read:failed");
            if(snapshot==null){File.WriteAllText(Path.Combine(evidence,"ui-results.json"),Json.Encode(checks));SmokeFailed=true;Quit();return;}
            // Subsequent assertions control their own snapshots; unsolicited service events must not replace fixtures.
            smokeFixtures=true;
            Capture(Path.Combine(evidence,"live.png"));
            CheckSummaryFit("live",checks,summaryGeometry);SetScale(0.9);Capture(Path.Combine(evidence,"live-90.png"));window.Left=(SystemParameters.WorkArea.Width-window.Width)/2;window.Top=(SystemParameters.WorkArea.Height-window.Height)/2;await CaptureScreen(window,Path.Combine(evidence,"live-90-screen.png"));RestorePosition();CheckSummaryFit("live-90",checks,summaryGeometry);SetScale(0.75);
            checks.Add(window.FindName("ScaledView")==null&&!window.AllowsTransparency&&TextOptions.GetTextRenderingMode(window)==TextRenderingMode.Grayscale&&Text("TotalTokenText").FontSize==Math.Round(13*settings.Scale,MidpointRounding.AwayFromZero)?"native-text-without-viewbox-scaling:passed":"native-text-without-viewbox-scaling:failed");
            checks.Add(snapshot!=null&&snapshot.Usage!=null&&!snapshot.UsageStale&&Text("TotalTokenText").Text==TokenUsage.Compact(snapshot.Usage.LifetimeTokens)&&((string)Text("TotalTokenText").ToolTip).Contains(TokenUsage.Full(snapshot.Usage.LifetimeTokens))?"live-account-token-total:passed":"live-account-token-total:failed");
            var yesterdayValue=snapshot==null||snapshot.Usage==null?null:snapshot.Usage.ForDate(TokenUsage.Yesterday);checks.Add(Text("YesterdayTokenText").Text==(yesterdayValue.HasValue?TokenUsage.Compact(yesterdayValue):"暂未更新")&&(!yesterdayValue.HasValue||((string)Text("YesterdayTokenText").ToolTip).Contains(TokenUsage.Full(yesterdayValue)))&&Text("TokenNoteText").Text.Contains("统计日期 "+TokenUsage.Yesterday)?"live-token-yesterday-or-pending:passed":"live-token-yesterday-or-pending:failed");
            if(snapshot!=null&&snapshot.Usage!=null)File.WriteAllText(Path.Combine(evidence,"token-usage.json"),Json.Encode(new {snapshot.Usage.LifetimeTokens,Yesterday=TokenUsage.Yesterday,YesterdayTokens=yesterdayValue,LatestDate=snapshot.Usage.LatestDate,snapshot.Usage.ReadAt,snapshot.UsageStale}));
            var liveSnapshot=snapshot;var livePath=client.Executable;client.Reconnect();client.Executable="missing-cli.exe";await RefreshAsync(true);checks.Add(snapshot==liveSnapshot&&Text("StatusText").Text.Contains("未找到")?"failed-refresh-keeps-snapshot:passed":"failed-refresh-keeps-snapshot:failed");Capture(Path.Combine(evidence,"stale.png"));client.Executable=livePath;
            snapshot=Snapshot.Parse(Json.Parse(Tests.Fixture),"pro");snapshot.ReadAt=new DateTime(2026,10,11,15,28,0);Render();Capture(Path.Combine(evidence,"selected-design.png"));Capture(Path.Combine(evidence,"selected-design-150.png"),1.5);
            CheckSummaryFit("zero-quota",checks,summaryGeometry);
            checks.Add(window.Width==293&&window.Height==411?"default-75-percent:passed":"default-75-percent:failed");
            var work=SystemParameters.WorkArea;checks.Add(Math.Abs(window.Left+window.Width-work.Right+16)<=PixelTolerance&&Math.Abs(window.Top+window.Height-work.Bottom+16)<=PixelTolerance?"bottom-right-placement:passed":"bottom-right-placement:failed");
            geometry.Add(new {state="default",window.Width,window.Height,window.ActualWidth,window.ActualHeight,window.Left,window.Top,Work=work.ToString()});
            foreach(var ratio in new[]{0.5,0.75,1.0,1.2}){SetScale(ratio);Capture(Path.Combine(evidence,"scaled-"+(ratio*100).ToString("0")+".png"));var surface=Element<Border>("SurfaceBorder");var point=surface.TransformToAncestor(window).Transform(new Point(surface.Width,surface.Height));geometry.Add(new {ratio,window.Width,window.Height,window.ActualWidth,window.ActualHeight,window.Left,window.Top,ContentRight=point.X,ContentBottom=point.Y,TokenFont=Text("TotalTokenText").FontSize});checks.Add(Math.Abs(window.Width-Math.Ceiling(390*ratio))<=PixelTolerance&&Math.Abs(window.Height-Math.Ceiling(548*ratio))<=PixelTolerance&&point.X<=window.ActualWidth+1&&point.Y<=window.ActualHeight+1?"uniform-scale-"+(ratio*100).ToString("0")+":passed":"uniform-scale-"+(ratio*100).ToString("0")+":failed");CheckSummaryFit("scale-"+(ratio*100).ToString("0"),checks,summaryGeometry);}
            SetScale(0.75);
            Button("PinButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(window.Topmost?"pin:passed":"pin:failed");
            Button("CollapseButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Capture(Path.Combine(evidence,"mini.png"));checks.Add(Math.Abs(window.Height-Math.Ceiling(108*settings.Scale))<=PixelTolerance&&Math.Abs(window.Top+window.Height-work.Bottom+16)<=PixelTolerance?"collapse:passed":"collapse:failed");Button("ExpandButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Button("ResetDetailsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(Element<StackPanel>("ResetDetails").Visibility==Visibility.Visible?"reset-details:passed":"reset-details:failed");Button("ResetDetailsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            settings.Theme="dark";ApplyTheme();Capture(Path.Combine(evidence,"dark.png"));settings.Theme="light";ApplyTheme();
            SetScale(0.9);OpenSettings();var userOptions=Settings.Load();((ComboBox)settingsWindow.FindName("IntervalChoice")).SelectedIndex=userOptions.Interval==1?0:userOptions.Interval==15?2:1;((CheckBox)settingsWindow.FindName("TaskbarChoice")).IsChecked=userOptions.TaskbarMode;((CheckBox)settingsWindow.FindName("DockChoice")).IsChecked=userOptions.DockBottomRight;CaptureWindow(settingsWindow,Path.Combine(evidence,"settings-light.png"));await CaptureScreen(settingsWindow,Path.Combine(evidence,"settings-light-screen.png"));
            var settingsScroll=(ScrollViewer)settingsWindow.FindName("SettingsScroll");checks.Add(settingsScroll.ExtentHeight<=settingsScroll.ViewportHeight+1?"settings-summary-fits:passed":"settings-summary-fits:failed");
            ((Button)settingsWindow.FindName("TaskbarTab")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));CaptureWindow(settingsWindow,Path.Combine(evidence,"settings-taskbar.png"));var taskbarScroll=(ScrollViewer)settingsWindow.FindName("TaskbarSettingsScroll");checks.Add(taskbarScroll.IsVisible&&taskbarScroll.ExtentHeight<=taskbarScroll.ViewportHeight+1?"settings-taskbar-items-fit:passed":"settings-taskbar-items-fit:failed");((CheckBox)settingsWindow.FindName("ShowGpuChoice")).IsChecked=true;((CheckBox)settingsWindow.FindName("ShowCreditsChoice")).IsChecked=false;((Button)settingsWindow.FindName("GeneralTab")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            settingsWindow.UpdateLayout();await Task.Delay(100);var themePopup=(ComboBox)settingsWindow.FindName("ThemeChoice");themePopup.IsDropDownOpen=true;var popup=(System.Windows.Controls.Primitives.Popup)themePopup.Template.FindName("PART_Popup",themePopup);for(int popupWait=0;popupWait<20&&(popup==null||!popup.IsOpen||popup.Child==null||!popup.Child.IsVisible);popupWait++)await Task.Delay(50);File.WriteAllText(Path.Combine(evidence,"settings-dropdown-state.json"),Json.Encode(new {RequestedOpen=themePopup.IsDropDownOpen,TemplatePopup=popup!=null,Open=popup!=null&&popup.IsOpen,Visible=popup!=null&&popup.Child!=null&&popup.Child.IsVisible}));checks.Add(popup!=null&&popup.IsOpen&&popup.Child.IsVisible?"settings-combobox-dropdown:passed":"settings-combobox-dropdown:failed");themePopup.IsDropDownOpen=false;
            ((Slider)settingsWindow.FindName("ScaleChoice")).Value=0.5;((CheckBox)settingsWindow.FindName("TaskbarChoice")).IsChecked=true;((Button)settingsWindow.FindName("CancelSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(settingsWindow==null&&settings.Scale==0.9&&!settings.TaskbarMode&&!settings.ShowGpu&&settings.ShowCredits?"settings-cancel-preserves-options:passed":"settings-cancel-preserves-options:failed");SetScale(0.75);
            settings.Theme="dark";ApplyTheme();OpenSettings();CaptureWindow(settingsWindow,Path.Combine(evidence,"settings-dark.png"));await CaptureScreen(settingsWindow,Path.Combine(evidence,"settings-dark-screen.png"));((Button)settingsWindow.FindName("CloseSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));settings.Theme="light";ApplyTheme();
            Button("SettingsButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(settingsWindow!=null&&settingsWindow.IsVisible?"settings:passed":"settings:failed");((ComboBox)settingsWindow.FindName("ThemeChoice")).SelectedIndex=2;((ComboBox)settingsWindow.FindName("IntervalChoice")).SelectedIndex=2;((Slider)settingsWindow.FindName("ScaleChoice")).Value=0.6;((Button)settingsWindow.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(settings.Theme=="dark"&&settings.Interval==15&&settingsWindow==null?"settings-save:passed":"settings-save:failed");checks.Add(Math.Abs(settings.Scale-0.6)<0.001&&Math.Abs(window.Width-234)<0.1?"scale-settings-save:passed":"scale-settings-save:failed");while(busy)await Task.Delay(30);settings.Theme="light";ApplyTheme();SetScale(0.75);
            Button("HideButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(!window.IsVisible&&tray.Visible?"tray-hide:passed":"tray-hide:failed");Show();checks.Add(window.IsVisible?"tray-show:passed":"tray-show:failed");
            window.Close();checks.Add(!window.IsVisible&&tray.Visible&&!quitting?"close-to-tray:passed":"close-to-tray:failed");Show();
            settings.DockBottomRight=false;settings.Left=-10000;settings.Top=-10000;RestorePosition();checks.Add(window.Left>=work.Left&&window.Top>=work.Top?"offscreen-recovery:passed":"offscreen-recovery:failed");settings.DockBottomRight=true;RestorePosition();
            snapshot=liveSnapshot;failures=0;Render();SetTaskbarMode(true);for(int i=0;i<40&&taskbar.Status!="attached";i++)await Task.Delay(100);await Task.Delay(150);
            checks.Add(taskbar.Visible&&!window.IsVisible&&taskbar.Status=="attached"?"taskbar-attach-and-hide-desktop:passed":"taskbar-attach-and-hide-desktop:failed");
            checks.Add(!taskbar.OverlapsControls?"taskbar-no-overlap-with-controls:passed":"taskbar-no-overlap-with-controls:failed");
            File.WriteAllText(Path.Combine(evidence,"taskbar-geometry.json"),Json.Encode(taskbar.LayoutEvidence()));taskbar.Capture(Path.Combine(evidence,"taskbar-live.png"),true);taskbar.Capture(Path.Combine(evidence,"taskbar-strip.png"),false);
            checks.Add(taskbar.VisibleGreenPixels()>50?"taskbar-text-visible-on-screen:passed":"taskbar-text-visible-on-screen:failed");
            checks.Add(taskbar.FirstLine.Contains(liveSnapshot.Title)&&taskbar.FirstLine.Contains("Credits")&&taskbar.SecondLine.Contains(liveSnapshot.Credits??"暂不可用")?"taskbar-live-plan-and-credits:passed":"taskbar-live-plan-and-credits:failed");
            checks.Add(taskbar.ExpiryText==TaskbarContent.Expiry(liveSnapshot)&&taskbar.Bounds.Width==(int)Math.Round(366*taskbar.Dpi/96.0)&&taskbar.Bounds.Height==(int)Math.Round(40*taskbar.Dpi/96.0)?"taskbar-reset-expiry-and-four-columns:passed":"taskbar-reset-expiry-and-four-columns:failed");
            checks.Add(taskbar.FirstLine.Contains("昨日")&&taskbar.SecondLine.Contains("累计 "+TokenUsage.Compact(liveSnapshot.Usage.LifetimeTokens))&&taskbar.TooltipText.Contains(TokenUsage.Full(liveSnapshot.Usage.LifetimeTokens))?"taskbar-account-tokens-and-exact-tooltip:passed":"taskbar-account-tokens-and-exact-tooltip:failed");
            var hover=await taskbar.TestHover(Path.Combine(evidence,"taskbar-hover.png"));File.WriteAllText(Path.Combine(evidence,"hover-geometry.json"),Json.Encode(hover));
            checks.Add(hover.BlankAreaHit&&hover.ShowAlways&&hover.PopupVisible&&hover.ForegroundUnchanged&&hover.Text.Contains("历史累计")&&hover.Text.Contains("重置到期")?"taskbar-inactive-blank-area-hover-popup:passed":"taskbar-inactive-blank-area-hover-popup:failed");
            checks.Add(hover.SurvivesLayoutCheck?"taskbar-hover-survives-layout-check:passed":"taskbar-hover-survives-layout-check:failed");
            checks.Add(hover.LeaveHides&&hover.CursorRestored?"taskbar-hover-leave-hides-and-restores-pointer:passed":"taskbar-hover-leave-hides-and-restores-pointer:failed");
            liveSnapshot.UsageStale=true;Render();checks.Add(taskbar.FirstLine.StartsWith(liveSnapshot.Title)&&taskbar.SecondLine.Contains("旧累计")&&Text("TokenNoteText").Text.Contains("Token 更新失败")?"token-failure-independent-from-quota:passed":"token-failure-independent-from-quota:failed");
            checks.Add(taskbar.TokensUseNormalColor&&((SolidColorBrush)Text("TotalTokenText").Foreground).Color==((SolidColorBrush)window.Resources["Ink"]).Color&&((SolidColorBrush)Text("YesterdayTokenText").Foreground).Color==((SolidColorBrush)window.Resources["Ink"]).Color?"stale-tokens-keep-normal-color:passed":"stale-tokens-keep-normal-color:failed");
            await Task.Delay(80);taskbar.Capture(Path.Combine(evidence,"taskbar-token-stale.png"),false);liveSnapshot.UsageStale=false;Render();
            taskbar.Render(snapshot,true);checks.Add(taskbar.FirstLine.StartsWith("更新失败")&&taskbar.SecondLine.Contains("上次额度")&&taskbar.TooltipText.Contains("上次成功")?"taskbar-stale-distinguished:passed":"taskbar-stale-distinguished:failed");await Task.Delay(80);taskbar.Capture(Path.Combine(evidence,"taskbar-stale.png"),false);taskbar.Render(null,false);checks.Add(taskbar.FirstLine.Contains("暂不可用")&&!taskbar.FirstLine.Contains("0%")&&taskbar.ExpiryText=="到期暂不可用"?"taskbar-unknown-not-zero:passed":"taskbar-unknown-not-zero:failed");taskbar.Render(snapshot,false);
            taskbar.TestClick();checks.Add(window.IsVisible?"taskbar-click-opens-details:passed":"taskbar-click-opens-details:failed");
            taskbar.TestMenu();checks.Add(tray.ContextMenuStrip.Visible?"taskbar-context-menu:passed":"taskbar-context-menu:failed");tray.ContextMenuStrip.Hide();taskbarItem.PerformClick();checks.Add(!taskbar.Visible&&taskbar.Handle==IntPtr.Zero&&window.IsVisible?"taskbar-return-desktop:passed":"taskbar-return-desktop:failed");
            OpenSettings();((CheckBox)settingsWindow.FindName("TaskbarChoice")).IsChecked=true;((Button)settingsWindow.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));checks.Add(settings.TaskbarMode&&taskbarItem.Checked&&settingsWindow==null?"taskbar-settings-save:passed":"taskbar-settings-save:failed");while(busy)await Task.Delay(30);SetTaskbarMode(false);
            snapshot=Snapshot.Parse(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":75,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":90,\"windowDurationMins\":10080}}}"),"plus");Render();Capture(Path.Combine(evidence,"two-windows.png"));checks.Add(Text("PlanTitle").Text=="ChatGPT Plus"?"dynamic-plan:passed":"dynamic-plan:failed");
            snapshot=liveSnapshot;Render();SetTaskbarMode(true);settings.ShowUpload=settings.ShowDownload=settings.ShowCpu=settings.ShowMemory=settings.ShowGpu=settings.ShowDisk=true;taskbar.SetSelection(TaskbarSelection.From(settings));await taskbar.SampleSystem();await Task.Delay(1100);await taskbar.SampleSystem();await taskbar.UpdatePlacement();await Task.Delay(150);File.WriteAllText(Path.Combine(evidence,"system-live.json"),Json.Encode(taskbar.Usage));File.WriteAllText(Path.Combine(evidence,"taskbar-system-geometry.json"),Json.Encode(taskbar.LayoutEvidence()));taskbar.Capture(Path.Combine(evidence,"taskbar-system.png"),false);taskbar.CaptureRendered(Path.Combine(evidence,"taskbar-system-rendered.png"));
            checks.Add(taskbar.RequestedWidth==598&&taskbar.Bounds.Width==(int)Math.Round(598*taskbar.Dpi/96.0)&&taskbar.Visible&&!taskbar.OverlapsControls&&taskbar.FirstLine.StartsWith("↑ ",StringComparison.Ordinal)&&taskbar.FirstLine.IndexOf("CPU:",StringComparison.Ordinal)<taskbar.FirstLine.IndexOf(liveSnapshot.Title,StringComparison.Ordinal)&&taskbar.FirstLine.Contains("显卡:")&&taskbar.SecondLine.Contains("内存:")?"taskbar-live-six-system-metrics:passed":"taskbar-live-six-system-metrics:failed");
            checks.Add(taskbar.Usage.Cpu.HasValue&&taskbar.Usage.Memory.HasValue&&taskbar.Usage.Upload.HasValue&&taskbar.Usage.Download.HasValue?"system-live-independent-sampling:passed":"system-live-independent-sampling:failed");
            OpenSettings();((Button)settingsWindow.FindName("TaskbarTab")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));CaptureWindow(settingsWindow,Path.Combine(evidence,"settings-taskbar-all.png"));foreach(var name in new[]{"ShowQuota","ShowCredits","ShowResets","ShowYesterdayTokens","ShowTotalTokens","ShowUpload","ShowDownload","ShowMemory","ShowGpu","ShowDisk"})((CheckBox)settingsWindow.FindName(name+"Choice")).IsChecked=false;((Button)settingsWindow.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));while(busy)await Task.Delay(30);await taskbar.UpdatePlacement();await Task.Delay(100);checks.Add(settings.ShowCpu&&!settings.ShowMemory&&!settings.ShowCredits&&taskbar.RequestedWidth==80&&taskbar.Bounds.Width==(int)Math.Round(80*taskbar.Dpi/96.0)?"taskbar-selection-save-and-shrink:passed":"taskbar-selection-save-and-shrink:failed");taskbar.Capture(Path.Combine(evidence,"taskbar-cpu-only.png"),false);
            taskbar.SetSelection(new TaskbarSelection());await taskbar.UpdatePlacement();checks.Add(!taskbar.Visible&&taskbar.Status=="all-items-hidden"?"taskbar-all-hidden-tray-remains:passed":"taskbar-all-hidden-tray-remains:failed");taskbar.SetSelection(TaskbarSelection.From(new Settings()));await taskbar.UpdatePlacement();SetTaskbarMode(false);
            snapshot=null;Render();Capture(Path.Combine(evidence,"unknown.png"));checks.Add(Text("CreditsText").Text=="暂不可用"&&!Button("ResetDetailsButton").IsEnabled&&Element<StackPanel>("ResetDetails").Children.Count==0?"unknown:passed":"unknown:failed");
            checks.Add(Text("TotalTokenText").Text=="暂不可用"&&Text("YesterdayTokenText").Text=="暂不可用"&&taskbar.FirstLine.Contains("昨日 —")&&taskbar.SecondLine.Contains("累计 —")?"account-clear-removes-token-values:passed":"account-clear-removes-token-values:failed");
            File.WriteAllText(Path.Combine(evidence,"ui-results.json"),Json.Encode(checks));File.WriteAllText(Path.Combine(evidence,"scale-geometry.json"),Json.Encode(geometry));File.WriteAllText(Path.Combine(evidence,"summary-geometry.json"),Json.Encode(summaryGeometry));SmokeFailed=checks.Any(c=>!c.EndsWith(":passed",StringComparison.Ordinal));Quit();
        }
    }
}




