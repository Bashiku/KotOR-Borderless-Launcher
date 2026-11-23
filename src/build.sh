#!/bin/sh
# Builds kotorfix.dll (i686, no CRT) with msys64 clang64 LLVM tools.
set -e
export PATH=/c/msys64/clang64/bin:$PATH
cd "$(dirname "$0")"
mkdir -p obj
for l in kernel32 user32; do llvm-dlltool -m i386 -k -d $l.def -l obj/lib$l.a; done
llvm-windres --target=pe-i386 -I C:/msys64/clang64/include -O coff kotorfix.rc obj/kotorfix_res.o
clang --target=i686-w64-mingw32 -isystem C:/msys64/clang64/include \
  -O2 -ffreestanding -fno-builtin -fno-stack-protector -fno-asynchronous-unwind-tables \
  -Wall -Wno-unused-function -nostdlib -shared -fuse-ld=lld \
  -Wl,--entry,_DllMain@12 -Wl,--enable-stdcall-fixup \
  -Wl,--kill-at -o ../dinput8.dll kotorfix.c obj/kotorfix_res.o -Lobj -lkernel32 -luser32
echo "built ../dinput8.dll"

# Launcher (C#, .NET Framework 4.x, x86)
CSC=/c/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe
"$CSC" -nologo -platform:x86 -target:winexe -optimize+ -out:../KotorLauncher.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:Microsoft.CSharp.dll -r:System.Core.dll \
  -win32icon:kotor.ico -win32manifest:launcher.manifest AssemblyInfo.cs Core.cs Ui.cs
echo "built ../KotorLauncher.exe"
