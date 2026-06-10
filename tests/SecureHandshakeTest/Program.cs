// Program.cs - self-test for SecureHandshake (skynet crypt.c port)
// 用 .NET Framework 4 csc 编译运行
// 失败返回 1, 全部通过返回 0
// 已知向量断言 Hmac64(0x01*8, 0x02*8) == 5c9f01d50fa9c25a (来自 skynet C 端实测)

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Manager;

public class Program {
    private static int _passed;
    private static int _failed;

    public static int Main() {
        Console.WriteLine("=== SecureHandshake self-test (skynet crypt.c port) ===\n");

        // ===== RandomKey =====
        Test("RandomKey output length = 8", () => {
            AssertEqual(8, SecureHandshake.RandomKey().Length);
        });

        Test("RandomKey varies between calls", () => {
            AssertTrue(!SecureHandshake.RandomKey().SequenceEqual(SecureHandshake.RandomKey()));
        });

        // ===== DH (skynet 64-bit) =====
        Test("DHExchange output length = 8", () => {
            AssertEqual(8, SecureHandshake.DHExchange(SecureHandshake.RandomKey()).Length);
        });

        Test("DHSecret output length = 8", () => {
            var a = SecureHandshake.DHExchange(SecureHandshake.RandomKey());
            AssertEqual(8, SecureHandshake.DHSecret(a, SecureHandshake.RandomKey()).Length);
        });

        Test("DH symmetric: Alice(BobPub, Akey) == Bob(AlicePub, Bkey)", () => {
            var aKey = SecureHandshake.RandomKey();
            var bKey = SecureHandshake.RandomKey();
            var aPub = SecureHandshake.DHExchange(aKey);
            var bPub = SecureHandshake.DHExchange(bKey);
            var aSec = SecureHandshake.DHSecret(bPub, aKey);
            var bSec = SecureHandshake.DHSecret(aPub, bKey);
            AssertEqualBytes(aSec, bSec, "shared secrets must match");
        });

        Test("DH symmetric 10 iterations", () => {
            for (int i = 0; i < 10; i++) {
                var aKey = SecureHandshake.RandomKey();
                var bKey = SecureHandshake.RandomKey();
                var aPub = SecureHandshake.DHExchange(aKey);
                var bPub = SecureHandshake.DHExchange(bKey);
                var aSec = SecureHandshake.DHSecret(bPub, aKey);
                var bSec = SecureHandshake.DHSecret(aPub, bKey);
                AssertEqualBytes(aSec, bSec, "iteration " + i);
            }
        });

        // 注: skynet 端 dhsecret 在 server_pub=0 或 key=0 时 silently fix 0→1, 不 throw。
        // 我们的 P/Invoke shim 跟 skynet 对齐, 也 silently fix, 不 throw (见 skynet_crypt_shim.c line 109/116-118)。
        // 所以此处不验 throw, 改为验 all-zero 时不 crash 且输出 8 字节 (行为与 skynet 一致)。
        Test("DHExchange silently handles all-zero input (skynet-compatible)", () => {
            var pub = SecureHandshake.DHExchange(new byte[8]);
            AssertEqual(8, pub.Length);
        });

        // ===== Hmac64 (skynet 自定义) =====
        Test("Hmac64 output length = 8", () => {
            AssertEqual(8, SecureHandshake.Hmac64(SecureHandshake.RandomKey(),
                                                  SecureHandshake.RandomKey()).Length);
        });

        Test("Hmac64 deterministic", () => {
            var k = SecureHandshake.RandomKey();
            AssertEqualBytes(SecureHandshake.Hmac64(k, k), SecureHandshake.Hmac64(k, k));
        });

        Test("Hmac64 changes with input", () => {
            var k1 = SecureHandshake.RandomKey();
            var k2 = SecureHandshake.RandomKey();
            AssertTrue(!SecureHandshake.Hmac64(k1, k1).SequenceEqual(SecureHandshake.Hmac64(k2, k2)));
        });

        // 关键已知向量 - 来自 skynet C 端实测 (用户跑 crypt.hexencode(crypt.hmac64(...)) 验证)
        Test("Hmac64 known vector: (0x01*8, 0x02*8) == 5c9f01d50fa9c25a", () => {
            var x = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
            var y = new byte[] { 2, 2, 2, 2, 2, 2, 2, 2 };
            AssertEqual("5c9f01d50fa9c25a", BytesToHex(SecureHandshake.Hmac64(x, y)));
        });

        // ===== DES (ISO 7816-4 + .NET DES-ECB) =====
        Test("DesEncode 7 bytes input -> 8 bytes output", () => {
            var key = SecureHandshake.RandomKey();
            var pt = new byte[7];
            AssertEqual(8, SecureHandshake.DesEncode(pt, key).Length);
        });

        Test("DesEncode 8 bytes input -> 16 bytes output (aligned adds full block)", () => {
            AssertEqual(16, SecureHandshake.DesEncode(new byte[8],
                SecureHandshake.RandomKey()).Length);
        });

        Test("DesEncode 16 bytes input -> 24 bytes output", () => {
            AssertEqual(24, SecureHandshake.DesEncode(new byte[16],
                SecureHandshake.RandomKey()).Length);
        });

        Test("DesEncode/Decode involution (sizes 0..24)", () => {
            var key = SecureHandshake.RandomKey();
            for (int n = 0; n <= 24; n++) {
                var pt = new byte[n];
                for (int i = 0; i < n; i++) pt[i] = (byte)((i * 7 + 3) & 0xff);
                var back = SecureHandshake.DesDecode(SecureHandshake.DesEncode(pt, key), key);
                AssertEqualBytes(pt, back, "size " + n);
            }
        });

        Test("DesEncode is actually encrypting", () => {
            var key = SecureHandshake.RandomKey();
            var pt = Encoding.UTF8.GetBytes("user@server:pass");
            AssertTrue(!SecureHandshake.DesEncode(pt, key).SequenceEqual(pt));
        });

        // ===== Base64 =====
        Test("Base64 roundtrip", () => {
            var data = new byte[100];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            AssertEqualBytes(data, SecureHandshake.Base64Decode(SecureHandshake.Base64Encode(data)));
        });

        Test("Base64 empty", () => {
            AssertEqual("", SecureHandshake.Base64Encode(new byte[0]));
            AssertEqual(0, SecureHandshake.Base64Decode("").Length);
        });

        // Regression: 修复前 C 端 skynet_b64decode 返回 -1, C# wrapper 拿 -1
        // 去 new byte[-1] 抛 OverflowException (message 跟 base64 错不相关,
        // 排查困难, Play 时会看到 "decode challenge failed: " message 为空)
        // 修后应抛 FormatException 带原 input
        Test("Base64 invalid input throws FormatException (regression)", () => {
            bool threw = false;
            try { SecureHandshake.Base64Decode("abcde"); }   // 5 chars, 不是 4 倍数, 触发 padding 错
            catch (FormatException) { threw = true; }
            AssertTrue(threw, "should throw FormatException on invalid base64");
        });

        // Phase 1 真实 Play 时遇到的两个 server pub 例子
        // 'qW0X60vFdsk=' 第一次成功, 'sFZs8WsAtO4=' 第二次失败
        // 如果这俩都能解,问题在传输层(行截断/CRLF);如果 sFZs8WsAtO4= 失败, shim 有 bug
        Test("Base64 decode skynet challenge (qW0X60vFdsk=) -> 8B", () => {
            var b = SecureHandshake.Base64Decode("qW0X60vFdsk=");
            AssertEqual(8, b.Length);
        });
        Test("Base64 decode skynet server pub (sFZs8WsAtO4=) -> 8B", () => {
            var b = SecureHandshake.Base64Decode("sFZs8WsAtO4=");
            AssertEqual(8, b.Length);
        });
        // Phase 1 第三次 Play 收到 'WMhjDV9/LQ4=' 同样 12 字符 1 '=', 但 Unity Play fail
        // 同样 22/22 PASS + 12 字符格式一致 → Unity Console 印 {0} 隐藏了控制字符
        // 兜底 trim 应该让 13 字符 'sFZs8WsAtO4=\n' 也能解
        Test("Base64 decode trailing \\n trimmed (sFZs8WsAtO4=\\n) -> 8B", () => {
            var b = SecureHandshake.Base64Decode("sFZs8WsAtO4=\n");
            AssertEqual(8, b.Length);
        });
        Test("Base64 decode trailing \\r\\n trimmed (sFZs8WsAtO4=\\r\\n) -> 8B", () => {
            var b = SecureHandshake.Base64Decode("sFZs8WsAtO4=\r\n");
            AssertEqual(8, b.Length);
        });
        Test("Base64 decode trailing \\0 trimmed (sFZs8WsAtO4=\\0) -> 8B", () => {
            var b = SecureHandshake.Base64Decode("sFZs8WsAtO4=\0");
            AssertEqual(8, b.Length);
        });

        // 真实 Play 失败对账: secret 不一致
        // client 端算:   secret = DHSecret(serverPub, clientKey) = efa8bd2ac5884300 ^ 8e6aca03be09a587
        // server 端算:   secret = crypt.dhsecret(clientkey, serverkey) = 41d4ce0499324d33 ^ 9e81da542b878370
        // 数学保证:      两个 secret 应该 byte-byte 相等
        // shim 实算:     client secret = a5b5ae2dd26e36ce (跟 server 9457f49289d83866 不等!)
        //
        // 如果下面这个测试 FAIL, shim 跟 skynet DH 算法在 pow_mod_p / mul_mod_p 上有 byte-level 偏差
        // 如果 PASS, 那 client 算的是真的(说明 client 端算法对, 是其他环节被改)
        Test("DHSecret regression: real Play values must match skynet 9457f49289d83866", () => {
            byte[] clientKey = { 0x8e, 0x6a, 0xca, 0x03, 0xbe, 0x09, 0xa5, 0x87 };
            byte[] serverPub = { 0xef, 0xa8, 0xbd, 0x2a, 0xc5, 0x88, 0x43, 0x00 };
            byte[] clientSecret = SecureHandshake.DHSecret(serverPub, clientKey);
            byte[] expected = { 0x94, 0x57, 0xf4, 0x92, 0x89, 0xd8, 0x38, 0x66 };
            AssertEqualBytes(clientSecret, expected, "shim must byte-match skynet serverSecret");
        });

        // 进一步定位: shim DHExchange(clientKey) 算的 clientPub 应该等于 server 收的
        // 如果这个 FAIL, shim 的 pow_mod_p 跟 skynet C 端 byte-level 偏差
        // 如果 PASS, 那 DHExchange 算法对, 但 DHSecret 内部状态被改 (?)
        Test("DHExchange regression: real Play clientKey must give clientPub 41d4ce0499324d33", () => {
            byte[] clientKey = { 0x8e, 0x6a, 0xca, 0x03, 0xbe, 0x09, 0xa5, 0x87 };
            byte[] clientPub = SecureHandshake.DHExchange(clientKey);
            byte[] expected = { 0x41, 0xd4, 0xce, 0x04, 0x99, 0x32, 0x4d, 0x33 };
            AssertEqualBytes(clientPub, expected, "shim DHExchange must match skynet dhexchange");
        });

        // 配套: 验 server 视角的 clientPub^serverKey 也得跟 serverSecret 一致
        // 如果上面 fail 但这个 pass, 说明 shim 内部对称但 pow_mod_p 跟 skynet byte-level 偏差
        Test("DHSecret regression: clientPub^serverKey must match skynet 9457f49289d83866", () => {
            byte[] clientPub = { 0x41, 0xd4, 0xce, 0x04, 0x99, 0x32, 0x4d, 0x33 };
            byte[] serverKey = { 0x9e, 0x81, 0xda, 0x54, 0x2b, 0x87, 0x83, 0x70 };
            byte[] serverSecret = SecureHandshake.DHSecret(clientPub, serverKey);
            byte[] expected = { 0x94, 0x57, 0xf4, 0x92, 0x89, 0xd8, 0x38, 0x66 };
            AssertEqualBytes(serverSecret, expected, "shim must byte-match skynet serverSecret");
        });

        // LoginParseResponse 实际收到的 1 字节 subid (server 用 err=1 占位)
        // 'MQ==' 4 字符 case 2 → 1 字节 = 0x31
        Test("Base64 decode 1B input (MQ==) -> 1B", () => {
            var b = SecureHandshake.Base64Decode("MQ==");
            AssertEqual(1, b.Length);
            AssertEqual(0x31, b[0]);
        });

        // ===== INTEGRATION: 模拟完整 client/server 加密握手 =====
        Test("INTEGRATION: full handshake flow (mock server, no real skynet)", () => {
            byte[] serverKey = SecureHandshake.RandomKey();
            byte[] serverPub = SecureHandshake.DHExchange(serverKey);

            byte[] challenge = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(challenge);
            string challengeB64 = SecureHandshake.Base64Encode(challenge);

            byte[] clientKey = SecureHandshake.RandomKey();
            byte[] clientPub = SecureHandshake.DHExchange(clientKey);
            byte[] clientSecret = SecureHandshake.DHSecret(serverPub, clientKey);
            byte[] serverSecret = SecureHandshake.DHSecret(clientPub, serverKey);

            AssertEqualBytes(clientSecret, serverSecret, "secrets must match");

            byte[] clientHmac = SecureHandshake.Hmac64(SecureHandshake.Base64Decode(challengeB64), clientSecret);
            byte[] serverHmac = SecureHandshake.Hmac64(SecureHandshake.Base64Decode(challengeB64), serverSecret);
            AssertEqualBytes(clientHmac, serverHmac, "HMACs must match");

            string tokenPlain = "user@server:pass";
            byte[] etoken = SecureHandshake.DesEncode(Encoding.UTF8.GetBytes(tokenPlain), clientSecret);
            byte[] tokenBack = SecureHandshake.DesDecode(SecureHandshake.Base64Decode(SecureHandshake.Base64Encode(etoken)), serverSecret);
            AssertEqual(tokenPlain, Encoding.UTF8.GetString(tokenBack));
        });

        Console.WriteLine("\n=== {0} passed, {1} failed ===", _passed, _failed);
        return _failed > 0 ? 1 : 0;
    }

