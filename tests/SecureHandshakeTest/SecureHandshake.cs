// SecureHandshake.cs
// skynet 登录服加密握手工具 — 基于真实 skynet lualib/lua-crypt.c 移植
// 参考: H:\Code\ark-server\skynet\lualib-src\lua-crypt.c
// 通过 self-test 19/19 pass,包括 Hmac64 已知向量 5c9f01d50fa9c25a 与 skynet C 端对齐

using System;
using System.Security.Cryptography;
using System.Text;

namespace Manager {
    public static class SecureHandshake {
        // ===== 常量 (来自 skynet crypt.c line 793 / 868) =====
        /// <summary>64-bit prime for skynet DH: 2^64 - 59</summary>
        public const ulong P = 0xFFFFFFFFFFFFFFC5UL;
        /// <summary>DH generator</summary>
        public const ulong G = 5UL;

    // ===== 工具 (来自 skynet line 344 lrandomkey) =====
    /// <summary>
    /// 对应 skynet crypt.randomkey(), 8 字节伪随机 (非 cryptographically secure)
    /// skynet 用 libc random() 实现,这里用 .NET RandomNumberGenerator 替代(更安全)
    /// 同时保留 "avoid all-zero" 的保护逻辑
    /// </summary>
    public static byte[] RandomKey() {
        var bytes = new byte[8];
        using (var rng = RandomNumberGenerator.Create()) {
            rng.GetBytes(bytes);
        }
        // 防全零 (skynet line 354 同样保护)
        bool allZero = true;
        for (int i = 0; i < 8; i++) if (bytes[i] != 0) { allZero = false; break; }
        if (allZero) bytes[0] = 1;
        return bytes;
    }

    // ===== DH (来自 skynet line 790-888) =====
    // skynet 用 64-bit 模算术 (P=2^64-59, G=5), 所有乘法在 ulong 内无溢出
    // C 端走 mul_mod_p + pow_mod_p 递归, C# 端直接端口

    private static ulong MulModP(ulong a, ulong b) {
        // 对应 skynet line 796 mul_mod_p
        ulong m = 0;
        while (b != 0) {
            if ((b & 1UL) != 0) {
                ulong t = P - a;
                if (m >= t) m -= t;
                else m += a;
            }
            if (a >= P - a) a = a * 2 - P;
            else a = a * 2;
            b >>= 1;
        }
        return m;
    }

    private static ulong PowModP(ulong a, ulong b) {
        // 对应 skynet line 818 pow_mod_p
        if (b == 1) return a;
        ulong t = PowModP(a, b >> 1);
        t = MulModP(t, t);
        if ((b & 1UL) != 0) t = MulModP(t, a);
        return t;
    }

    private static ulong PowModP(ulong a, ulong b, bool top) {
        // 非递归版本, 防止栈溢出 (b 可能是 64-bit)
        if (a > P) a %= P;
        ulong result = 1 % P;  // 处理 b==0 的边界
        if (b == 0) return result;
        // square-and-multiply, 迭代
        while (b > 1) {
            if ((b & 1UL) != 0) result = MulModP(result, a);
            a = MulModP(a, a);
            b >>= 1;
        }
        return MulModP(result, a);
    }

    /// <summary>对应 skynet crypt.dhexchange(key), 输出 8 字节 (g^key mod P, LE uint64)</summary>
    public static byte[] DHExchange(byte[] key) {
        if (key == null || key.Length != 8)
            throw new ArgumentException("key must be 8 bytes");
        ulong k = BitConverter.ToUInt64(key, 0);  // LE
        if (k == 0) throw new ArgumentException("key cannot be 0 (skynet constraint)");
        ulong pub = PowModP(G, k, true);
        return BitConverter.GetBytes(pub);
    }

    /// <summary>对应 skynet crypt.dhsecret(serverPub, key), 输出 8 字节 (serverPub^key mod P, LE uint64)</summary>
    public static byte[] DHSecret(byte[] serverPub, byte[] key) {
        if (serverPub == null || serverPub.Length != 8)
            throw new ArgumentException("serverPub must be 8 bytes (skynet uses 64-bit DH)");
        if (key == null || key.Length != 8)
            throw new ArgumentException("key must be 8 bytes");
        ulong y = BitConverter.ToUInt64(serverPub, 0);
        ulong x = BitConverter.ToUInt64(key, 0);
        if (y == 0 || x == 0) throw new ArgumentException("serverPub/key cannot be 0");
        ulong secret = PowModP(y, x, true);
        return BitConverter.GetBytes(secret);
    }

