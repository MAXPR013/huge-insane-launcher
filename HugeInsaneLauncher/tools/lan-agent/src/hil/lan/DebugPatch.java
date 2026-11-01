package hil.lan;

import java.nio.file.Files;
import java.nio.file.Paths;

/** 调试：对指定 class 跑 patcher 并写出，供 javap 检查结构 */
public class DebugPatch {
    public static void main(String[] a) throws Exception {
        byte[] in = Files.readAllBytes(Paths.get(a[0]));
        byte[] out = LanAgent.Patcher.patch(in);
        if (out == null) { System.out.println("patcher 返回 null（不匹配）"); return; }
        Files.write(Paths.get(a[1]), out);
        System.out.println("patched: " + in.length + " → " + out.length);
    }
}
