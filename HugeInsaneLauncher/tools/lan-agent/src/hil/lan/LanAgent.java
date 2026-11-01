package hil.lan;

import java.lang.instrument.ClassFileTransformer;
import java.lang.instrument.Instrumentation;
import java.security.ProtectionDomain;
import java.io.ByteArrayOutputStream;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * HIL LAN 破墙 agent：让「对局域网开放」的世界接纳离线玩家。
 *
 * 原理：LAN 服务端 online-mode 硬编码 true，玩家进服时服务端调用 authlib 的
 * YggdrasilMinecraftSessionService.hasJoinedServer() 向 mojang 验证会话，离线玩家在此被踢。
 * 本 agent 把该方法体替换为直接放行。authlib 不参与混淆，但签名随版本分两代：
 *   老（authlib ≤5.x，MC ~1.18 及更早）：HasJoinedMinecraftServerResponse hasJoinedServer(GameProfile, String, InetAddress)
 *   新（authlib 6/7+，MC 1.19+）：              ProfileResult hasJoinedServer(String, String, InetAddress)
 * 两代都覆盖。只影响「本机作为服务端被连接」的验证路径；进别人服务器的客户端侧不受影响。
 *
 * 零依赖：class 文件用内置 mini patcher 手改（不 shade ASM——ASM 进 system classpath 会触发
 * Fabric Loader 的 duplicate-ASM 校验把游戏直接挡死）。
 */
public final class LanAgent {

    static final String TARGET = "com/mojang/authlib/yggdrasil/YggdrasilMinecraftSessionService";

    public static void premain(String args, Instrumentation inst) { install(inst); }
    public static void agentmain(String args, Instrumentation inst) { install(inst); }

    static void install(Instrumentation inst) {
        inst.addTransformer(new ClassFileTransformer() {
            public byte[] transform(ClassLoader loader, String className, Class<?> clazz,
                                    ProtectionDomain domain, byte[] bytes) {
                if (!TARGET.equals(className) || bytes == null) return null;
                try {
                    byte[] out = Patcher.patch(bytes);
                    if (out != null) {
                        System.out.println("[HIL] LAN 破墙已注入：离线玩家可加入本机的局域网世界");
                        return out;
                    }
                    System.out.println("[HIL] LAN 破墙：不认识的 authlib 签名，未改动（联机可能仍要正版）");
                    return null;
                } catch (Throwable t) {
                    System.out.println("[HIL] LAN 破墙注入失败（不影响游戏）: " + t);
                    return null;
                }
            }
        });
    }

    // ================= 零依赖 mini class patcher =================
    // 只支持本 agent 需要的读写面：解析常量池/方法表，替换目标方法的 Code 属性，
    // 缺的常量追加到常量池末尾。其余字节原样拷贝。
    static final class Patcher {

        // ---- 目标方法两种形态 ----
        static final String RESP_OLD = "com/mojang/authlib/yggdrasil/HasJoinedMinecraftServerResponse";
        static final String RESP_NEW = "com/mojang/authlib/yggdrasil/ProfileResult";
        static final String GP = "com/mojang/authlib/GameProfile";

