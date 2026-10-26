using System.Text;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

public class SplitNulTests
{
    [Fact]
    public void SplitNul_按零字节切分()
    {
        // "a\0b\0c"
        var buf = new byte[] { (byte)'a', 0, (byte)'b', 0, (byte)'c' };
        var parts = EncodingUtil.SplitNul(buf);
        Assert.Equal(3, parts.Count);
        Assert.Equal("a", Encoding.UTF8.GetString(parts[0]));
        Assert.Equal("b", Encoding.UTF8.GetString(parts[1]));
        Assert.Equal("c", Encoding.UTF8.GetString(parts[2]));
    }

    [Fact]
    public void SplitNul_尾部无Nul_最后一段保留()
    {
        var buf = new byte[] { (byte)'x', 0, (byte)'y' };
        var parts = EncodingUtil.SplitNul(buf);
        Assert.Equal(2, parts.Count);
        Assert.Equal("y", Encoding.UTF8.GetString(parts[1]));
    }

    [Fact]
    public void SplitNul_连续Nul_产生空段()
    {
        var buf = new byte[] { (byte)'a', 0, 0, (byte)'b' };
        var parts = EncodingUtil.SplitNul(buf);
        Assert.Equal(3, parts.Count);
        Assert.Empty(parts[1]);
    }

    [Fact]
    public void SplitNul_gbk路径_字节级切分后再解码()
    {
        // 测试侧也需要注册 CodePages 才能拿 GB18030
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        // 模拟 -z 输出：路径是 GBK 字节，段间以 0x00 分隔
        var gbk = Encoding.GetEncoding("GB18030");
        var p1 = gbk.GetBytes("修改.txt");
        var p2 = gbk.GetBytes("中文新增.txt");
        var buf = new List<byte>();
        buf.AddRange(p1); buf.Add(0);
        buf.AddRange(p2); buf.Add(0);

        var parts = EncodingUtil.SplitNul(buf.ToArray());
        Assert.Equal(2, parts.Count);
        // 逐段解码，GBK 不乱码
        Assert.Equal("修改.txt", EncodingUtil.Decode(parts[0]));
        Assert.Equal("中文新增.txt", EncodingUtil.Decode(parts[1]));
    }
}
