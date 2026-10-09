#!/bin/sh
# Builds and runs the tests on Linux with Mono, and compile-checks the whole tray app as C# 5
# against .NET Framework 4.8 reference assemblies. Needs: mono-devel, python3, and optionally
# dotnet-sdk-8.0 (for the Roslyn C# 5 check). Nothing here runs the Windows-only code.
# With ../stage1-tools beside this folder, it also checks the tray against the real tool's code.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
app="$here/.."
out="${TMPDIR:-/tmp}/dualconnect-tray-tests"
mkdir -p "$out"
ref=/usr/lib/mono/4.8-api

roslyn="$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | head -n 1 || true)"
if [ -n "$roslyn" ]; then
  echo "== C# 5 compile check of the whole tray app (Roslyn, warnings as errors)"
  dotnet "$roslyn" -nologo -noconfig -nostdlib+ -langversion:5 -warnaserror+ -warn:4 \
    -target:winexe -out:"$out/DualConnectTray.exe" \
    -r:$ref/mscorlib.dll -r:$ref/System.dll -r:$ref/System.Core.dll \
    -r:$ref/System.Drawing.dll -r:$ref/System.Windows.Forms.dll \
    "$app"/src/*.cs
fi

echo "== Mono compile check of the whole tray app"
mcs -langversion:5 -warnaserror+ -target:winexe -out:"$out/DualConnectTray.mono.exe" \
  -r:System.Drawing.dll -r:System.Windows.Forms.dll "$app"/src/*.cs

echo "== Core tests"
mcs -langversion:5 -warnaserror+ -out:"$out/CoreTests.exe" \
  "$app/src/TrayCore.cs" "$app/src/DualConnectRunner.cs" "$here/CoreTests.cs"
cp "$here/fake-dualconnect.py" "$out/fake-dualconnect"
chmod +x "$out/fake-dualconnect"
mono "$out/CoreTests.exe" "$out/fake-dualconnect"

stage1="$app/../stage1-tools/DualConnect.cs"
if [ -f "$stage1" ]; then
  echo "== Contract tests against the real Stage 1 code (run under Mono, so its Windows calls fail)"
  mkdir -p "$out/real"
  mcs -langversion:5 -target:library -out:"$out/DualConnect.dll" "$stage1"
  mcs -langversion:5 -out:"$out/real/DualConnect.exe" "$stage1"
  printf '#!/bin/sh\nexec mono "%s/real/DualConnect.exe" "$@"\n' "$out" > "$out/real-dualconnect"
  chmod +x "$out/real-dualconnect"
  mcs -langversion:5 -warnaserror+ -out:"$out/ContractTests.exe" -r:"$out/DualConnect.dll" \
    "$app/src/TrayCore.cs" "$app/src/DualConnectRunner.cs" "$here/ContractTests.cs"
  mono "$out/ContractTests.exe" "$stage1" "$out/real-dualconnect"
else
  echo "== Contract tests skipped: $stage1 not found"
fi
