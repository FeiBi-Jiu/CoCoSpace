using System;
using System.Collections.Generic;
using Game.Proto;

namespace Game.Net
{
    public static class MsgIdMap
    {
        // msgId → 对应 payload 类型
        public static readonly Dictionary<MsgId, Type> IdToType = new()
        {
            [MsgId.S2CLoginOk]         = typeof(S2C_LoginOk),
            [MsgId.S2CSnapshot]        = typeof(S2C_Snapshot),
            [MsgId.S2CMapLayout]       = typeof(MapLayout),
            [MsgId.S2CHitApply]        = typeof(S2CHitApply),
            [MsgId.S2CUpgradeChoice]   = typeof(S2CUpgradeChoice),
            [MsgId.S2CDropSpawn]       = typeof(S2CDropSpawn),
            [MsgId.S2CEnrage]          = typeof(S2CEnrage),
            [MsgId.S2CBossCharge]      = typeof(S2CBossCharge),
            [MsgId.S2CArmorBreak]      = typeof(S2CArmorBreak),
            [MsgId.S2CBulletRedirect]  = typeof(S2CBulletRedirect),
            [MsgId.S2CGameOver]        = typeof(S2CGameOver),
            [MsgId.S2CShopOpen]        = typeof(S2CShopOpen),
            [MsgId.S2CDeath]           = typeof(S2CDeath),
            [MsgId.S2CReconnectAck]    = typeof(S2CReconnectAck),
            [MsgId.S2CReviveProgress]  = typeof(S2CReviveProgress),
            [MsgId.S2CRevived]         = typeof(S2CRevived),
            [MsgId.S2CReviveInterrupted] = typeof(S2CReviveInterrupted),
            [MsgId.S2CGhostTether]     = typeof(S2CGhostTether),
            [MsgId.S2CGhostTetherBreak] = typeof(S2CGhostTetherBreak),
            [MsgId.S2CItemCarried]     = typeof(S2CItemCarried),
            [MsgId.S2CGhostBuff]       = typeof(S2CGhostBuff),
            [MsgId.S2CError]           = typeof(Error),
        };

        // 反向：消息类型 → msgId（上行打包用）
        public static readonly Dictionary<Type, MsgId> TypeToId = new()
        {
            [typeof(C2S_Login)]    = MsgId.C2SLogin,
            [typeof(C2S_Intent)]   = MsgId.C2SIntent,
            [typeof(C2SPickUpgrade)] = MsgId.C2SPickUpgrade,
            [typeof(C2SBuy)]      = MsgId.C2SBuy,
            [typeof(C2SRevive)]   = MsgId.C2SRevive,
            [typeof(C2S_Reconnect)]= MsgId.C2SReconnect,
        };
    }
}