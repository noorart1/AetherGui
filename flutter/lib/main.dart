import 'dart:io';
import 'dart:math';

import 'package:flutter/material.dart';

import 'aether.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Aether.init();
  runApp(
    MaterialApp(
      title: 'Aether VPN',
      debugShowCheckedModeBanner: false,
      // Dark only: the status colors below are picked for contrast on this dark surface.
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: Palette.primary,
          brightness: Brightness.dark,
          surface: Palette.background,
        ),
        scaffoldBackgroundColor: Palette.background,
      ),
      home: const HomePage(),
    ),
  );
}

// "Shield dark + connected green" palette (ui-ux-pro-max): one place for every color the app uses.
abstract final class Palette {
  static const background = Color(0xFF0F172A);
  static const muted = Color(0xFF10192E);
  static const primary = Color(0xFF1E3A5F);
  static const mutedText = Color(0xFF94A3B8);
  static const connected = Color(0xFF22C55E);
  static const connecting = Color(0xFFF59E0B);
  static const disconnected = Color(0xFFEF4444);
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  static const startupDelayMs =
      3000; // pause before the automatic connect on startup
  static const red = Palette.disconnected,
      orange = Palette.connecting,
      seaGreen = Palette.connected;

  AetherProcess? _process;
  Cancel? _ct;
  bool _autoConnect = false;
  int _preset = -1; // index into Aether.presets, -1 = Auto (rotate through all)
  String _status = 'Disconnected';
  Color _statusColor = red;
  String _delay = 'Delay: -';
  final _lines = <(String, Color?)>[];
  final _scroll = ScrollController();
  final _rng = Random();

  File get _settings => File('${Aether.dir}/settings.txt');

  @override
  void initState() {
    super.initState();
    try {
      final s = _settings.readAsStringSync();
      _autoConnect = s.contains('AutoConnect=1');
      final i =
          int.tryParse(
            RegExp(r'Preset=(-?\d+)').firstMatch(s)?.group(1) ?? '',
          ) ??
          -1;
      _preset = i < Aether.presets.length ? i : -1;
    } catch (_) {}
    if (_autoConnect) {
      _log('Auto-connecting in ${startupDelayMs ~/ 1000}s...');
      Future.delayed(const Duration(milliseconds: startupDelayMs), () {
        if (mounted && _autoConnect && _ct == null) _connect();
      });
    }
  }

  @override
  void dispose() {
    _ct?.cancel();
    _killProcess();
    super.dispose();
  }

  Future<void> _connect() async {
    if (_ct != null) return _ct!.cancel();
    final ct = _ct = Cancel();
    setState(() {});
    await _run(ct);
    _ct = null;
    if (mounted) setState(() {});
  }

  void _saveSettings() => _settings.writeAsStringSync(
    'AutoConnect=${_autoConnect ? 1 : 0}\nPreset=$_preset\n',
  );

  // Auto: try the fast presets in random order; one that times out or drops is not retried until all others were tried.
  // A chosen preset (including the slow manual-only ones) is simply retried.
  Future<void> _run(Cancel ct) async {
    final untried = <Preset>[];
    try {
      if (!File(Aether.exe).existsSync())
        return _log('aether binary missing: run fetch_aether.sh and rebuild.');
      while (true) {
        if (untried.isEmpty) {
          untried.addAll(
            _preset < 0
                ? Aether.presets.where((p) => !p.manualOnly)
                : [Aether.presets[_preset]],
          );
        }
        await _connectAndMonitor(
          untried.removeAt(_rng.nextInt(untried.length)),
          ct,
        );
        await ct.delay(1000);
      }
    } on Cancelled {
      // user pressed Disconnect
    } finally {
      Aether.vpnStop();
      _killProcess();
      _setStatus('Disconnected', red);
      _setDelay('Delay: -');
    }
  }

  // Returns when the preset fails to connect within the timeout, or after a connected session drops.
  Future<void> _connectAndMonitor(Preset p, Cancel ct) async {
    _log('Trying ${p.name}...');
    _setStatus('Connecting: ${p.name}', orange);
    final proc = _process = await Aether.start(p, _log);
    if (proc == null) return;

    int? ms = await Aether.waitConnected(proc, p.startupSecs, ct);
    if (ms == null) {
      _log(
        proc.exited
            ? '${p.name}: process exited.'
            : '${p.name}: no internet, switching.',
      );
      _killProcess();
      return;
    }

    _setStatus('Connected: ${p.name}', seaGreen);
    _log('Connected via ${p.name}, delay $ms ms.');
    _log(
      await Aether.vpnStart()
          ? 'VPN on: all apps go through aether.'
          : 'VPN permission denied: proxy only.',
    );
    for (var fails = 0; fails < 2;) {
      _setDelay(ms == null ? 'Delay: timeout' : 'Delay: $ms ms');
      await ct.delay(5000);
      if (proc.exited) break;
      ms = await Aether.ping(ct);
      fails = ms == null ? fails + 1 : 0;
    }
    _log('${p.name}: connection lost, switching.');
    _killProcess();
  }

  void _killProcess() {
    _process?.p.kill(ProcessSignal.sigkill);
    _process = null;
  }

