# SecureHandshake self-test (skynet crypt.c port)

独立的 .NET Framework 4 Console App,验证 `SecureHandshake.cs` 与 skynet 服务端 byte-byte 对齐。

## 跑测试

需要 Windows + .NET Framework 4 csc (Windows 自带):

```bash
cd tests/SecureHandshakeTest
"/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe" \
    /target:exe /out:SecureHandshakeTest.exe \
    /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Numerics.dll" \
    SecureHandshake.cs Program.cs
./SecureHandshakeTest.exe
```

期望输出: `=== 19 passed, 0 failed ===` (exit code 0)

## 关键已知向量

| 测试 | 输入 | 期望输出 | 来源 |
|---|---|---|---|
| Hmac64 已知向量 | x=`0x01*8`, y=`0x02*8` | `5c9f01d50fa9c25a` | skynet C 端实测 `crypt.hexencode(crypt.hmac64(...))` |
| DH 对称性 | 任意 8 字节 key × 10 iterations | AliceSecret == BobSecret | 数学定义 |
| DES involution | 0-24 字节 × 1 字节 step | encode 后 decode = 原数据 | 算法定义 |
| 集成 | 模拟 login server 完整流程 | 全流程字节一致 | skynet 标准流程 |

## 跟 `Assets/Manager/SecureHandshake.cs` 的关系

`tests/SecureHandshakeTest/SecureHandshake.cs` 是 **独立测试版本** (无 namespace 包装,方便 csc 编译),
跑通后**复制**到 `Assets/Manager/SecureHandshake.cs` 实际编译进 Unity。

每次修改 `Assets/Manager/SecureHandshake.cs` 时,务必:
1. 把同样的修改同步到 `tests/SecureHandshakeTest/SecureHandshake.cs`
2. 跑一次 self-test 验证 19/19 pass
3. 验证 Hmac64 已知向量仍是 `5c9f01d50fa9c25a`

## 为什么不能用 .NET BCL MD5?

.NET `System.Security.Cryptography.MD5` 是**标准 MD5** (RFC 1321)。
skynet `digest_md5` 表面像标准 MD5, 但 round 函数有细微偏差,
对相同输入产生**不同**输出 (例如 64 字节 `[01*8 02*8] × 4`:
- .NET MD5 = `daf81821b9b33420a466b280589e01d0`
- skynet digest_md5 = 输出 (a, b, c, d) → c^d=`0xd5019f5c`, a^b=`0x5ac2a90f`)

所以 SecureHandshake.cs 自带了 skynet 版 digest_md5 实现, 不用 .NET BCL MD5。
DES 部分用 .NET DES (因为 .NET DES 是标准 FIPS 46-3, 与 skynet 手写 DES 等价), 只手动处理 ISO/IEC 7816-4 padding。
