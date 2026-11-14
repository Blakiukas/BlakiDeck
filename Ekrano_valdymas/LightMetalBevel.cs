using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;

// Continuous skin plus the measured rounded-edge lighting delta from Blender.
internal static class LightMetalBevel
{
 internal static int CompleteFaceCoverageChecks {get;private set;}
 internal const double Width=3.5;
 private static readonly Dictionary<string,EdgePixels> edges=new();
 private static readonly Queue<string> order=new();
 private static long cachedBytes;
 private static readonly Dictionary<string,byte[]> faceSamples=new();
 private static readonly Queue<string> faceSampleOrder=new();
 private static long faceSampleBytes;
 private const long FaceBudget=32L*1024*1024;
 private const long EdgeBudget=48L*1024*1024;
 internal static void ApplyResourceMode()
 {
  while(faceSampleOrder.Count>0&&faceSampleBytes>FaceBudget){var key=faceSampleOrder.Dequeue();faceSampleBytes-=faceSamples[key].Length;faceSamples.Remove(key);}
  while(order.Count>0&&cachedBytes>EdgeBudget){var key=order.Dequeue();cachedBytes-=edges[key].Bytes;edges.Remove(key);}
 }
 private static readonly Dictionary<double,byte[]> coverageTables=new();
 // Keep only nontransparent runs. A large frame used to evict the entire
 // cache with its empty centre. The live WPF drawing owns the decoded bitmap;
 // a weak reference reuses it without pinning another full-sized surface.
 private sealed class EdgePixels
 {
  private readonly int width,height;
  private readonly double dpiX,dpiY;
  private readonly int[] runs;
  private readonly byte[] ink;
  private readonly byte[]? diagnosticDigest;
  private WeakReference<BitmapSource> live;
  internal long Bytes=>ink.Length+(long)runs.Length*sizeof(int);
  internal EdgePixels(BitmapSource bitmap,byte[] pixels)
  {
   width=bitmap.PixelWidth;height=bitmap.PixelHeight;dpiX=bitmap.DpiX;dpiY=bitmap.DpiY;live=new(bitmap);
   diagnosticDigest=DiagnosticState.Root!=null?System.Security.Cryptography.SHA256.HashData(pixels):null;
   var spans=new List<int>();using var data=new System.IO.MemoryStream();
   var words=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,uint>(pixels.AsSpan());
   int p=0;while(p<words.Length){while(p<words.Length&&words[p]==0)p++;int start=p;while(p<words.Length&&words[p]!=0)p++;
    if(p>start){int offset=start*4,count=(p-start)*4;spans.Add(offset);spans.Add(count);data.Write(pixels,offset,count);}}
   runs=spans.ToArray();ink=data.ToArray();
  }
  internal BitmapSource Get()
  {
   if(live.TryGetTarget(out var bitmap))return bitmap;
   var timing=System.Diagnostics.Stopwatch.StartNew();
   var pixels=Decode();
   bitmap=BitmapSource.Create(width,height,dpiX,dpiY,PixelFormats.Pbgra32,null,pixels,width*4);bitmap.Freeze();live.SetTarget(bitmap);
   InteractionDiagnostics.Log($"Bevel restore size={width}x{height} ms={timing.Elapsed.TotalMilliseconds:0.###}");return bitmap;
  }
  private byte[] Decode()
  {
   var pixels=new byte[checked(width*height*4)];int source=0;
   for(int i=0;i<runs.Length;i+=2){Buffer.BlockCopy(ink,source,pixels,runs[i],runs[i+1]);source+=runs[i+1];}
   return pixels;
  }
  internal void Verify()
  {
   if(diagnosticDigest==null||!System.Security.Cryptography.SHA256.HashData(Decode()).AsSpan().SequenceEqual(diagnosticDigest))
    throw new InvalidOperationException("Edge cache changed raster pixels");
  }
 }
 internal static int VerifyCachedPixels(){foreach(var edge in edges.Values)edge.Verify();return edges.Count;}
 internal static double Sampling(FrameworkElement owner)
 {
  double scale=VisualTreeHelper.GetDpi(owner).DpiScaleX;
  if(Window.GetWindow(owner) is Window window&&window!=owner)
   scale*=owner.TransformToAncestor(window).TransformBounds(new Rect(0,0,1,1)).Width;
  return Math.Max(2,Math.Ceiling(scale*1.5-1e-6));
 }
 internal static void Draw(DrawingContext dc,Geometry shape,FrameworkElement owner,double profileWidth=Width,bool completeFace=false,double luminance=1)
 {
  if(CommonMetal.NoSkin){if(completeFace)dc.DrawGeometry(CommonMetal.ForElement(owner),null,shape);return;}
  RenderOptions.SetBitmapScalingMode(owner,BitmapScalingMode.HighQuality);
  var flat=shape.GetFlattenedPathGeometry(.01,ToleranceType.Absolute);var bounds=flat.Bounds;
  if(bounds.IsEmpty||bounds.Width<=0||bounds.Height<=0)return;
  // Layout arithmetic may return832 or832.0000000000002 for the same
  // contour. Ignore sub-millionth-unit noise, not actual pixel/skin offsets.
  double sampling=Sampling(owner);
  string key=sampling+"|"+completeFace+"|"+luminance+"|"+profileWidth+"|"+CommonMetal.FinishName+"|"+((IFormattable)CommonMetal.Viewport(owner)).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+"|"+((IFormattable)flat).ToString("F6",System.Globalization.CultureInfo.InvariantCulture);
  BitmapSource bitmap;
  if(edges.TryGetValue(key,out var cached))bitmap=cached.Get();
  else {
   var generation=System.Diagnostics.Stopwatch.StartNew();int evicted=0;
   int width=Math.Max(1,(int)Math.Ceiling(bounds.Width*sampling-1e-7)),height=Math.Max(1,(int)Math.Ceiling(bounds.Height*sampling-1e-7)),stride=width*4;
   var pixels=new byte[stride*height];var distance=new float[width*height];Array.Fill(distance,float.PositiveInfinity);
   var band=shape.GetWidenedPathGeometry(new Pen(Brushes.Black,2*(profileWidth+2)),.01,ToleranceType.Absolute);band.Freeze();
   string sampleKey=CommonMetal.FinishName+"|"+((IFormattable)CommonMetal.Viewport(owner)).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+"|"+((IFormattable)bounds).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+"|"+width+"|"+height;
   byte[] skin=Array.Empty<byte>();
   bool reused=completeFace&&faceSamples.TryGetValue(sampleKey,out skin!);
   if(!reused){
   var skinVisual=new DrawingVisual();using(var paint=skinVisual.RenderOpen()){
    paint.PushTransform(new TranslateTransform(-bounds.X,-bounds.Y));
    if(completeFace){var sample=bounds;sample.Inflate(1,1);paint.DrawRectangle(CommonMetal.ForBevel(owner),null,sample);}else paint.DrawGeometry(CommonMetal.ForBevel(owner),null,band);paint.Pop();
   }
   var skinBitmap=new RenderTargetBitmap(width,height,96*sampling,96*sampling,PixelFormats.Pbgra32);skinBitmap.Render(skinVisual);
   skin=new byte[pixels.Length];skinBitmap.CopyPixels(skin,stride,0);
   if(completeFace){
    // The sampled field has resampling alpha at its cropped band boundaries.
    // Metal is opaque: recover its color before applying the sole shape mask.
    for(int i=0;i<skin.Length;i+=4){int a=skin[i+3];if(a>0&&a<255)for(int c=0;c<3;c++)skin[i+c]=(byte)Math.Min(255,(skin[i+c]*255+a/2)/a);skin[i+3]=255;}
    while(faceSampleOrder.Count>0&&faceSampleBytes+skin.Length>FaceBudget){var old=faceSampleOrder.Dequeue();faceSampleBytes-=faceSamples[old].Length;faceSamples.Remove(old);}
    if(skin.Length<=FaceBudget){faceSamples[sampleKey]=skin;faceSampleOrder.Enqueue(sampleKey);faceSampleBytes+=skin.Length;}
   }
   }
   if(completeFace)Buffer.BlockCopy(skin,0,pixels,0,skin.Length);
   var atlas=new FormatConvertedBitmap(MusicConsoleControl.Bitmap("machined-edge-atlas.png",finish:CommonMetal.IsLight?"light":"dark"),PixelFormats.Bgra32,null,0);
   var response=new byte[360*32*4];atlas.CopyPixels(response,360*4,0);
   double skinMs=generation.Elapsed.TotalMilliseconds;
   var correction=new double[3];
   foreach(var figure in flat.Figures){
    var points=new List<Point>{figure.StartPoint};
    foreach(var segment in figure.Segments){if(segment is PolyLineSegment poly)points.AddRange(poly.Points);else if(segment is LineSegment line)points.Add(line.Point);}
    if(points.Count>1&&(points[0]-points[^1]).Length<.001)points.RemoveAt(points.Count-1);
    double area=0;for(int i=0;i<points.Count;i++){var a=points[i];var b=points[(i+1)%points.Count];area+=a.X*b.Y-b.X*a.Y;}
    for(int i=0;i<points.Count;i++){
     var a=points[i];var b=points[(i+1)%points.Count];var tangent=b-a;double length2=tangent.LengthSquared;if(length2<1e-8)continue;
     var normal=new Vector(-tangent.Y,tangent.X)*(area>=0?1:-1);normal.Normalize();
     int left=Math.Max(0,(int)Math.Floor((Math.Min(a.X,b.X)-profileWidth-bounds.X)*sampling));
     int right=Math.Min(width-1,(int)Math.Ceiling((Math.Max(a.X,b.X)+profileWidth-bounds.X)*sampling));
     int top=Math.Max(0,(int)Math.Floor((Math.Min(a.Y,b.Y)-profileWidth-bounds.Y)*sampling));
     int bottom=Math.Min(height-1,(int)Math.Ceiling((Math.Max(a.Y,b.Y)+profileWidth-bounds.Y)*sampling));
     for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++){
      var point=new Point(bounds.X+(x+.5)/sampling,bounds.Y+(y+.5)/sampling);double t=Math.Clamp(Vector.Multiply(point-a,tangent)/length2,0,1);
      var delta=point-(a+tangent*t);double squared=delta.LengthSquared;int index=y*width+x;
      if(squared>=distance[index]||squared>(profileWidth+.5/sampling)*(profileWidth+.5/sampling))continue;
      distance[index]=(float)squared;double d=Math.Sqrt(squared);
      var n=d>1e-6&&Vector.Multiply(delta,normal)>0?delta/d:normal;
      double angle=(Math.Atan2(n.Y,-n.X)*180/Math.PI+360)%360;int angle0=(int)angle,angle1=(angle0+1)%360;double angular=angle-angle0;
      double row=Math.Clamp(d/profileWidth*32-.5,0,31);int r0=(int)row,r1=Math.Min(31,r0+1);double radial=row-r0;
      // The sampled swatch ends just inside the rounded shoulder, with a
      // residual lighting delta. Join the flat face with zero value AND slope
      // instead of cutting that residual off at the inner edge of the band.
      double join=Math.Clamp((d/profileWidth-.65)/.35,0,1);
      double shoulder=1-join*join*(3-2*join);
      double alpha=Math.Clamp((profileWidth-d)*sampling+.5,0,1);int pixel=index*4;
      double coverageAlpha=0;
      for(int c=0;c<3;c++){
       double v0=response[(r0*360+angle0)*4+c]*(1-angular)+response[(r0*360+angle1)*4+c]*angular;
       double v1=response[(r1*360+angle0)*4+c]*(1-angular)+response[(r1*360+angle1)*4+c]*angular;
       // Preserve spatial skin contrast. A ratio exaggerated highlights/shadows
       // wherever the flat reference was dark, creating a separate black rim.
       double current=skin[pixel+c];double target=Math.Clamp(current+2*(v0*(1-radial)+v1*radial-128)*shoulder,0,255);
       correction[c]=target-current;
       coverageAlpha=Math.Max(coverageAlpha,correction[c]>0?correction[c]/Math.Max(1,255-current):-correction[c]/Math.Max(1,current));
      }
      // Paint only the lighting correction over the original material. An
      // opaque resampled copy of the skin made a second texture seam/rim.
      if(completeFace){
       for(int c=0;c<3;c++)pixels[pixel+c]=(byte)Math.Round(Math.Clamp(skin[pixel+c]+correction[c]*alpha,0,255));
       pixels[pixel+3]=255;
      }else{
       for(int c=0;c<3;c++)pixels[pixel+c]=(byte)Math.Round(Math.Clamp(skin[pixel+c]*coverageAlpha+correction[c],0,255)*alpha);
       pixels[pixel+3]=(byte)Math.Round(255*coverageAlpha*alpha);
      }
     }
    }
   }
   double kernelMs=generation.Elapsed.TotalMilliseconds-skinMs;
   // Resolve contour coverage at the SAME sampling scale as its lighting.
   // Native-scale clipping after bitmap reduction left a second jagged edge.
   var maskVisual=new DrawingVisual();using(var paint=maskVisual.RenderOpen()){
    paint.PushTransform(new TranslateTransform(-bounds.X,-bounds.Y));paint.DrawGeometry(Brushes.White,null,shape);paint.Pop();
   }
   var mask=new RenderTargetBitmap(width,height,96*sampling,96*sampling,PixelFormats.Pbgra32);mask.Render(maskVisual);
   var coverage=new byte[pixels.Length];mask.CopyPixels(coverage,stride,0);
   if(!coverageTables.TryGetValue(luminance,out var table)){
    table=new byte[65536];for(int a=0;a<256;a++)for(int v=0;v<256;v++)table[a*256+v]=(byte)Math.Round(v*a/255d*luminance);coverageTables[luminance]=table;
   }
   for(int i=0;i<pixels.Length;i+=4)if(pixels[i+3]!=0){
    int a=coverage[i+3],offset=a*256;
    pixels[i]=table[offset+pixels[i]];pixels[i+1]=table[offset+pixels[i+1]];pixels[i+2]=table[offset+pixels[i+2]];
    pixels[i+3]=(byte)((pixels[i+3]*a+127)/255);
   }
   if(completeFace&&DiagnosticState.Root!=null){
    for(int i=0;i<pixels.Length;i+=4)
     if(pixels[i+3]!=coverage[i+3]||pixels[i]>pixels[i+3]||pixels[i+1]>pixels[i+3]||pixels[i+2]>pixels[i+3])
      throw new InvalidOperationException($"Connected metal must have exactly one contour coverage mask and valid premultiplied ink: pixel {i/4}, alpha {pixels[i+3]}, mask {coverage[i+3]}, rgb {pixels[i]}/{pixels[i+1]}/{pixels[i+2]}");
    CompleteFaceCoverageChecks++;
   }
   bitmap=BitmapSource.Create(width,height,96*sampling,96*sampling,PixelFormats.Pbgra32,null,pixels,stride);bitmap.Freeze();
   double maskMs=generation.Elapsed.TotalMilliseconds-skinMs-kernelMs;
   var entry=new EdgePixels(bitmap,pixels);long bytes=entry.Bytes;
   while(order.Count>0&&cachedBytes+bytes>EdgeBudget){var old=order.Dequeue();cachedBytes-=edges[old].Bytes;edges.Remove(old);evicted++;}
   if(bytes<=EdgeBudget){edges[key]=entry;order.Enqueue(key);cachedBytes+=bytes;}
   InteractionDiagnostics.Log($"Bevel miss owner={owner.GetType().Name}:{owner.Name} visible={owner.IsVisible} arranged={owner.RenderSize} viewport={CommonMetal.Viewport(owner)} size={width}x{height} ms={generation.Elapsed.TotalMilliseconds:0.###} evicted={evicted} cacheBytes={cachedBytes}");
   InteractionDiagnostics.Log($"Bevel stages skinMs={skinMs:0.###} kernelMs={kernelMs:0.###} maskMs={maskMs:0.###} packMs={generation.Elapsed.TotalMilliseconds-skinMs-kernelMs-maskMs:0.###}");
  }
  dc.DrawImage(bitmap,new Rect(bounds.Location,new Size(bitmap.Width,bitmap.Height)));
 }
 internal static Brush Background(FrameworkElement e,Brush material)
 {
  if(CommonMetal.NoSkin)return material;
  if(e is not Border border||e is MetalRailBase||e.ActualHeight<12)return material;
  var bounds=new Rect(0,0,e.ActualWidth,e.ActualHeight);
  var group=new DrawingGroup();using(var dc=group.Open()){dc.DrawRectangle(material,null,bounds);Draw(dc,Outline(bounds,border.CornerRadius),e);}
  RenderOptions.SetBitmapScalingMode(group,BitmapScalingMode.HighQuality);
  group.Freeze();var brush=new DrawingBrush(group){Stretch=Stretch.Fill};brush.Freeze();return brush;
 }
 internal static Geometry Outline(Rect r,CornerRadius corners)
 {
  double limit=Math.Min(r.Width,r.Height)/2,tl=Math.Min(limit,corners.TopLeft),tr=Math.Min(limit,corners.TopRight),br=Math.Min(limit,corners.BottomRight),bl=Math.Min(limit,corners.BottomLeft);
  var shape=new StreamGeometry();using(var g=shape.Open()){
   void Corner(Point end,double radius){if(radius>0)g.ArcTo(end,new Size(radius,radius),0,false,SweepDirection.Clockwise,true,false);else g.LineTo(end,true,false);}
   g.BeginFigure(new Point(r.Left+tl,r.Top),true,true);g.LineTo(new Point(r.Right-tr,r.Top),true,false);Corner(new Point(r.Right,r.Top+tr),tr);
   g.LineTo(new Point(r.Right,r.Bottom-br),true,false);Corner(new Point(r.Right-br,r.Bottom),br);
   g.LineTo(new Point(r.Left+bl,r.Bottom),true,false);Corner(new Point(r.Left,r.Bottom-bl),bl);
   g.LineTo(new Point(r.Left,r.Top+tl),true,false);Corner(new Point(r.Left+tl,r.Top),tl);
  }shape.Freeze();return shape;
 }
}




