using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexQuotaMonitor {
    // Scale layout and font sizes before rendering, rather than transforming rendered text.
    sealed class DisplayLayout {
        readonly ConditionalWeakTable<FrameworkElement,Metrics> originals=new ConditionalWeakTable<FrameworkElement,Metrics>();
        sealed class Metrics {
            public double Width,Height,Font;public Thickness Margin,Padding,Border;public CornerRadius Corners;
            public GridLength[] Rows,Columns;
        }
        Metrics Read(FrameworkElement element){
            var m=new Metrics {Width=element.Width,Height=element.Height,Margin=element.Margin,Font=double.NaN};
            var text=element as TextBlock;if(text!=null)m.Font=text.FontSize;
            var control=element as Control;if(control!=null){m.Font=control.FontSize;m.Padding=control.Padding;m.Border=control.BorderThickness;}
            var border=element as Border;if(border!=null){m.Padding=border.Padding;m.Border=border.BorderThickness;m.Corners=border.CornerRadius;}
            var grid=element as Grid;if(grid!=null){m.Rows=new GridLength[grid.RowDefinitions.Count];m.Columns=new GridLength[grid.ColumnDefinitions.Count];for(int i=0;i<m.Rows.Length;i++)m.Rows[i]=grid.RowDefinitions[i].Height;for(int i=0;i<m.Columns.Length;i++)m.Columns[i]=grid.ColumnDefinitions[i].Width;}
            return m;
        }
        void Collect(DependencyObject node,List<FrameworkElement> elements){
            var element=node as FrameworkElement;if(element!=null){originals.GetValue(element,Read);elements.Add(element);}
            foreach(var child in LogicalTreeHelper.GetChildren(node)){var dependency=child as DependencyObject;if(dependency!=null)Collect(dependency,elements);}
        }
        public void Font(TextBlock text,double value){originals.GetValue(text,Read).Font=value;}
        public void Apply(Window window,double ratio,bool mini){
            var source=PresentationSource.FromVisual(window);double dpi=source==null||source.CompositionTarget==null?1:source.CompositionTarget.TransformToDevice.M11;
            Func<double,double> size=n=>double.IsNaN(n)?n:Math.Round(n*ratio*dpi,MidpointRounding.AwayFromZero)/dpi;
            Func<Thickness,Thickness> thickness=t=>new Thickness(size(t.Left),size(t.Top),size(t.Right),size(t.Bottom));
            Func<double,double> line=n=>n==0?0:Math.Max(1/dpi,size(n));
            var elements=new List<FrameworkElement>();Collect(window,elements);
            foreach(var element in elements){var m=originals.GetValue(element,Read);element.UseLayoutRounding=true;element.SnapsToDevicePixels=true;element.Margin=thickness(m.Margin);
                if(element!=window){element.Width=size(m.Width);element.Height=size(m.Height);}
                var text=element as TextBlock;if(text!=null)text.FontSize=Math.Max(5/dpi,size(m.Font));
                var control=element as Control;if(control!=null){control.FontSize=Math.Max(5/dpi,size(m.Font));control.Padding=thickness(m.Padding);control.BorderThickness=new Thickness(line(m.Border.Left),line(m.Border.Top),line(m.Border.Right),line(m.Border.Bottom));}
                var border=element as Border;if(border!=null){border.Padding=thickness(m.Padding);border.BorderThickness=new Thickness(line(m.Border.Left),line(m.Border.Top),line(m.Border.Right),line(m.Border.Bottom));border.CornerRadius=new CornerRadius(size(m.Corners.TopLeft),size(m.Corners.TopRight),size(m.Corners.BottomRight),size(m.Corners.BottomLeft));}
                var grid=element as Grid;if(grid!=null){for(int i=0;i<m.Rows.Length;i++)grid.RowDefinitions[i].Height=m.Rows[i].IsAbsolute?new GridLength(size(m.Rows[i].Value)):m.Rows[i];for(int i=0;i<m.Columns.Length;i++)grid.ColumnDefinitions[i].Width=m.Columns[i].IsAbsolute?new GridLength(size(m.Columns[i].Value)):m.Columns[i];}
            }
            var surface=(Border)window.FindName("SurfaceBorder");surface.Width=window.Width=Math.Ceiling(390*ratio);surface.Height=window.Height=Math.Ceiling((mini?108:548)*ratio);
        }
    }
}
