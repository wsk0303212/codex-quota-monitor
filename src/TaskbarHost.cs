using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using Forms=System.Windows.Forms;

namespace CodexQuotaMonitor {
    // Own-process child window only: never resize, patch or inject code into Explorer.
    public sealed class TaskbarHost : IDisposable {
        readonly Forms.Timer timer=new Forms.Timer {Interval=3000};
        readonly Forms.Timer systemTimer=new Forms.Timer {Interval=1000};readonly SystemSampler sampler=new SystemSampler();
        readonly Action open;readonly Forms.ContextMenuStrip menu;
        Strip strip;bool enabled,scanning,disposed;IntPtr parent;int parentDpi;
        TaskbarContent content=TaskbarContent.Create(null,false);
        TaskbarSelection selection=TaskbarSelection.From(new Settings());SystemUsage usage=new SystemUsage();bool sampling;
        List<TaskbarColumn> Columns {get{return TaskbarColumns.Create(content,usage,selection);}}
        public int RequestedWidth {get{return TaskbarColumns.Width(Columns);}}
        internal SystemUsage Usage {get{return usage;}}
        internal int Dpi {get{return parentDpi>0?parentDpi:96;}}
        public string FirstLine {get{return string.Join(" · ",Columns.Select(c=>c.Header));}}
        public string SecondLine {get{return string.Join(" · ",Columns.Select(c=>c.Value));}}
        public string ExpiryText {get{return content.ResetExpiry;}}
        public string TooltipText {get{return (selection.AnyCodex?content.Tooltip:"")+(selection.AnySystem?"\n\n"+usage.Detail(selection):"");}}
        internal bool TokensUseNormalColor {get{return strip!=null&&strip.TokenInk==strip.NormalInk;}}
        public string Status {get;private set;}
        public bool PreferLeft=true;
        public bool Visible {get{return strip!=null&&!strip.IsDisposed&&strip.Visible;}}
        public Rectangle Bounds {get{return strip==null?Rectangle.Empty:Native.ScreenRect(strip.Handle);}}
        public IntPtr ParentHandle {get{return parent;}}
        public IntPtr Handle {get{return strip==null?IntPtr.Zero:strip.Handle;}}
        public bool OverlapsControls {get{var layout=ReadLayout();return layout==null||layout.Occupied.Any(r=>r.IntersectsWith(Bounds));}}
        public TaskbarHost(Action show,Forms.ContextMenuStrip context){open=show;menu=context;timer.Tick+=async(s,e)=>await UpdatePlacement();systemTimer.Tick+=async(s,e)=>await SampleSystem();}
        public void SetSelection(TaskbarSelection value){selection=value;ApplyText();if(enabled&&selection.AnySystem)systemTimer.Start();else systemTimer.Stop();}
        internal async Task SampleSystem(){if(disposed||sampling||!selection.AnySystem)return;sampling=true;var requested=selection;try{var fresh=await Task.Run(()=>sampler.Sample(requested));if(!disposed&&selection==requested){usage=fresh;ApplyText();}}finally{sampling=false;if(disposed)sampler.Dispose();}}
        public async void Enable(bool value){enabled=value;if(value){timer.Start();if(selection.AnySystem)systemTimer.Start();await UpdatePlacement();}else{timer.Stop();systemTimer.Stop();CloseStrip();Status="disabled";}}
        public void Render(Snapshot snapshot,bool stale,int warningThreshold=20){
            content=TaskbarContent.Create(snapshot,stale,warningThreshold);
            ApplyText();
        }
        void ApplyText(){if(strip!=null&&!strip.IsDisposed)strip.UpdateText(Columns,TooltipText);}
        public async Task<bool> UpdatePlacement(){
            if(!enabled||disposed||scanning)return Visible;scanning=true;
            try {
                var task=Task.Run(()=>ReadLayout());
                if(await Task.WhenAny(task,Task.Delay(1500))!=task){HideStrip();Status="layout-timeout";await task;return false;}
                var layout=await task;if(!enabled||disposed)return false;
                if(layout==null){HideStrip();Status="taskbar-unavailable";return false;}
                if(RequestedWidth==0){HideStrip();Status="all-items-hidden";return false;}
                int width=(int)Math.Round(RequestedWidth*layout.Dpi/96.0),height=Math.Min(layout.Bar.Height-4,(int)Math.Round(40*layout.Dpi/96.0));
                var bounds=FindSpace(layout.Bar,layout.Occupied,width,height,(int)Math.Ceiling(8*layout.Dpi/96.0),PreferLeft);
                if(bounds.IsEmpty){HideStrip();Status="no-free-space";return false;}
                if(strip==null||strip.IsDisposed||parent!=layout.Handle||parentDpi!=layout.Dpi||!Native.IsWindow(strip.Handle)){CloseStrip();parent=layout.Handle;parentDpi=layout.Dpi;using(PixelCoordinates.Window(parent)){strip=new Strip(layout.Dpi,open,menu);var h=strip.Handle;Native.SetChild(h);Native.SetParent(h,parent);if(Native.GetParent(h)!=parent){CloseStrip();Status="attach-failed";return false;}}ApplyText();}
                if(strip.NeedsBackground(layout.Light)){var background=SampleBackground(bounds,parent);strip.SetBackground(layout.Light,background??(layout.Light?Color.FromArgb(243,243,243):Color.FromArgb(32,32,32)),background.HasValue);}
                // Physical screen pixels become taskbar client coordinates at the native boundary.
                using(PixelCoordinates.Physical()){
                var point=new Native.Point {X=bounds.Left,Y=bounds.Top};Native.ScreenToClient(parent,ref point);
                Native.SetOpaqueLayered(strip.Handle);
                strip.Show();if(Native.ScreenRect(strip.Handle)!=bounds)Native.SetWindowPos(strip.Handle,IntPtr.Zero,point.X,point.Y,bounds.Width,bounds.Height,0x10|0x40);
                }
                strip.SetLight(layout.Light);
                strip.Invalidate();
                Status="attached";return true;
            }catch{HideStrip();Status="layout-unavailable";return false;}finally{scanning=false;}
        }
        Layout ReadLayout(){using(PixelCoordinates.Physical())return ReadLayoutPhysical();}
        Layout ReadLayoutPhysical(){
            var handle=Native.FindWindow("Shell_TrayWnd",null);if(handle==IntPtr.Zero||!Native.IsWindowVisible(handle))return null;
            var bar=Native.ScreenRect(handle);var screen=Forms.Screen.FromHandle(handle).Bounds;
            if(bar.Width<=bar.Height||bar.Height<24||bar.Height>160||bar.Top>=screen.Bottom-4||bar.Bottom<=screen.Top+4)return null;
            var items=AutomationElement.FromHandle(handle).FindAll(TreeScope.Descendants,Condition.TrueCondition);var occupied=new List<Rectangle>();int ownPid=System.Diagnostics.Process.GetCurrentProcess().Id;
            for(int i=0;i<items.Count;i++){
                var c=items[i].Current;if(c.ProcessId==ownPid||c.IsOffscreen)continue;
                var r=c.BoundingRectangle;if(r.IsEmpty||r.Width<=0||r.Height<=0)continue;
                bool button=c.ControlType==ControlType.Button||c.ControlType==ControlType.CheckBox||c.ControlType==ControlType.MenuItem;
                bool custom=c.NativeWindowHandle!=0&&c.ControlType==ControlType.Pane&&r.Width<bar.Width*0.5&&c.ClassName!="ReBarWindow32"&&c.ClassName!="MSTaskSwWClass";
                if(button||custom){var rect=Rectangle.FromLTRB((int)Math.Floor(r.Left),(int)Math.Floor(r.Top),(int)Math.Ceiling(r.Right),(int)Math.Ceiling(r.Bottom));if(rect.IntersectsWith(bar))occupied.Add(rect);}
            }
            // With no actionable taskbar controls we cannot establish a safe empty slot.
            if(occupied.Count==0)return null;
            bool light=false;using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){light=key!=null&&Convert.ToInt32(key.GetValue("SystemUsesLightTheme",0))!=0;}
            int dpi=96;try{dpi=(int)Native.GetDpiForWindow(handle);}catch(EntryPointNotFoundException){}return new Layout {Handle=handle,Bar=bar,Occupied=occupied,Dpi=dpi>0?dpi:96,Light=light};
        }
        public static Rectangle FindSpace(Rectangle bar,IEnumerable<Rectangle> occupied,int width,int height,int margin,bool preferLeft=false){
            if(width<=0||height<=0||height>bar.Height)return Rectangle.Empty;
            int cursor=bar.Left+margin;var gaps=new List<Rectangle>();
            foreach(var r in occupied.Where(r=>r.IntersectsWith(bar)).OrderBy(r=>r.Left)){
                int edge=Math.Max(bar.Left,Math.Min(bar.Right,r.Left));if(edge-margin-cursor>=width)gaps.Add(new Rectangle(preferLeft?cursor:edge-margin-width,bar.Top+(bar.Height-height)/2,width,height));cursor=Math.Max(cursor,r.Right+margin);
            }
            if(bar.Right-margin-cursor>=width)gaps.Add(new Rectangle(preferLeft?cursor:bar.Right-margin-width,bar.Top+(bar.Height-height)/2,width,height));
            return preferLeft?gaps.FirstOrDefault():gaps.LastOrDefault();
        }
        void HideStrip(){if(strip!=null&&!strip.IsDisposed)strip.Hide();}
        static Color? SampleBackground(Rectangle bounds,IntPtr taskbar){
            var point=new System.Drawing.Point(bounds.Left-2,bounds.Top+bounds.Height/2);var hit=Native.Hit(point);if(hit!=taskbar&&!Native.IsChild(taskbar,hit))return null;
            try{using(PixelCoordinates.Physical())using(var bitmap=new Bitmap(1,1))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(point.X,point.Y,0,0,bitmap.Size);return bitmap.GetPixel(0,0);}}
            catch{return null;}
        }
        internal async Task<HoverEvidence> TestHover(string screenshot){return strip==null?new HoverEvidence():await strip.TestHover(screenshot,async()=>{await UpdatePlacement();});}
        internal sealed class HoverEvidence {public bool BlankAreaHit,ShowAlways,PopupVisible,ForegroundUnchanged,CursorRestored,PointerInside,SurvivesLayoutCheck,LeaveHides;public int ShowAttempts,MouseMoves;public string Text;public Rectangle PopupBounds;}
        internal void TestClick(){if(strip!=null)strip.TestClick();}
        internal void TestMenu(){if(strip!=null)menu.Show(strip,new Point(5,0));}
        internal object LayoutEvidence(){var layout=ReadLayout();return new {Status,Visible,RequestedWidth,Dpi,Columns=Columns.Select(c=>new {c.Header,c.Value,c.Width}),Parent=parent.ToInt64(),ActualParent=strip==null?0:Native.GetParent(strip.Handle).ToInt64(),Bounds,StripContext=strip==null?0:Native.GetWindowDpiAwarenessContext(strip.Handle).ToInt64(),ParentContext=Native.GetWindowDpiAwarenessContext(parent).ToInt64(),Client=strip==null?Size.Empty:strip.ClientSize,CenterHit=Native.Hit(new System.Drawing.Point(Bounds.Left+2,Bounds.Top+2)).ToInt64(),OwnHandle=Handle.ToInt64(),Bar=layout==null?Rectangle.Empty:layout.Bar,Occupied=layout==null?new List<Rectangle>():layout.Occupied,Overlaps=layout==null||layout.Occupied.Any(r=>r.IntersectsWith(Bounds))};}
        internal void Capture(string path,bool wholeBar){var rect=wholeBar?Native.ScreenRect(parent):Bounds;if(rect.IsEmpty)return;using(PixelCoordinates.Physical())using(var bmp=new Bitmap(rect.Width,rect.Height))using(var g=Graphics.FromImage(bmp)){g.CopyFromScreen(rect.Location,Point.Empty,rect.Size);bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
        internal int VisibleGreenPixels(){var rect=Bounds;if(rect.IsEmpty)return 0;int count=0;using(PixelCoordinates.Physical())using(var bitmap=new Bitmap(rect.Width,rect.Height))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(rect.Location,Point.Empty,rect.Size);for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var color=bitmap.GetPixel(x,y);if(color.G>100&&color.G>color.R+40&&color.G>color.B+20)count++;}}return count;}
        internal void CaptureRendered(string path){if(strip==null)return;using(PixelCoordinates.Window(strip.Handle))using(var bitmap=new Bitmap(strip.ClientSize.Width,strip.ClientSize.Height)){strip.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
        void CloseStrip(){if(strip!=null){strip.Dispose();strip=null;}parent=IntPtr.Zero;}
        public void Dispose(){disposed=true;enabled=false;timer.Stop();timer.Dispose();systemTimer.Stop();systemTimer.Dispose();if(!sampling)sampler.Dispose();CloseStrip();}
        sealed class Layout {public IntPtr Handle;public Rectangle Bar;public List<Rectangle> Occupied;public int Dpi;public bool Light;}
        sealed class Strip:Forms.Form {
            readonly Forms.ToolTip tooltip=new Forms.ToolTip {AutoPopDelay=15000,InitialDelay=450,ReshowDelay=100,ShowAlways=true};
            readonly Forms.Timer hoverTimer=new Forms.Timer {Interval=100};
            readonly Forms.ContextMenuStrip contextMenu;
            List<TaskbarColumn> columns;string tooltipText="";bool light,hover,backgroundReady;readonly Font textFont,titleFont;readonly float scale;
            int showAttempts,mouseMoves;
            bool tipShown,suppressTip;DateTime hoverSince;
            protected override bool ShowWithoutActivation {get{return true;}}
            protected override Forms.CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
            public Strip(int dpi,Action open,Forms.ContextMenuStrip menu){Text="CodexQuotaTaskbar";Name="CodexQuotaTaskbar";ShowInTaskbar=false;FormBorderStyle=Forms.FormBorderStyle.None;AutoScaleMode=Forms.AutoScaleMode.None;BackColor=Color.FromArgb(32,32,32);ContextMenuStrip=menu;Cursor=Forms.Cursors.Hand;DoubleBuffered=true;scale=dpi/96f;textFont=new Font("Microsoft YaHei UI",12f*scale,FontStyle.Regular,GraphicsUnit.Pixel);titleFont=new Font("Microsoft YaHei UI",12f*scale,FontStyle.Bold,GraphicsUnit.Pixel);
                contextMenu=menu;hoverTimer.Tick+=(s,e)=>PollHover();VisibleChanged+=(s,e)=>{if(Visible)hoverTimer.Start();else{hoverTimer.Stop();hover=false;HideHoverTip();}};
                MouseClick+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)open();};MouseDown+=(s,e)=>HideHoverTip();
                MouseMove+=(s,e)=>mouseMoves++;
            }
            void PollHover(){
                var position=PixelCoordinates.Cursor;bool inside=Visible&&!contextMenu.Visible&&Native.ScreenRect(Handle).Contains(position)&&Native.Hit(position)==Handle;
                if(!inside){if(hover){hover=false;HideHoverTip();Invalidate();}suppressTip=false;return;}
                if(!hover){hover=true;hoverSince=DateTime.UtcNow;Invalidate();}
                if(!tipShown&&!suppressTip&&(DateTime.UtcNow-hoverSince).TotalMilliseconds>=450)ShowHoverTip();
            }
            internal bool NeedsBackground(bool value){return !backgroundReady||light!=value;}
            internal void SetBackground(bool value,Color background,bool ready){light=value;backgroundReady=ready;BackColor=background;Invalidate();}
            public void SetLight(bool value){if(light!=value){light=value;Invalidate();}}
            public void UpdateText(List<TaskbarColumn> value,string detail){columns=value;tooltipText=detail;Invalidate();}
            void ShowHoverTip(){
                if(columns==null||IsDisposed)return;
                showAttempts++;
                tipShown=true;
                int height=Forms.TextRenderer.MeasureText(tooltipText,SystemFonts.MessageBoxFont,new Size((int)(600*scale),0),Forms.TextFormatFlags.WordBreak).Height+24;
                using(PixelCoordinates.Window(Handle))tooltip.Show(tooltipText,this,new Point(4,-height-8),15000);
            }
            void HideHoverTip(){tooltip.Hide(this);tipShown=false;suppressTip=true;}
            internal Color NormalInk {get{return light?Color.FromArgb(20,122,63):Color.FromArgb(70,235,135);}}
            internal Color TokenInk {get{return NormalInk;}}
            internal void TestClick(){OnMouseClick(new Forms.MouseEventArgs(Forms.MouseButtons.Left,1,5,5,0));}
            internal async Task<HoverEvidence> TestHover(string screenshot,Func<Task> layoutCheck){
                var rect=Native.ScreenRect(Handle);var point=new Point(rect.Left+2,rect.Top+2);var old=PixelCoordinates.Cursor;var foreground=Native.GetForegroundWindow();
                var outside=new Point(rect.Left+2,rect.Top-4);
                var result=new HoverEvidence {ShowAlways=tooltip.ShowAlways,Text=tooltipText,BlankAreaHit=Native.Hit(point)==Handle};
                if(!result.BlankAreaHit)return result;
                try{
                    PixelCoordinates.Cursor=point;
                    var property=typeof(Forms.ToolTip).GetProperty("Handle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                    IntPtr tipHandle;using(PixelCoordinates.Window(Handle))tipHandle=(IntPtr)property.GetValue(tooltip,null);
                    for(int attempt=0;attempt<16&&!Native.IsWindowVisible(tipHandle);attempt++){await Task.Delay(100);using(PixelCoordinates.Window(Handle))tipHandle=(IntPtr)property.GetValue(tooltip,null);}
                    result.PointerInside=hover;result.ShowAttempts=showAttempts;result.MouseMoves=mouseMoves;
                    result.PopupVisible=Native.IsWindowVisible(tipHandle);result.PopupBounds=Native.ScreenRect(tipHandle);result.ForegroundUnchanged=foreground==Native.GetForegroundWindow();
                    if(result.PopupVisible&&!result.PopupBounds.IsEmpty)using(PixelCoordinates.Physical())using(var bitmap=new Bitmap(result.PopupBounds.Width,result.PopupBounds.Height))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(result.PopupBounds.Location,Point.Empty,bitmap.Size);bitmap.Save(screenshot,System.Drawing.Imaging.ImageFormat.Png);}
                    await layoutCheck();await Task.Delay(150);result.SurvivesLayoutCheck=Native.IsWindowVisible(tipHandle);
                    PixelCoordinates.Cursor=outside;await Task.Delay(250);result.LeaveHides=!Native.IsWindowVisible(tipHandle);
                }finally{tooltip.Hide(this);if(PixelCoordinates.Cursor==point||PixelCoordinates.Cursor==outside){PixelCoordinates.Cursor=old;result.CursorRestored=true;}}
                return result;
            }
            protected override void OnPaint(Forms.PaintEventArgs e){
                base.OnPaint(e);if(columns==null)return;
                var ink=NormalInk;var warning=light?Color.FromArgb(145,83,13):Color.FromArgb(255,196,89);
                if(hover)using(var brush=new SolidBrush(light?Color.FromArgb(225,229,234):Color.FromArgb(46,51,58)))e.Graphics.FillRectangle(brush,ClientRectangle);
                var flags=Forms.TextFormatFlags.Left|Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.EndEllipsis|Forms.TextFormatFlags.NoPadding|Forms.TextFormatFlags.SingleLine;
                int position=4,half=ClientSize.Height/2;
                using(var pen=new Pen(light?Color.FromArgb(180,189,198):Color.FromArgb(70,78,87)))for(int i=0;i<columns.Count;i++){var column=columns[i];int left=(int)Math.Round(position*scale),right=(int)Math.Round((position+column.Width)*scale);var top=new Rectangle(left,0,right-left,half);var bottom=new Rectangle(left,half,top.Width,ClientSize.Height-half);Forms.TextRenderer.DrawText(e.Graphics,column.Header,column.Bold?titleFont:textFont,top,ink,flags);Forms.TextRenderer.DrawText(e.Graphics,column.Value,textFont,bottom,column.QuotaWarning?warning:ink,flags);position+=column.Width;if(i+1<columns.Count){int x=(int)Math.Round((position+TaskbarColumns.Gap/2.0)*scale);e.Graphics.DrawLine(pen,x,(int)Math.Round(8*scale),x,ClientSize.Height-(int)Math.Round(8*scale));position+=TaskbarColumns.Gap;}}
            }
            protected override void Dispose(bool disposing){if(disposing){hoverTimer.Stop();hoverTimer.Dispose();tooltip.Dispose();textFont.Dispose();titleFont.Dispose();}base.Dispose(disposing);}
        }
        static class Native {
            [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
            [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
            [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string name,string title);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out Rect rect);
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr handle);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
            [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child,IntPtr parent);
            [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr handle);
            [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent,IntPtr child);
            [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr handle,ref Point point);
            [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int w,int h,uint flags);
            [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
            [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
            [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr handle,int index);
            [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr handle,int index,int value);
            [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr handle,uint color,byte alpha,uint flags);
            public static void SetOpaqueLayered(IntPtr handle){int style=GetWindowLong(handle,-20);if((style&0x80000)==0){SetWindowLong(handle,-20,style|0x80000);SetLayeredWindowAttributes(handle,0,255,2);}}
            public static void SetChild(IntPtr handle){int style=GetWindowLong(handle,-16);SetWindowLong(handle,-16,(style&~unchecked((int)0x80000000))|0x40000000);}
            public static Rectangle ScreenRect(IntPtr handle){using(PixelCoordinates.Physical()){Rect r;return GetWindowRect(handle,out r)?Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom):Rectangle.Empty;}}
            public static IntPtr Hit(System.Drawing.Point point){using(PixelCoordinates.Physical())return WindowFromPoint(new Point {X=point.X,Y=point.Y});}
        }
    }
}

