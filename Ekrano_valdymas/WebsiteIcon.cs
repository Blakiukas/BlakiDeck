using System.IO;
using System.Net.Http;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;
internal static class WebsiteIcon
{
    private static readonly HttpClient Client=new() { Timeout=TimeSpan.FromSeconds(6) };
    static WebsiteIcon() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
    private sealed class Entry
    {
        internal Task<ImageSource?> Task=System.Threading.Tasks.Task.FromResult<ImageSource?>(null);
        internal DateTime RetryAfter=DateTime.MaxValue;
        internal long LastUse;
    }
    private const int CacheLimit=128;
    private static readonly object CacheLock=new();
    private static readonly Dictionary<string,Entry> Cache=new(StringComparer.OrdinalIgnoreCase);
    private static long useSequence;
    internal static Task<ImageSource?> LoadAsync(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))return Task.FromResult<ImageSource?>(null);
        string icon=new Uri(uri.GetLeftPart(UriPartial.Authority)+"/favicon.ico").AbsoluteUri;
        lock(CacheLock)
        {
            if(Cache.TryGetValue(icon,out var existing)&&DateTime.UtcNow<existing.RetryAfter)
            {existing.LastUse=++useSequence;return existing.Task;}
            Cache.Remove(icon);
            if(Cache.Count>=CacheLimit)Cache.Remove(Cache.MinBy(pair=>pair.Value.LastUse).Key);
            var entry=new Entry{LastUse=++useSequence};
            Cache[icon]=entry;
            entry.Task=FetchAndRecordAsync(icon,entry);
            return entry.Task;
        }
    }
    private static async Task<ImageSource?> FetchAndRecordAsync(string icon,Entry entry)
    {
        var image=await Fetch(icon);
        if (image is not BitmapSource initial || Math.Max(initial.PixelWidth,initial.PixelHeight)<128)
        {
            try
            {
                var home=new Uri(new Uri(icon),"/");
                using var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var response=await Client.GetAsync(home,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                response.EnsureSuccessStatusCode();
                using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
                using var reader=new StreamReader(stream);
                var chars=new char[256*1024];int count=0,read;
                while(count<chars.Length&&(read=await reader.ReadAsync(chars.AsMemory(count),timeout.Token))>0)count+=read;
                var page=response.RequestMessage?.RequestUri??home;
                foreach(var candidate in IconCandidates(new string(chars,0,count),page).Take(3))
                {
                    var better=await Fetch(candidate.AbsoluteUri);
                    if(better is BitmapSource next && (image is not BitmapSource previous || next.PixelWidth*next.PixelHeight>previous.PixelWidth*previous.PixelHeight))image=next;
                    if(image is BitmapSource enough&&Math.Max(enough.PixelWidth,enough.PixelHeight)>=128)break;
                }
            } catch { /* Keep the origin favicon if discovery is unavailable. */ }
        }
        // Keep successful images warm. Failed requests may retry after a short
        // cooldown, without hammering an unavailable origin on every redraw.
        if(image==null)lock(CacheLock)entry.RetryAfter=DateTime.UtcNow.AddSeconds(30);
        return image;
    }
    internal static Uri[] IconCandidates(string html,Uri page)
    {
        var candidates=new List<(Uri Url,int Size)>();
        foreach(Match link in Regex.Matches(html,@"<link\b[^>]*>",RegexOptions.IgnoreCase))
        {
            var attrs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(Match attr in Regex.Matches(link.Value,"""([\w-]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))"""))
                attrs[attr.Groups[1].Value]=WebUtility.HtmlDecode(attr.Groups[2].Success?attr.Groups[2].Value:attr.Groups[3].Success?attr.Groups[3].Value:attr.Groups[4].Value);
            if(!attrs.TryGetValue("rel",out var rel)||!rel.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Any(v=>v.Equals("icon",StringComparison.OrdinalIgnoreCase)||v.Equals("apple-touch-icon",StringComparison.OrdinalIgnoreCase)||v.Equals("apple-touch-icon-precomposed",StringComparison.OrdinalIgnoreCase)))continue;
            if(!attrs.TryGetValue("href",out var href)||!Uri.TryCreate(page,href,out var uri)||uri.Scheme is not ("http" or "https"))continue;
            int size=0;if(attrs.TryGetValue("sizes",out var sizes))foreach(Match value in Regex.Matches(sizes,@"(\d+)x(\d+)",RegexOptions.IgnoreCase))if(int.TryParse(value.Groups[1].Value,out int width)&&int.TryParse(value.Groups[2].Value,out int height))size=Math.Max(size,Math.Min(width,height));
            candidates.Add((uri,size));
        }
        return candidates.OrderByDescending(c=>c.Size).Select(c=>c.Url).Distinct().ToArray();
    }
    private static async Task<ImageSource?> Fetch(string url)
    {
        try
        {
            using var response=await Client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>1024*1024)return null;
            await response.Content.LoadIntoBufferAsync(1024*1024);
            using var stream=new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            return Decode(stream);
        } catch {return null;}
    }
    internal static ImageSource? Decode(Stream stream)
    {
        try
        {
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var frame=decoder.Frames.OrderByDescending(f=>f.PixelWidth*f.PixelHeight).ThenByDescending(f=>f.Format.BitsPerPixel).First();
            BitmapSource bitmap=frame;
            if(Math.Max(frame.PixelWidth,frame.PixelHeight)>256)
            {
                double scale=256d/Math.Max(frame.PixelWidth,frame.PixelHeight);
                bitmap=new TransformedBitmap(frame,new ScaleTransform(scale,scale));
            }
            bitmap.Freeze();return bitmap;
        }catch{return null;}
    }
}
