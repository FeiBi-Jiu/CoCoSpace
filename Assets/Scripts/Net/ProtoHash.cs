// 用 proto 目录内容的哈希作为版本号，登录时随 C2S_Login 上送，
// 服务器比对不一致则拒绝/下发新表，避免协议不匹配的诡异 bug
public static class ProtoHash
{
    public const string Version = "a1b2c3d4"; // 跑完 gen_protos_cs.sh 后手动更新（或脚本生成写死）
}
