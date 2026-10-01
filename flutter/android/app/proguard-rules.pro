# hev-socks5-tunnel JNI_OnLoad registers every TProxy* native by name; a stripped one aborts the app.
-keep class com.aether.aether_gui.AetherVpn { native <methods>; }