  void _showProxyInfo() {
    const head = Colors.cyan, info = Colors.white, tip = Color(0xFFFFD700);
    final secrets = Directory(Aether.dir)
        .listSync()
        .where((f) => RegExp(r'[/\\]aether[^/\\]*\.toml$').hasMatch(f.path))
        .length;
    _log('=== Proxy info ===', head);
    _log(
      'HTTP 127.0.0.1:${Aether.httpPort} | SOCKS5 127.0.0.1:${Aether.socksPort}',
      info,
    );
    _log(
      'aether: ${_process != null && !_process!.exited ? 'running' : 'stopped'} | $_status | $_delay',
      info,
    );
    _log('Presets: ${Aether.presets.map((p) => p.name).join(', ')}', info);
    _log(
      'Cleanup: Disconnect, or Android stops aether together with the app.',
      info,
    );
    _log(
      'Credits: aether by CluvexStudio (github.com/CluvexStudio) | VPN via hev-socks5-tunnel (heiher)',
      info,
    );
    _log('=== Recommendations ===', head);
    _log(
      '1. Allow the VPN prompt: it routes browsers/most apps through aether on any network.',
      tip,
    );
    _log(
      '2. $secrets aether*.toml file(s) in app storage hold private keys/tokens: rotate keys if the device is shared.',
      tip,
    );
    _log(
      '3. Auto only rotates the fast protocols (30s each). Tor/Psiphon are manual: pick them on strict networks.',
      tip,
    );
  }

  void _setStatus(String text, Color color) {
    if (mounted)
      setState(() {
        _status = text;
        _statusColor = color;
      });
  }

  void _setDelay(String text) {
    if (mounted) setState(() => _delay = text);
  }

  void _log(String msg, [Color? color]) {
    if (!mounted) return;
    final t = DateTime.now().toIso8601String().substring(11, 19);
    setState(() => _lines.add(('[$t] $msg', color)));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  // Color is never the only signal: each state also has its own icon and text.
  IconData get _statusIcon => switch (_statusColor) {
    seaGreen => Icons.check_circle,
    orange => Icons.autorenew,
    _ => Icons.cancel,
  };

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final busy = _ct != null;
    final connecting = busy && _statusColor == orange;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);
    final motion = reduceMotion
        ? Duration.zero
        : const Duration(milliseconds: 250);

    final controls = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 16),
        Center(
          child: SizedBox.square(
            dimension: 164,
            child: Stack(
              alignment: Alignment.center,
              children: [
                // progress ring while a protocol is being tried; static under reduced motion
                if (connecting)
                  SizedBox.expand(
                    child: CircularProgressIndicator(
                      strokeWidth: 4,
                      color: orange,
                      value: reduceMotion ? 1 : null,
                    ),
                  ),
                Semantics(
                  button: true,
                  label: busy ? 'Disconnect' : 'Connect',
                  child: AnimatedContainer(
                    duration: motion,
                    width: 140,
                    height: 140,
                    decoration: BoxDecoration(
                      color: _statusColor,
                      shape: BoxShape.circle,
                      boxShadow: [
                        BoxShadow(
                          color: _statusColor.withValues(alpha: 0.35),
                          blurRadius: 24,
                        ),
                      ],
                    ),
                    child: Material(
                      type: MaterialType.transparency,
                      child: InkWell(
                        customBorder: const CircleBorder(),
                        onTap: _connect,
                        // dark glyph: white on the green/amber fills falls under 3:1
                        child: const Icon(
                          Icons.power_settings_new,
                          size: 64,
                          color: Palette.background,
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            ExcludeSemantics(
              child: Icon(_statusIcon, color: _statusColor, size: 22),
            ),
            const SizedBox(width: 8),
            Flexible(
              child: Text(
                _status,
                style: text.titleLarge?.copyWith(color: _statusColor),
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          _delay,
          textAlign: TextAlign.center,
          style: text.bodyMedium?.copyWith(color: Palette.mutedText),
        ),
        const SizedBox(height: 24),
        DropdownMenu<int>(
          label: const Text('Protocol'),
          leadingIcon: const Icon(Icons.vpn_lock),
          expandedInsets: EdgeInsets.zero,
          enabled: !busy, // pick before connecting; Disconnect to change
          initialSelection: _preset,
          dropdownMenuEntries: [
            const DropdownMenuEntry(value: -1, label: 'Auto (fast protocols)'),
            for (final (i, p) in Aether.presets.indexed)
              DropdownMenuEntry(value: i, label: p.label),
          ],
          onSelected: (v) {
            setState(() => _preset = v ?? -1);
            _saveSettings();
          },
        ),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: const Text('Connect on startup'),
          value: _autoConnect,
          onChanged: (v) {
            setState(() => _autoConnect = v);
            _saveSettings();
          },
        ),
      ],
    );

    return Scaffold(
      appBar: AppBar(
        title: const Text('Aether VPN'),
        actions: [
          IconButton(
            tooltip: 'Proxy info',
            icon: const Icon(Icons.info_outline),
            onPressed: _showProxyInfo,
          ),
        ],
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          // controls take their natural height, the log fills the rest; on small phones,
          // landscape or large text the whole page scrolls instead of clipping the controls
          child: CustomScrollView(
            slivers: [
              SliverToBoxAdapter(child: controls),
              // fills the leftover height, but never shrinks below 240 so the log stays usable
              SliverFillRemaining(
                hasScrollBody: false,
                child: SizedBox(
                  height: 240,
                  child: Padding(
                    padding: const EdgeInsets.only(top: 8, bottom: 16),
                    child: Card.filled(
                      color: Palette.muted,
                      margin: EdgeInsets.zero,
                      clipBehavior: Clip.antiAlias,
                      child: SelectionArea(
                        child: ListView.builder(
                          controller: _scroll,
                          padding: const EdgeInsets.all(8),
                          itemCount: _lines.length,
                          itemBuilder: (_, i) => Text(
                            _lines[i].$1,
                            style: TextStyle(
                              color: _lines[i].$2 ?? Palette.mutedText,
                              fontFamily: 'monospace',
                              fontSize: 12,
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
