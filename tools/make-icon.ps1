param([Parameter(Mandatory=$true)][string]$SourceImage)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$assets=Join-Path $root 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Copy-Item -LiteralPath $SourceImage -Destination (Join-Path $assets 'app-icon.png')
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
public static class IconPack {
 public static void Build(string input,string output,string assets) {
  int[] sizes={16,20,24,32,40,48,64,128,256};var frames=new List<byte[]>();
  using(var source=Image.FromFile(input))foreach(int size in sizes) {
   using(var bmp=new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
    using(var g=Graphics.FromImage(bmp)){g.Clear(Color.Transparent);g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(source,new Rectangle(0,0,size,size),new Rectangle(0,0,source.Width,source.Height),GraphicsUnit.Pixel);}
    bmp.Save(Path.Combine(assets,"icon-"+size+".png"),ImageFormat.Png);
    using(var stream=new MemoryStream()) {
     if(size==256)bmp.Save(stream,ImageFormat.Png);
     else using(var writer=new BinaryWriter(stream)) {
      int maskStride=((size+31)/32)*4;
      writer.Write(40);writer.Write(size);writer.Write(size*2);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(0);writer.Write(size*size*4+maskStride*size);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
      for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){var c=bmp.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);}
      for(int y=size-1;y>=0;y--){var mask=new byte[maskStride];for(int x=0;x<size;x++)if(bmp.GetPixel(x,y).A==0)mask[x/8]|=(byte)(1<<(7-x%8));writer.Write(mask);}
      writer.Flush();frames.Add(stream.ToArray());continue;
     }
     frames.Add(stream.ToArray());
    }
   }
  }
  using(var writer=new BinaryWriter(File.Create(output))) {
   writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
   for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}
   foreach(var frame in frames)writer.Write(frame);
  }
 }
}
'@
[IconPack]::Build((Join-Path $assets 'app-icon.png'),(Join-Path $assets 'app-icon.ico'),$assets)
Write-Output (Join-Path $assets 'app-icon.ico')
