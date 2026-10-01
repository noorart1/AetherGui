#!/usr/bin/env bash
# Downloads aether for Android from the latest GitHub release, verifies SHA-256, and places it
# where Gradle packs it into the APK as libaether.so (Android 10+ only runs binaries shipped in the APK).
set -euo pipefail
cd "$(dirname "$0")"
base=https://github.com/CluvexStudio/Aether/releases/latest/download
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
for pair in arm64:arm64-v8a armv7:armeabi-v7a x86_64:x86_64; do
  arch=${pair%%:*} abi=${pair##*:} f=aether-android-$arch.tar.gz
  curl -fsSL -o "$tmp/$f" "$base/$f"
  curl -fsSL "$base/$f.sha256" | awk -v f="$tmp/$f" '{print $1"  "f}' | sha256sum -c --quiet
  mkdir -p "$tmp/$arch" android/app/src/main/jniLibs/$abi
  tar -xzf "$tmp/$f" -C "$tmp/$arch"
  find "$tmp/$arch" -type f -name aether -exec cp {} android/app/src/main/jniLibs/$abi/libaether.so \;
  test -s android/app/src/main/jniLibs/$abi/libaether.so
  # tor bridge transport and psiphon, run by aether via --tor-pt / --psiphon-bin (see aether.dart)
  cp "$tmp/$arch/pt/lyrebird" android/app/src/main/jniLibs/$abi/liblyrebird.so
  cp "$tmp/$arch/pt/psiphon-tunnel-core" android/app/src/main/jniLibs/$abi/libpsiphon.so
  echo "$abi ok"
done
# tun2socks for the VPN (AetherVpn.kt), built with the NDK into jniLibs as libhev-socks5-tunnel.so.
# JNI natives get registered on com.aether.aether_gui.AetherVpn via PKGNAME/CLSNAME.
git -c advice.detachedHead=false clone -q --depth 1 --branch 2.17.1 --recursive --shallow-submodules https://github.com/heiher/hev-socks5-tunnel "$tmp/hev"
# Windows git without symlink rights checks symlinks out as text files holding the target: copy the target in.
for repo in "$tmp/hev" $(git -C "$tmp/hev" submodule foreach --recursive --quiet pwd); do
  git -C "$repo" ls-files -s | awk '$1==120000{print $4}' | while read -r l; do
    f=$repo/$l; [ -L "$f" ] || cp "$(dirname "$f")/$(cat "$f")" "$f"
  done
done
ndk=${ANDROID_NDK_HOME:-$(ls -d "${ANDROID_HOME:-$LOCALAPPDATA/Android/Sdk}"/ndk/* | tail -1)}
nb=$ndk/ndk-build; [ -f "$nb" ] || nb=$nb.cmd
"$nb" -C "$tmp/hev" -j4 NDK_PROJECT_PATH=. APP_BUILD_SCRIPT=Android.mk NDK_APPLICATION_MK=Application.mk \
  APP_MODULES=hev-socks5-tunnel APP_ABI="arm64-v8a armeabi-v7a x86_64" \
  APP_CFLAGS="-O3 -DPKGNAME=com/aether/aether_gui -DCLSNAME=AetherVpn -DLWIP_ERRNO_STDINCLUDE=1" NDK_LIBS_OUT="$tmp/libs" # not jniLibs: ndk-build wipes its output dir, libaether.so included
for abi in arm64-v8a armeabi-v7a x86_64; do cp "$tmp/libs/$abi/libhev-socks5-tunnel.so" android/app/src/main/jniLibs/$abi/; done
echo "hev-socks5-tunnel ok"
