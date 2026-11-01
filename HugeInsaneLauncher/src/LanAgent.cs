// HugeLauncher - LAN 破墙 agent 释放器
// agent jar（tools/lan-agent 构建）内嵌在 exe 里，启动游戏前释放到 HugeLauncher/，
// 经 -javaagent 挂到 MC 客户端，把 LAN 世界的服务端侧会话验证放行（离线玩家可进）。
using System;
using System.IO;
using System.Reflection;

namespace HugeLauncher
{
    static class LanAgent
    {
        const string ResourceName = "hil-lan-agent.jar";
        static string prepared;   // 已释放的 jar 路径（null=未尝试，""=释放失败）

        // 返回可用的 agent jar 路径；释放失败返回 null（游戏照常启动，只是没破墙）
        public static string Ensure()
        {
            if (prepared != null) return prepared.Length > 0 ? prepared : null;
            prepared = "";
            try
            {
                string dir = Path.Combine(Paths.Root, "HugeLauncher");
                Directory.CreateDirectory(dir);
                string jar = Path.Combine(dir, ResourceName);
                using (var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (rs == null) { HilLog.Write("LAN破墙 内嵌 agent 资源缺失"); return null; }
                    // 已存在且大小一致就复用（jar 内容只随启动器版本变）
                    if (File.Exists(jar) && new FileInfo(jar).Length == rs.Length)
                    {
                        prepared = jar;
                        return prepared;
                    }
                    string tmp = jar + ".tmp";
                    using (var fs = File.Create(tmp)) rs.CopyTo(fs);
                    if (File.Exists(jar)) File.Delete(jar);
                    File.Move(tmp, jar);
                }
                prepared = jar;
                HilLog.Write("LAN破墙 agent 已释放: " + jar);
                // 品牌改名遗留清理：旧文件名的 agent jar 没用了
                try { string oldJar = Path.Combine(dir, "hgl-lan-agent.jar"); if (File.Exists(oldJar)) File.Delete(oldJar); } catch { }
            }
            catch (Exception ex)
            {
                HilLog.Write("LAN破墙 agent 释放失败（不影响启动）: " + ex.Message);
            }
            return prepared.Length > 0 ? prepared : null;
        }
    }
}