    private static void Test(string name, Action test) {
        try {
            test();
            Console.WriteLine("  PASS {0}", name);
            _passed++;
        } catch (Exception e) {
            Console.WriteLine("  FAIL {0}: {1}", name, e.Message);
            _failed++;
        }
    }

    private static void AssertEqual(int expected, int actual, string msg = null) {
        if (expected != actual) {
            string extra = msg != null ? " (" + msg + ")" : "";
            throw new Exception(string.Format("expected {0}, got {1}{2}", expected, actual, extra));
        }
    }

    private static void AssertEqual(string expected, string actual) {
        if (expected != actual) {
            throw new Exception(string.Format("expected '{0}', got '{1}'", expected, actual));
        }
    }

    private static void AssertEqualBytes(byte[] a, byte[] b, string msg = null) {
        if (a.Length != b.Length) {
            string extra = msg != null ? " (" + msg + ")" : "";
            throw new Exception(string.Format("length {0} vs {1}{2}", a.Length, b.Length, extra));
        }
        for (int i = 0; i < a.Length; i++) {
            if (a[i] != b[i]) {
                string extra = msg != null ? " (" + msg + ")" : "";
                throw new Exception(string.Format("byte {0}: 0x{1:x2} vs 0x{2:x2}{3}", i, a[i], b[i], extra));
            }
        }
    }

    private static void AssertTrue(bool cond, string msg = null) {
        if (!cond) {
            throw new Exception("assertion failed" + (msg != null ? ": " + msg : ""));
        }
    }

    private static string BytesToHex(byte[] b) {
        var sb = new StringBuilder();
        for (int i = 0; i < b.Length; i++) sb.AppendFormat("{0:x2}", b[i]);
        return sb.ToString();
    }
}
