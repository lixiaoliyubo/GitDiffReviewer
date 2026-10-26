using System.Text;

namespace GitDiffReviewer.Services;

/// <summary>
/// 字节解码与 -z 输出切分（§7.3 / §7.4）。
/// 顺序不能反：严格 UTF-8 失败才回退 GB18030。
/// </summary>
public static class EncodingUtil
{
    static EncodingUtil()
    {
        // GB18030 解码需要注册 CodePages provider
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>先按严格 UTF-8 解码，失败再回退 GB18030。</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }

    /// <summary>
    /// 按 0x00 字节切分（§7.3）。必须先切字节再逐段解码，
    /// 禁止整段先 UTF-8.GetString 再切，否则 GBK 路径会被解坏。
    /// </summary>
    public static List<byte[]> SplitNul(byte[] buf)
    {
        var list = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            if (buf[i] == 0)
            {
                list.Add(buf[start..i]);
                start = i + 1;
            }
        }
        if (start < buf.Length) list.Add(buf[start..]);
        return list;
    }
}