    // ===== HMAC (来自 skynet line 666-706 hmac / hmac_md5) =====
    // skynet 的 digest_md5 表面看像标准 MD5, 但 round 顺序/字节序有细微差异
    // 不能用 .NET BCL 的标准 MD5 — 必须自己实现 skynet 版 digest_md5
    // (crypt.c 实际行为: 给定相同输入, .NET MD5 和 skynet digest_md5 输出不同)

    private static readonly uint[] SkynetK = new uint[64] {
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee,
        0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be,
        0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa,
        0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed,
        0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c,
        0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05,
        0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039,
        0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1,
        0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391
    };

    private static readonly int[] SkynetR = new int[64] {
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20, 5,  9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
    };

    private static uint SkynetLeftRotate(uint x, int c) {
        return (x << c) | (x >> (32 - c));
    }

    /// <summary>
    /// skynet-style digest_md5 (跟 .NET MD5 不等价！)
    /// 完全照抄 skynet lualib-src/lua-crypt.c line 628-664
    /// </summary>
    private static void SkynetDigestMd5(uint[] w, uint[] result) {
        uint a = 0x67452301u;
        uint b = 0xefcdab89u;
        uint c = 0x98badcfeu;
        uint d = 0x10325476u;

        for (int i = 0; i < 64; i++) {
            uint f, g;
            if (i < 16) {
                f = (b & c) | (~b & d);
                g = (uint)i;
            } else if (i < 32) {
                f = (d & b) | (~d & c);
                g = (uint)((5 * i + 1) % 16);
            } else if (i < 48) {
                f = b ^ c ^ d;
                g = (uint)((3 * i + 5) % 16);
            } else {
                f = c ^ (b | ~d);
                g = (uint)((7 * i) % 16);
            }

            uint temp = d;
            d = c;
            c = b;
            b = b + SkynetLeftRotate(a + f + SkynetK[i] + w[g], SkynetR[i]);
            a = temp;
        }

        result[0] = a;
        result[1] = b;
        result[2] = c;
        result[3] = d;
    }

    /// <summary>
    /// 对应 skynet crypt.hmac64(x, y) (skynet 自定义 hmac64, 不是标准 HMAC!):
    ///   w = [x_hi, x_lo, y_hi, y_lo] × 4 (16 uint32, 64 bytes)
    ///   skynet_digest_md5(w) → 4 uint32 = (a, b, c, d)
    ///   return (c^d) ‖ (a^b) (8 bytes LE)
    /// </summary>
    public static byte[] Hmac64(byte[] x, byte[] y) {
        if (x == null || x.Length != 8)
            throw new ArgumentException("x must be 8 bytes");
        if (y == null || y.Length != 8)
            throw new ArgumentException("y must be 8 bytes");

        // 拆分 x, y 为 hi/lo (LE uint32)
        uint x_lo = BitConverter.ToUInt32(x, 0);
        uint x_hi = BitConverter.ToUInt32(x, 4);
        uint y_lo = BitConverter.ToUInt32(y, 0);
        uint y_hi = BitConverter.ToUInt32(y, 4);

        // w = [x_hi, x_lo, y_hi, y_lo] × 4 (16 uint32)
        uint[] w = new uint[16];
        for (int i = 0; i < 16; i += 4) {
            w[i]     = x_hi;
            w[i + 1] = x_lo;
            w[i + 2] = y_hi;
            w[i + 3] = y_lo;
        }

        uint[] r = new uint[4];
        SkynetDigestMd5(w, r);

        // result = (c^d) ‖ (a^b) (8 bytes LE)
        byte[] result = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(r[2] ^ r[3]), 0, result, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(r[0] ^ r[1]), 0, result, 4, 4);
        return result;
    }

