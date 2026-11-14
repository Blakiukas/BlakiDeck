using System.Buffers.Binary;
using System.IO;
using System.Text;
namespace Ekrano_valdymas;

// Read measured stream properties and embedded pictures without changing audio files.
internal static class AudioFileDetails
{
    internal sealed record Details(string Format,double? Bitrate,double? SampleRate,byte[]? Artwork);
    internal static Details Read(string path)
    {
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        byte[] magic=reader.ReadBytes(4);byte[]? artwork=null;double? rate=null,bitrate=null;
        if(Encoding.ASCII.GetString(magic)=="fLaC")
        {
            bool last=false;long audioStart=0;double seconds=0;
            while(!last&&stream.Position+4<=stream.Length){byte type=reader.ReadByte();last=(type&128)!=0;int length=(reader.ReadByte()<<16)|(reader.ReadByte()<<8)|reader.ReadByte();if(length>32*1024*1024||stream.Position+length>stream.Length)break;var data=reader.ReadBytes(length);
                if((type&127)==0&&data.Length==34){ulong packed=BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(10,8));rate=packed>>44;ulong samples=packed&0xfffffffff;seconds=rate>0?samples/rate.Value:0;}
                if((type&127)==6){try{int p=4;int U32(){int n=checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p,4)));p+=4;return n;}int mime=U32();p+=mime;int description=U32();p+=description;p+=16;int size=U32();if(size>0&&p+size<=data.Length)artwork=data.AsSpan(p,size).ToArray();}catch(ArgumentOutOfRangeException){}catch(OverflowException){}}
                audioStart=stream.Position;
            }
            if(seconds>0)bitrate=(stream.Length-audioStart)*8/seconds;
            return new("FLAC",bitrate,rate,artwork);
        }
        if(Encoding.ASCII.GetString(magic)=="RIFF"){
            stream.Position=8;if(Encoding.ASCII.GetString(reader.ReadBytes(4))=="WAVE")while(stream.Position+8<=stream.Length){string id=Encoding.ASCII.GetString(reader.ReadBytes(4));uint length=reader.ReadUInt32();long next=stream.Position+length+(length%2);if(next>stream.Length)break;if(id=="fmt "&&length>=16){reader.ReadUInt16();reader.ReadUInt16();rate=reader.ReadUInt32();bitrate=reader.ReadUInt32()*8d;return new("WAV",bitrate,rate,null);}stream.Position=next;}
        }
        if(magic.Length==4&&magic[0]=='I'&&magic[1]=='D'&&magic[2]=='3'){
            int version=magic[3];stream.Position=5;byte flags=reader.ReadByte();var sizes=reader.ReadBytes(4);int size=Sync(sizes);if(version is 3 or 4&&flags==0&&size<=32*1024*1024){var tag=reader.ReadBytes(size);int p=0;while(p+10<=tag.Length){string id=Encoding.ASCII.GetString(tag,p,4);int length=version==4?Sync(tag.AsSpan(p+4,4)):BinaryPrimitives.ReadInt32BigEndian(tag.AsSpan(p+4,4));p+=10;if(length<=0||p+length>tag.Length)break;if(id=="APIC"){var data=tag.AsSpan(p,length);int q=1;while(q<data.Length&&data[q]!=0)q++;q+=2;int step=data[0] is 1 or 2?2:1;while(q+step<=data.Length){if(data[q]==0&&(step==1||data[q+1]==0)){q+=step;break;}q+=step;}if(q<data.Length)artwork=data[q..].ToArray();break;}p+=length;}}}
        return new(Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),null,null,artwork);
    }
    private static int Sync(ReadOnlySpan<byte> b)=>(b[0]&127)<<21|(b[1]&127)<<14|(b[2]&127)<<7|b[3]&127;
}
