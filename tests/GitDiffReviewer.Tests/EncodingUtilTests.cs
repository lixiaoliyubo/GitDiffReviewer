using System.Text;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

public class EncodingUtilTests
{
    [Fact]
    public void Decode_utf8文本_原样返回()
    {
        var bytes = Encoding.UTF8.GetBytes("中文内容");
        Assert.Equal("中文内容", EncodingUtil.Decode(bytes));
    }

    [Fact]
    public void Decode_gbk文本_回退GB18030成功()
    {
        // "中文内容" 的 GBK 编码字节
        var gbk = Encoding.GetEncoding("GB18030");
        var bytes = gbk.GetBytes("中文内容");
        // 这些字节不是合法 UTF-8 序列，应触发 GB18030 回退
        Assert.Equal("中文内容", EncodingUtil.Decode(bytes));
    }

    [Fact]
    public void Decode_空字节串_返回空串()
    {
        Assert.Equal("", EncodingUtil.Decode(Array.Empty<byte>()));
    }
}
