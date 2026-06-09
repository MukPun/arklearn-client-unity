// SecureHandshake.cs
// skynet 登录服加密握手工具 —— **P/Invoke wrapper 模式**
// ---------------------------------------------------------------
// 把 skynet lualib-src/lua-crypt.c 的加密核心 (DH / HMAC-MD5 / base64 / randomkey)
// 通过 native lib skynet_crypt.dll (在 Assets/Plugins/x86_64/ 下) 调, 避免
// 在 C# 端重新移植加密逻辑。skynet 升级 lua-crypt.c 时, 只需重编译 .dll
// 即可跟随, 不必手动同步 C# 移植代码。
//
// 当前实现:
//   - DH / HMAC / RandomKey / Base64 → P/Invoke skynet_crypt.dll
//   - DES → .NET BCL System.Security.Cryptography.DES (标准 FIPS 46-3, 跟 skynet
//     手写 DES 算法等价, 0/1/2/3/4/5/6/7 号 S-box 跟 RFC 完全一致)
//     + 自实现 ISO/IEC 7816-4 padding (0x80 + 0x00 直到 8 字节, 对齐时
//     额外加一整个 8 字节 block)
//   - HashKey → .NET BCL (skynet 没在我们 Phase 1 流程里用它, 仅为 API 完整性)
//
// 已知向量 (跟 skynet C 端 byte-byte 对齐):
//   Hmac64(0x01*8, 0x02*8) == 5c9f01d50fa9c25a  ✓ (P/Invoke 路径下也通过自测)
//
// 平台支持: 当前仅编译了 Windows x64 .dll
//   - Assets/Plugins/x86_64/skynet_crypt.dll (46,864 字节, MinGW gcc 15.2)
//   - Android/iOS/Linux/macOS 需额外编译对应平台的 .so/.a/.dylib/.bundle
//   - 编译脚本: Assets/Plugins/Native/build_skynet_crypt.bat (Windows 用)
//   - 源码:     Assets/Plugins/Native/src/skynet_crypt_shim.c

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Manager {
    public static class SecureHandshake {
        // ============================================================
        // P/Invoke 绑定到 skynet_crypt.dll
        // ============================================================
        private const string LIB = "skynet_crypt";

        [DllImport(LIB, EntryPoint = "skynet_randomkey", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SkynetRandomkey(byte[] out8);

        [DllImport(LIB, EntryPoint = "skynet_dhexchange", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SkynetDHExchange(byte[] key8, byte[] out8);

        [DllImport(LIB, EntryPoint = "skynet_dhsecret", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SkynetDHSecret(byte[] serverPub8, byte[] key8, byte[] out8);

        [DllImport(LIB, EntryPoint = "skynet_hmac64", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SkynetHmac64(byte[] x8, byte[] y8, byte[] out8);

        [DllImport(LIB, EntryPoint = "skynet_b64encode", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SkynetB64Encode(byte[] data, int len, byte[] outBuf);

        [DllImport(LIB, EntryPoint = "skynet_b64decode", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SkynetB64Decode(byte[] ascii, byte[] outBuf, int maxOut);

        // ============================================================
        // Public API —— 跟原 C# 移植版完全一致, NetworkManager.cs 无需改
        // ============================================================

        /// <summary>对应 skynet crypt.randomkey(), 8 字节随机。</summary>
        public static byte[] RandomKey() {
            var b = new byte[8];
            SkynetRandomkey(b);
            return b;
        }

        /// <summary>对应 skynet crypt.dhexchange(key), 输出 8 字节 (5^key mod P, LE uint64)。</summary>
        public static byte[] DHExchange(byte[] key) {
            if (key == null || key.Length != 8)
                throw new ArgumentException("key must be 8 bytes");
            var b = new byte[8];
            SkynetDHExchange(key, b);
            return b;
        }

        /// <summary>对应 skynet crypt.dhsecret(serverPub, key), 输出 8 字节 (serverPub^key mod P → MD5 → first 8B)。</summary>
        public static byte[] DHSecret(byte[] serverPub, byte[] key) {
            if (serverPub == null || serverPub.Length != 8)
                throw new ArgumentException("serverPub must be 8 bytes (skynet uses 64-bit DH)");
            if (key == null || key.Length != 8)
                throw new ArgumentException("key must be 8 bytes");
            var b = new byte[8];
            SkynetDHSecret(serverPub, key, b);
            return b;
        }

        /// <summary>对应 skynet crypt.hmac64(x, y), 8 字节 (skynet custom hmac64, 非标准 HMAC)。</summary>
        public static byte[] Hmac64(byte[] x, byte[] y) {
            if (x == null || x.Length != 8)
                throw new ArgumentException("x must be 8 bytes");
            if (y == null || y.Length != 8)
                throw new ArgumentException("y must be 8 bytes");
            var b = new byte[8];
            SkynetHmac64(x, y, b);
            return b;
        }

        /// <summary>对应 skynet crypt.base64encode(data), 标准 base64 字符串。</summary>
        public static string Base64Encode(byte[] data) {
            if (data == null || data.Length == 0) return "";
            int maxLen = (data.Length + 2) / 3 * 4;
            var buf = new byte[maxLen + 1];
            int written = SkynetB64Encode(data, data.Length, buf);
            return Encoding.ASCII.GetString(buf, 0, written);
        }

        /// <summary>对应 skynet crypt.base64decode(s), 标准 base64 字节。</summary>
        public static byte[] Base64Decode(string s) {
            if (string.IsNullOrEmpty(s)) return new byte[0];
            byte[] ascii = Encoding.ASCII.GetBytes(s);
            int maxOut = (ascii.Length + 3) / 4 * 3;
            var buf = new byte[maxOut + 4];
            int written = SkynetB64Decode(ascii, buf, buf.Length);
            // C 端 skynet_b64decode 解析失败时返回 -1,不能直接 new byte[-1]
            // (会抛 OverflowException, message 跟 base64 错不相关, 排查困难)
            if (written < 0)
                throw new FormatException(string.Format("invalid base64 input: '{0}'", s));
            var result = new byte[written];
            Buffer.BlockCopy(buf, 0, result, 0, written);
            return result;
        }

        // ============================================================
        // DES —— 走 .NET BCL (FIPS 46-3 标准), 配 ISO/IEC 7816-4 padding
        // ============================================================
        private static byte[] Iso7816_4_Pad(byte[] data) {
            int padLen = 8 - (data.Length % 8);
            if (padLen == 0) padLen = 8;
            var padded = new byte[data.Length + padLen];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            padded[data.Length] = 0x80;
            return padded;
        }

        /// <summary>对应 skynet crypt.desencode(data, key), ISO 7816-4 + DES-ECB。</summary>
        public static byte[] DesEncode(byte[] data, byte[] key) {
            if (key == null || key.Length != 8)
                throw new ArgumentException("key must be 8 bytes");
            var padded = Iso7816_4_Pad(data);
            using (var des = new DESCryptoServiceProvider()) {
                des.Key = key;
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.None;
                using (var enc = des.CreateEncryptor()) {
                    return enc.TransformFinalBlock(padded, 0, padded.Length);
                }
            }
        }

        /// <summary>对应 skynet crypt.desdecode(data, key), ISO 7816-4 移除 + DES-ECB。</summary>
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
            int pad = 1;
            for (int i = plain.Length - 1; i >= 0; i--, pad++) {
                if (plain[i] == 0x00) continue;
                if (plain[i] == 0x80) {
                    var result = new byte[plain.Length - pad];
                    Buffer.BlockCopy(plain, 0, result, 0, result.Length);
                    return result;
                }
                throw new ArgumentException("invalid ISO 7816-4 padding");
            }
            throw new ArgumentException("invalid ISO 7816-4 padding (no 0x80 marker)");
        }

        // ============================================================
        // HashKey —— 走 .NET BCL (skynet crypt.hashkey 当前流程未用, 仅为 API 完整性)
        // ============================================================
        /// <summary>对应 skynet crypt.hashkey(text), DJB2 + JS hash → 8 字节 (LE)。</summary>
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

