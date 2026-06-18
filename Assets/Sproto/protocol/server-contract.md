# skynet 服务端契约:handshake + getPlayerData

> 给服务端同学看。本文档定义两个 sproto 协议的服务器侧语义。
> 协议定义文件:`Assets/Sproto/protocol/game.sproto`(由 sprotodump 双向生成,改完同步更新本文档)

---

## handshake (tag=1)

### 客户端请求
无 request payload(客户端发空包)。

### 服务端响应(handshake.response)
```sproto
response {
    result 0 : integer      # 1 = 成功, 非 0 = 错误码(>1 业务,<0 系统)
    uid 1 : integer         # 玩家唯一 ID(由 login 服务在用户表里分配的,game 服从 session 查)
    dataVersion 2 : integer # 该玩家当前数据版本号,首次握手时为 1(任何修改都自增)
}
```

### 服务端实现要点
1. 在 skynet gateserver 文本握手通过后(收到 "200 OK" 之后)立即回本响应
2. `uid`:从当前 skynet session 的 user 表里取(login 服在认证时已写入 session context)
3. `dataVersion`:从玩家持久化存储(Redis/DB)取最近一次自增 ID,客户端用此做 ETag 条件拉取
4. 鉴权失败:回 `result = 401` / 403,不要回 uid(避免信息泄露)

---

## getPlayerData (tag=4)

### 客户端请求
```sproto
request {
    uid 0 : integer
    version 1 : integer    # 客户端当前已知版本(0 = 无缓存)
}
```

### 服务端响应(getPlayerData.response)
```sproto
response {
    result 0 : integer       # 1 = 成功, 非 1 = 错误码
    version 1 : integer      # 当前服务器版本(可能 > request.version)
    level 2 : integer
    exp 3 : integer
    reason 4 : integer
    charList 5 : *charData
    squad 6 : *string        # 长度必须 = 4
    desktopChar 7 : string
    items 8 : *itemStack
    permissions 9 : *string
}
```

### 子结构
```sproto
charData {
    id 0 : string
    elite 1 : integer        # 0-2
    level 2 : integer
    exp 3 : integer
    trust 4 : integer
}

itemStack {
    id 0 : integer
    amount 1 : integer
}
```

### 服务端实现要点
1. 根据 `request.uid` 查玩家数据(`SELECT ... FROM player WHERE uid = ?`)
2. 如果 `request.version > 0 && request.version == dataVersion`(客户端缓存有效):**仍然返回 200 + 全量数据**(客户端可能版本号被重置过),简化逻辑
3. `result != 1` 表示查表失败/玩家不存在/权限不足,不要回部分字段(避免客户端解析异常)
4. `squad` 数组长度必须恰好 4(客户端会按这个长度对齐,不够补空串、多了截断)
5. `charList` / `items` / `permissions` 用空数组(`*` 字段)而不是 null,客户端按 foreach 处理
6. **任何对玩家数据的修改**(升级、获得物品、抽卡)都必须让 `dataVersion` 自增,这是客户端 ETag 机制的基础

### 推荐数据存储格式
- 单玩家 JSON 文件 / Redis Hash / MySQL 行,字段对应上面 sproto
- `dataVersion` 独立字段(不是 timestamp,是单调自增整数),维护成本低

---

## 错误码约定
- `result = 1` 成功
- `result = 401` 未鉴权(handshake 阶段)/ session 过期(getPlayerData 阶段)
- `result = 404` 玩家不存在
- `result = 500` 服务器内部错误
- 其他值保留作扩展

---

## 版本兼容规则
- sproto 协议**只能加字段,不能改/删已有字段**。tag 编号一旦定下来**不能复用**
- 客户端保留旧 tag 永远兼容服务端增字段
- 服务端如果要废弃字段,新版本把字段保留但不再写值,等所有客户端升级后下个大版本删除