        static byte[] patch(byte[] cls) throws Exception {
            Reader r = new Reader(cls);
            if ((r.u4() & 0xFFFFFFFFL) != 0xCAFEBABEL) return null;   // int→long 符号扩展会毁掉直接比较
            int minor = r.u2(), major = r.u2();
            int cpCount = r.u2();
            int cpStart = r.pos;

            // ---- 第一遍：解析常量池 ----
            String[] utf = new String[cpCount];
            int[] classNameIdx = new int[cpCount];   // Class → name 的 utf 索引
            int[] strUtfIdx = new int[cpCount];      // String → utf 索引
            int[] natNameIdx = new int[cpCount];     // NameAndType → name utf 索引
            int[] natDescIdx = new int[cpCount];     // NameAndType → desc utf 索引
            int[] refClassIdx = new int[cpCount];    // Field/Methodref → class 索引
            int[] refNatIdx = new int[cpCount];      // Field/Methodref → nat 索引
            int[] tagOf = new int[cpCount];
            for (int i = 1; i < cpCount; i++) {
                int tag = r.u1();
                tagOf[i] = tag;
                switch (tag) {
                    case 1: { // UTF8
                        int len = r.u2();
                        utf[i] = new String(cls, r.pos, len, "UTF-8");
                        r.pos += len;
                        break;
                    }
                    case 7: classNameIdx[i] = r.u2(); break;   // Class
                    case 8: strUtfIdx[i] = r.u2(); break;      // String
                    case 9: case 10: case 11:                  // Field/Method/InterfaceMethodref
                    case 12:                                   // NameAndType
                    case 17: case 18: {                        // InvokeDynamic / Dynamic
                        int a = r.u2(), b2 = r.u2();
                        if (tag == 12) { natNameIdx[i] = a; natDescIdx[i] = b2; }
                        else { refClassIdx[i] = a; refNatIdx[i] = b2; }
                        break;
                    }
                    case 3: case 4: case 15: case 16: case 19: case 20: r.pos += tag == 15 ? 3 : (tag == 16 || tag == 19 || tag == 20 ? 2 : 4); break;
                    case 5: case 6: r.pos += 8; i++; break;    // Long/Double 占两槽
                    default: throw new Exception("未知常量 tag " + tag);
                }
            }
            int cpEnd = r.pos;

            // ---- 第二遍：跳过头部（access/this/super/interfaces/fields），到方法表 ----
            r.pos = cpEnd;
            r.pos += 6;                    // access, this, super
            int ifaces = r.u2(); r.pos += ifaces * 2;
            int fields = r.u2();
            for (int i = 0; i < fields; i++) {
                r.pos += 6;
                int ac = r.u2();
                for (int a = 0; a < ac; a++) { r.u2(); int aLen = r.u4(); r.pos += aLen; }
            }
            int methodsPos = r.pos;        // 方法表起始（含 count）
            int methods = r.u2();

            // 逐个方法：记录区间，收集全部 hasJoinedServer 重载（老版有 2 参/3 参两个，都要 patch）
            List<int[]> methodSpans = new ArrayList<int[]>(); // {start, end}
            List<int[]> targets = new ArrayList<int[]>();     // {start, end, codeStart, codeEnd, codeNameIdx, locals}
            List<Boolean> targetNew = new ArrayList<Boolean>();
            for (int m = 0; m < methods; m++) {
                int mStart = r.pos;
                r.pos += 4;                // access, name
                int descIdx = r.u2();
                int ac = r.u2();
                String name = utf[readU2(cls, mStart + 2)];
                String desc = utf[descIdx];
                boolean isTarget = "hasJoinedServer".equals(name)
                        && (desc.endsWith(")L" + RESP_OLD + ";") && desc.startsWith("(L" + GP + ";")
                         || desc.endsWith(")L" + RESP_NEW + ";") && desc.startsWith("(Ljava/lang/String;"));
                int codeStart = -1, codeEnd = -1, codeNameIdx = -1;
                for (int a = 0; a < ac; a++) {
                    int aStart = r.pos;
                    int aName = r.u2();
                    int aLen = r.u4();
                    r.pos += aLen;
                    if (isTarget && "Code".equals(utf[aName])) { codeStart = aStart; codeEnd = r.pos; codeNameIdx = aName; }
                }
                if (isTarget && codeStart >= 0) {
                    targets.add(new int[]{mStart, r.pos, codeStart, codeEnd, codeNameIdx, 1 + countSlots(desc)});
                    targetNew.add(desc.endsWith(")L" + RESP_NEW + ";"));
                }
                methodSpans.add(new int[]{mStart, r.pos});
            }
            if (targets.isEmpty()) return null;
            int classAttrsPos = r.pos;

            // ---- 常量补齐（复用已有，缺的追加；多目标共享同一批常量）----
            Cp cp = new Cp(utf, classNameIdx, strUtfIdx, natNameIdx, natDescIdx, refClassIdx, refNatIdx, tagOf, cpCount);
            byte[] bodyOld = null, bodyNew = null;
            for (int i = 0; i < targets.size(); i++)
                if (targetNew.get(i)) { if (bodyNew == null) bodyNew = buildNewBody(cp); }
                else { if (bodyOld == null) bodyOld = buildOldBody(cp); }

            // ---- 组装输出 ----
            ByteArrayOutputStream out = new ByteArrayOutputStream(cls.length + 4096);
            writeU4(out, 0xCAFEBABE); writeU2(out, minor); writeU2(out, major);
            writeU2(out, cpCount + cp.added);
            out.write(cls, cpStart, cpEnd - cpStart);        // 原常量池
            out.write(cp.extra.toByteArray(), 0, cp.extra.size());  // 追加常量
            out.write(cls, cpEnd, methodsPos - cpEnd);       // access..fields 原样
            writeU2(out, methods);
            for (int i = 0; i < methodSpans.size(); i++) {
                int[] span = methodSpans.get(i);
                int ti = -1;
                for (int t = 0; t < targets.size(); t++) if (targets.get(t)[0] == span[0]) { ti = t; break; }
                if (ti < 0) { out.write(cls, span[0], span[1] - span[0]); continue; }
                int[] target = targets.get(ti);
                byte[] body = targetNew.get(ti) ? bodyNew : bodyOld;
                int maxStack = targetNew.get(ti) ? 6 : 3;
                // 目标方法：头部（access/name/desc/attrCount）原样，Code 换新，其他属性原样
                out.write(cls, span[0], 8);
                out.write(cls, span[0] + 8, target[2] - (span[0] + 8));
                ByteArrayOutputStream code = new ByteArrayOutputStream();
                writeU2(code, maxStack); writeU2(code, target[5]);
                writeU4(code, body.length); code.write(body, 0, body.length);
                writeU2(code, 0); writeU2(code, 0);          // 无异常表、无内嵌属性（直线代码无需 StackMapTable）
                byte[] codeBytes = code.toByteArray();
                writeU2(out, target[4]); writeU4(out, codeBytes.length); out.write(codeBytes, 0, codeBytes.length);
                out.write(cls, target[3], target[1] - target[3]);
            }
            out.write(cls, classAttrsPos, cls.length - classAttrsPos);  // 类属性原样
            return out.toByteArray();
        }

