using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;


namespace PlaywrightHumanInput;
public static class HumanSeed
{
    /// <summary>
    /// 创建一个新的随机主种子。
    /// 创建后应当保存并在整个 Worker 中复用。
    /// </summary>
    public static int CreateRandom()
    {
        Span<byte> bytes = stackalloc byte[4];

        RandomNumberGenerator.Fill(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    /// <summary>
    /// 从稳定字符串生成种子。
    ///
    /// 不要使用 string.GetHashCode()，
    /// 因为不同进程之间可能不一致。
    /// </summary>
    public static int FromString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(hash);
    }

    /// <summary>
    /// 从主种子派生子种子。
    /// 可用于不同页面、鼠标、输入和滚动子流。
    /// </summary>
    public static int Derive(
        int rootSeed,
        int streamId)
    {
        ulong value =
            ((ulong)(uint)rootSeed << 32) |
            (uint)streamId;

        value += 0x9E3779B97F4A7C15UL;

        value = (value ^ (value >> 30)) *
                0xBF58476D1CE4E5B9UL;

        value = (value ^ (value >> 27)) *
                0x94D049BB133111EBUL;

        value ^= value >> 31;

        return unchecked((int)value);
    }
}

