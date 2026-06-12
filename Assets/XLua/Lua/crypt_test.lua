local crypt = require "crypt"

return {
    HashKey = crypt.hashkey,
    RandomKey = crypt.randomkey,
    DesEncode = crypt.desencode,
    DesDecode = crypt.desdecode,
    HexEncode = crypt.tohex,
    HexDecode = crypt.fromhex,
    Hmac64 = crypt.hmac64,
    Hmac64Md5 = crypt.hmac64_md5,
    DhExchange = crypt.dhexchange,
    DhSecret = crypt.dhsecret,
    Base64Encode = crypt.base64encode,
    Base64Decode = crypt.base64decode,
}