        // 老签名方法体：return new HasJoinedMinecraftServerResponse((GameProfile)arg1)
        static byte[] buildOldBody(Cp cp) {
            int cResp = cp.class_(RESP_OLD);
            int mInit = cp.methodref(cResp, "<init>", "(L" + GP + ";)V");
            ByteArrayOutputStream b = new ByteArrayOutputStream();
            b.write(0xBB); writeU2(b, cResp);       // new
            b.write(0x59);                          // dup
            b.write(0x2B);                          // aload_1
            b.write(0xB7); writeU2(b, mInit);       // invokespecial <init>
            b.write(0xB0);                          // areturn
            return b.toByteArray();
        }

        // 新签名方法体：return new ProfileResult(new GameProfile(离线UUID(name), name))
        static byte[] buildNewBody(Cp cp) {
            int cPR = cp.class_(RESP_NEW);
            int mPR = cp.methodref(cPR, "<init>", "(L" + GP + ";)V");
            int cGP = cp.class_(GP);
            int mGP = cp.methodref(cGP, "<init>", "(Ljava/util/UUID;Ljava/lang/String;)V");
            int cStr = cp.class_("java/lang/String");
            int mConcat = cp.methodref(cStr, "concat", "(Ljava/lang/String;)Ljava/lang/String;");
            int mGetBytes = cp.methodref(cStr, "getBytes", "(Ljava/nio/charset/Charset;)[B");
            int fUtf8 = cp.fieldref(cp.class_("java/nio/charset/StandardCharsets"), "UTF_8", "Ljava/nio/charset/Charset;");
            int mUuid = cp.methodref(cp.class_("java/util/UUID"), "nameUUIDFromBytes", "([B)Ljava/util/UUID;");
            int sOff = cp.string("OfflinePlayer:");

            ByteArrayOutputStream b = new ByteArrayOutputStream();
            b.write(0xBB); writeU2(b, cPR);         // new ProfileResult
            b.write(0x59);                          // dup
            b.write(0xBB); writeU2(b, cGP);         // new GameProfile
            b.write(0x59);                          // dup
            b.write(0x13); writeU2(b, sOff);        // ldc_w "OfflinePlayer:"
            b.write(0x2B);                          // aload_1 (name)
            b.write(0xB6); writeU2(b, mConcat);     // String.concat
            b.write(0xB2); writeU2(b, fUtf8);       // getstatic UTF_8
            b.write(0xB6); writeU2(b, mGetBytes);   // getBytes(UTF_8)
            b.write(0xB8); writeU2(b, mUuid);       // UUID.nameUUIDFromBytes
            b.write(0x2B);                          // aload_1 (name)
            b.write(0xB7); writeU2(b, mGP);         // GameProfile.<init>(UUID, String)
            b.write(0xB7); writeU2(b, mPR);         // ProfileResult.<init>(GameProfile)
            b.write(0xB0);                          // areturn
            return b.toByteArray();
        }

        // 描述符参数槽数（L;=1，J/D=2）
        static int countSlots(String desc) {
            int slots = 0;
            for (int i = 1; desc.charAt(i) != ')';) {
                char c = desc.charAt(i);
                if (c == 'L') { while (desc.charAt(i) != ';') i++; i++; slots++; }
                else if (c == '[') { i++; }
                else { if (c == 'J' || c == 'D') slots += 2; else slots++; i++; }
            }
            return slots;
        }

        static int readU2(byte[] b, int pos) { return ((b[pos] & 0xFF) << 8) | (b[pos + 1] & 0xFF); }
        static void writeU2(ByteArrayOutputStream o, int v) { o.write((v >> 8) & 0xFF); o.write(v & 0xFF); }
        static void writeU4(ByteArrayOutputStream o, long v) {
            o.write((int) (v >> 24) & 0xFF); o.write((int) (v >> 16) & 0xFF);
            o.write((int) (v >> 8) & 0xFF); o.write((int) v & 0xFF);
        }

