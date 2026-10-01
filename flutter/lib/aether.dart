import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

class Preset {
  final String name, args, httpFlag;
  final int startupSecs;
  const Preset(this.name, this.args, [this.startupSecs = Aether.fastSecs, this.httpFlag = '--http-proxy']);

  bool get manualOnly => startupSecs > Aether.fastSecs; // too slow to wait for in the Auto rotation
  String get label => manualOnly ? '$name (manual)' : name;
}

typedef Log = void Function(String);

class Cancelled implements Exception {}

// Stand-in for CancellationToken: delays wake up early and throw once cancelled.
class Cancel {
  final _done = Completer<void>();
  bool get isCancelled => _done.isCompleted;
  void cancel() => isCancelled ? null : _done.complete();
  void check() => isCancelled ? throw Cancelled() : null;
  Future<void> delay(int ms) async {
    await Future.any([Future.delayed(Duration(milliseconds: ms)), _done.future]);
    check();
  }
}

// aether, running as a child process of the app (Android kills it together with the app).
class AetherProcess {
  final Process p;
  bool exited = false;
  AetherProcess(this.p) {
    p.exitCode.then((_) => exited = true);
  }
}

// Everything about the aether binary itself: where it lives, starting it, and testing its tunnel.
class Aether {
  static const testUrl = 'http://www.gstatic.com/generate_204';
  static const connectTimeoutMs = 10000; // after the proxy is up, the internet test must pass within this
  static const httpPort = 1819; // HTTP proxy: point Wi-Fi proxy / apps here
  static const socksPort = 1818; // SOCKS5, free for apps that support it

  static const _warp = '--turbo -4 --quick-reconnect';
  static const fastSecs = 30; // startup budget of the fast protocols, the only ones Auto rotates through

  // The protocol menu aether itself offers. Tor/Psiphon bootstrap slowly (tor tries 75s plainly, then bridges;
  // psiphon waits up to 180s), so they get longer startup budgets and run only when picked by hand.
  // The "alone" modes don't serve --http-proxy, so they expose their own HTTP proxy flag on the same port.
  static const presets = <Preset>[
    Preset('MASQUE', '--masque $_warp'),
    Preset('WireGuard', '--wg $_warp'),
    Preset('WARP-in-WARP (gool)', '--gool $_warp'),
    Preset('MASQUE-in-MASQUE', '--mim $_warp'),
    Preset('Tor alone', '--tor-only', 300, '--tor-http'),
    Preset('Tor + WARP', '--tor-reverse $_warp', 300), // warp reached via tor, exit via warp
    Preset('Psiphon alone', '--psiphon-only', 240, '--psiphon-http'),
    Preset('Psiphon + WARP', '--psiphon-reverse $_warp', 240), // warp reached via psiphon, exit via warp
  ];

  static late String exe; // libaether.so in nativeLibraryDir, packed by fetch_aether.sh
  static late String lyrebird; // tor bridge transport (obfs4, webtunnel), packed beside it as liblyrebird.so
  static late String psiphon; // psiphon-tunnel-core, packed beside it as libpsiphon.so
  static late String dir; // app files dir: aether's working dir, holds its identity .toml files

  static const _ch = MethodChannel('aether');

  static Future<void> init() async {
    final dirs = await _ch.invokeListMethod<String>('dirs');
    exe = '${dirs![0]}/libaether.so';
    lyrebird = '${dirs[0]}/liblyrebird.so';
    psiphon = '${dirs[0]}/libpsiphon.so';
    dir = dirs[1];
  }

  // Full-device VPN (tun2socks onto the SOCKS port); false if the user declined the VPN prompt.
  static Future<bool> vpnStart() async => await _ch.invokeMethod<bool>('vpnStart', socksPort) ?? false;
  static Future<void> vpnStop() => _ch.invokeMethod('vpnStop');

  // A real request through aether's HTTP proxy measures the real delay.
  static final _http = HttpClient()
    ..findProxy = ((_) => 'PROXY 127.0.0.1:$httpPort')
    ..connectionTimeout = const Duration(seconds: 3);

  static Future<AetherProcess?> start(Preset p, Log log) async {
    try {
      // aether looks for these helpers by their own names, which Android's lib*.so packing can't keep.
      final helper = p.args.contains('--tor')
          ? ['--tor-pt', lyrebird]
          : p.args.contains('--psiphon')
              ? ['--psiphon-bin', psiphon]
              : <String>[];
      final proc = await Process.start(
          exe, [...p.args.split(' '), ...helper, '--bind', '127.0.0.1:$socksPort', p.httpFlag, '127.0.0.1:$httpPort'],
          workingDirectory: dir, environment: {'HOME': dir});
      for (final s in [proc.stdout, proc.stderr]) {
        s.transform(const Utf8Decoder(allowMalformed: true)).transform(const LineSplitter()).listen(log);
      }
      return AetherProcess(proc);
    } catch (e) {
      log('Start failed: $e');
      return null;
    }
  }

  // Waits for a passing internet test through aether's proxy. Gives up once the startup budget is spent
  // and the proxy has been up for connectTimeoutMs without passing (tor opens its port long before it can carry traffic).
  static Future<int?> waitConnected(AetherProcess proc, int startupSecs, Cancel ct) async {
    final sw = Stopwatch()..start();
    int? upAt;
    while (!proc.exited) {
      if (upAt == null && await _portOpen()) upAt = sw.elapsedMilliseconds;
      if (upAt != null) {
        final ms = await ping(ct);
        if (ms != null) return ms;
      }
      final t = sw.elapsedMilliseconds;
      if (t > startupSecs * 1000 && (upAt == null || t - upAt > connectTimeoutMs)) return null;
      await ct.delay(500);
    }
    return null;
  }

  static Future<bool> _portOpen() async {
    try {
      (await Socket.connect('127.0.0.1', socksPort, timeout: const Duration(seconds: 1))).destroy();
      return true;
    } catch (_) {
      return false;
    }
  }

  static Future<int?> ping(Cancel ct) async {
    final sw = Stopwatch()..start();
    Future<int?> get() async {
      final req = await _http.getUrl(Uri.parse(testUrl));
      req.persistentConnection = false;
      final res = await req.close();
      await res.drain<void>();
      return res.statusCode == HttpStatus.noContent ? sw.elapsedMilliseconds : null;
    }

    int? ms;
    try {
      ms = await get().timeout(const Duration(seconds: 3));
    } catch (_) {}
    ct.check();
    return ms;
  }
}
