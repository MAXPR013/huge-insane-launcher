@echo off
rem HIL LAN 破墙 agent 构建：零依赖（手写 class patcher，不 shade ASM——
rem ASM 进 system classpath 会触发 Fabric 的 duplicate-ASM 校验），内嵌进 HIL.exe
setlocal
set ROOT=..\..\..
set JAVAC=%ROOT%\.minecraft\runtime\java-runtime-epsilon\bin\javac.exe
set JAR=%ROOT%\.minecraft\runtime\java-runtime-epsilon\bin\jar.exe
if not exist "%JAVAC%" set JAVAC=%ROOT%\.minecraft\runtime\jre\bin\javac.exe
if not exist "%JAR%" set JAR=%ROOT%\.minecraft\runtime\jre\bin\jar.exe

rd /s /q build 2>nul
mkdir build
"%JAVAC%" --release 8 -encoding UTF-8 -d build src\hil\lan\LanAgent.java || exit /b 1
"%JAR%" cfm hil-lan-agent.jar MANIFEST.MF -C build . || exit /b 1
echo [OK] hil-lan-agent.jar 构建完成（零依赖）