        static final class Reader {
            final byte[] b; int pos;
            Reader(byte[] b) { this.b = b; }
            int u1() { return b[pos++] & 0xFF; }
            int u2() { int v = ((b[pos] & 0xFF) << 8) | (b[pos + 1] & 0xFF); pos += 2; return v; }
            int u4() { int v = ((b[pos] & 0xFF) << 24) | ((b[pos + 1] & 0xFF) << 16) | ((b[pos + 2] & 0xFF) << 8) | (b[pos + 3] & 0xFF); pos += 4; return v; }
        }

        // 常量池助手：复用已有项，缺的追加到 extra
        static final class Cp {
            final String[] utf; final int[] className, strUtf, natName, natDesc, refClass, refNat, tag;
            final int base;              // 原常量池项数（新索引从这里开始）
            int added = 0;
            final ByteArrayOutputStream extra = new ByteArrayOutputStream();
            final Map<String, Integer> addedIdx = new HashMap<String, Integer>();

            Cp(String[] utf, int[] cn, int[] su, int[] nn, int[] nd, int[] rc, int[] rn, int[] tag, int base) {
                this.utf = utf; this.className = cn; this.strUtf = su; this.natName = nn; this.natDesc = nd;
                this.refClass = rc; this.refNat = rn; this.tag = tag; this.base = base;
            }

            int utf8(String s) {
                for (int i = 1; i < base; i++) if (tag[i] == 1 && s.equals(utf[i])) return i;
                Integer ex = addedIdx.get("u" + s); if (ex != null) return ex.intValue();
                int idx = base + added++;
                extra.write(1);
                byte[] sb;
                try { sb = s.getBytes("UTF-8"); } catch (Exception e) { throw new RuntimeException(e); }
                writeU2(extra, sb.length); extra.write(sb, 0, sb.length);
                addedIdx.put("u" + s, idx);
                return idx;
            }

            int class_(String internal) {
                for (int i = 1; i < base; i++)
                    if (tag[i] == 7 && internal.equals(utf[className[i]])) return i;
                Integer ex = addedIdx.get("c" + internal); if (ex != null) return ex.intValue();
                int nameIdx = utf8(internal);          // 子项必须先索引先写入，字节顺序与索引顺序一致
                int idx = base + added++;
                extra.write(7); writeU2(extra, nameIdx);
                addedIdx.put("c" + internal, idx);
                return idx;
            }

            int string(String s) {
                for (int i = 1; i < base; i++)
                    if (tag[i] == 8 && s.equals(utf[strUtf[i]])) return i;
                Integer ex = addedIdx.get("s" + s); if (ex != null) return ex.intValue();
                int utfIdx = utf8(s);
                int idx = base + added++;
                extra.write(8); writeU2(extra, utfIdx);
                addedIdx.put("s" + s, idx);
                return idx;
            }

            int nameAndType(String name, String desc) {
                for (int i = 1; i < base; i++)
                    if (tag[i] == 12 && name.equals(utf[natName[i]]) && desc.equals(utf[natDesc[i]])) return i;
                Integer ex = addedIdx.get("n" + name + desc); if (ex != null) return ex.intValue();
                int n = utf8(name), d = utf8(desc);
                int idx = base + added++;
                extra.write(12); writeU2(extra, n); writeU2(extra, d);
                addedIdx.put("n" + name + desc, idx);
                return idx;
            }

            int methodref(int classIdx, String name, String desc) { return ref(10, classIdx, name, desc); }
            int fieldref(int classIdx, String name, String desc) { return ref(9, classIdx, name, desc); }

            int ref(int tagWanted, int classIdx, String name, String desc) {
                String clsName = null;
                if (classIdx < base) clsName = utf[className[classIdx]];
                for (int i = 1; i < base; i++)
                    if (tag[i] == tagWanted) {
                        boolean classSame = refClass[i] == classIdx
                                || (clsName != null && clsName.equals(utf[className[refClass[i]]]));
                        if (classSame && name.equals(utf[natName[refNat[i]]]) && desc.equals(utf[natDesc[refNat[i]]]))
                            return i;
                    }
                String key = "r" + tagWanted + classIdx + name + desc;
                Integer ex = addedIdx.get(key); if (ex != null) return ex.intValue();
                int nat = nameAndType(name, desc);     // 子项先行
                int idx = base + added++;
                extra.write(tagWanted); writeU2(extra, classIdx); writeU2(extra, nat);
                addedIdx.put(key, idx);
                return idx;
            }
        }
    }
}