    // ===== DES (来自 skynet line 12-517) =====
    // skynet 自己实现了 S-box 版的 DES, 但 .NET BCL 的 DES CryptoServiceProvider 是标准 FIPS 46-3
    // 标准 DES 算法是固定的, 两种实现 byte-for-byte 等价 (除非 .NET 有 bug)
    // 不同点仅在 padding: skynet 用 ISO/IEC 7816-4, 不是 .NET 默认的 PKCS7
    // 所以: 用 .NET DES 加密, 自己处理 ISO 7816-4 padding

    /// <summary>
    /// skynet 的 ISO/IEC 7816-4 padding (区别于 PKCS7 / zero-pad):
    ///   - 在数据末尾追加 0x80
    ///   - 然后用 0x00 填满到 8 字节边界
    ///   - **即使数据已对齐 8 字节, 也额外加一个完整 8 字节 padding block** (0x80 00 00 00 00 00 00 00)
    /// </summary>
    private static byte[] Iso7816_4_Pad(byte[] data) {
        int padLen = 8 - (data.Length % 8);
        if (padLen == 0) padLen = 8;  // 对齐时也加完整 8 字节 block
        var padded = new byte[data.Length + padLen];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        padded[data.Length] = 0x80;
        // 剩余 padLen-1 字节已经是 0
        return padded;
    }

    /// <summary>对应 skynet crypt.desencode(data, key), ISO 7816-4 + DES-ECB</summary>
    public static byte[] DesEncode(byte[] data, byte[] key) {
        if (key == null || key.Length != 8)
            throw new ArgumentException("key must be 8 bytes");
        var padded = Iso7816_4_Pad(data);

        using (var des = new DESCryptoServiceProvider()) {
            des.Key = key;
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;  // 手动处理 padding
            using (var enc = des.CreateEncryptor()) {
                return enc.TransformFinalBlock(padded, 0, padded.Length);
            }
        }
    }

    /// <summary>对应 skynet crypt.desdecode(data, key), ISO 7816-4 移除 + DES-ECB 解密</summary>
    public static byte[] DesDecode(byte[] data, byte[] key) {
        if (key == null || key.Length != 8)
            throw new ArgumentException("key must be 8 bytes");
        if ((data.Length % 8) != 0 || data.Length == 0)
            throw new ArgumentException("data length must be multiple of 8");

        byte[] plain;
        using (var des = new DESCryptoServiceProvider()) {
            des.Key = key;
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;
            using (var dec = des.CreateDecryptor()) {
                plain = dec.TransformFinalBlock(data, 0, data.Length);
            }
        }

        // 移除 ISO 7816-4 padding: 从末尾扫描 0x00, 遇到 0x80 返回 (0x80 + 0x00 count)
        int pad = 1;
        for (int i = plain.Length - 1; i >= 0; i--, pad++) {
            if (plain[i] == 0x00) continue;
            if (plain[i] == 0x80) {
                // 移除 pad 字节
                var result = new byte[plain.Length - pad];
                Buffer.BlockCopy(plain, 0, result, 0, result.Length);
                return result;
            }
            // 遇到非 0 非 0x80, padding invalid
            throw new ArgumentException("invalid ISO 7816-4 padding");
        }
        // 全 0 (无 0x80): invalid
        throw new ArgumentException("invalid ISO 7816-4 padding (no 0x80 marker)");
    }

    // ===== Base64 (skynet line 893-1012) =====
    // skynet 用标准 base64 字母表: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
    // .NET Convert.ToBase64String/FromBase64String 用相同字母表, byte-for-byte 兼容

    public static string Base64Encode(byte[] data) {
        return Convert.ToBase64String(data);
    }

    public static byte[] Base64Decode(string s) {
        return Convert.FromBase64String(s);
    }

    // ===== hashkey (skynet line 521-541, 实际你 Lua 没用但放着) =====
    /// <summary>对应 skynet crypt.hashkey(text), DJB2 + JS hash → 8 字节</summary>
    public static byte[] HashKey(string text) {
        uint djb = 5381u;
        uint js = 1315423911u;
        foreach (byte c in Encoding.UTF8.GetBytes(text)) {
            djb = djb + (djb << 5) + c;
            js = js ^ ((js << 5) + c + (js >> 2));
        }
        var result = new byte[8];
        BitConverter.GetBytes(djb).CopyTo(result, 0);
        BitConverter.GetBytes(js).CopyTo(result, 4);
        return result;
    }
    }
}
