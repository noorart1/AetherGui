import 'dart:io';
import 'dart:math';

import 'package:flutter/material.dart';

import 'aether.dart';
import 'core_options.dart';
import 'core_settings_page.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Aether.init();
  runApp(
    MaterialApp(
      title: 'Aether VPN',
      debugShowCheckedModeBanner: false,
      theme: Palette.theme,
      home: const HomePage(),
    ),
  );
}

// One place for every color, and the dark theme built from them.
abstract final class Palette {
  static const background = Color(0xFF121212);
  static const surface = Color(0xFF1E1E1E);
  static const accent = Color(0xFFB585FF); // neon purple
  static const secondaryText = Color(0xFF9E9E9E);
  static const connected = Color(0xFF4ADE80); // neon green
  static const connecting = Color(0xFFFBBF24);
  static const disconnected = Color(0xFFF87171); // muted coral

  static final theme = ThemeData(
    brightness: Brightness.dark,
    scaffoldBackgroundColor: background,
    colorScheme: ColorScheme.fromSeed(
      seedColor: accent,
      brightness: Brightness.dark,
    ).copyWith(primary: accent, surface: background),
    appBarTheme: const AppBarTheme(
      backgroundColor: background,
      surfaceTintColor: Colors.transparent,
    ),
    cardTheme: CardThemeData(
      color: surface,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
    ),
    listTileTheme: const ListTileThemeData(
      contentPadding: EdgeInsets.zero,
      subtitleTextStyle: TextStyle(color: secondaryText, fontSize: 13),
    ),
  );
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
  CoreOptions _coreOptions = const CoreOptions();
  int _preset = -1; // index into Aether.presets, -1 = Auto (rotate through all)
  String _scan = 'turbo'; // AETHER_SCAN: turbo or balanced
  String _status = 'Disconnected';
  Color _statusColor = red;
  String _delay = ''; // e.g. '24 ms', shown in the button while connected
  String? _route; // preset being tried or used right now
  final _changes = ValueNotifier(0); // tells an open Settings page to rebuild
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
      _preset = i >= -1 && i < Aether.presets.length ? i : -1;
      if (s.contains('Scan=balanced')) _scan = 'balanced';
    } catch (_) {}
    try {
      _coreOptions = CoreOptions.load(Aether.dir);
    } catch (_) {
      _autoConnect = false;
      _log('Could not load core settings. Review settings before connecting.');
    }
    _checkUpdates(interactive: false);
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
    _scroll.dispose();
    _changes.dispose();
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
    'AutoConnect=${_autoConnect ? 1 : 0}\nPreset=$_preset\nScan=$_scan\n',
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
      _setDelay('');
      _route = null;
    }
  }

  // Returns when the preset fails to connect within the timeout, or after a connected session drops.
  Future<void> _connectAndMonitor(Preset p, Cancel ct) async {
    _log('Trying ${p.name}...');
    _route = p.name;
    _setStatus('Connecting: ${p.name}', orange);
    final proc = _process = await Aether.start(p, _log, _coreOptions, _scan);
    if (proc == null) return;

    int? ms = await Aether.waitConnected(
      proc,
      _coreOptions.isConfigured ? _coreOptions.startupSeconds : p.startupSecs,
      ct,
    );
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
      _setDelay(ms == null ? 'timeout' : '$ms ms');
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

  Future<void> _showCoreSettings() async {
    final options = await Navigator.push<CoreOptions>(
      context,
      MaterialPageRoute(
        builder: (_) => CoreSettingsPage(options: _coreOptions),
      ),
    );
    if (mounted && options != null) setState(() => _coreOptions = options);
  }

  Future<void> _sendCoreInput() async {
    final proc = _process;
    if (proc == null || proc.exited) return;
    final input = TextEditingController();
    final value = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Core input'),
        content: TextField(
          controller: input,
          autofocus: true,
          obscureText: true,
          autocorrect: false,
          enableSuggestions: false,
          decoration: const InputDecoration(
            labelText: 'Code or prompt response',
          ),
          onSubmitted: (value) => Navigator.pop(context, value),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, input.text),
            child: const Text('Send'),
          ),
        ],
      ),
    );
    // Let the closing dialog finish before disposing its controller.
    await Future<void>.delayed(const Duration(milliseconds: 300));
    input.dispose();
    if (!mounted || value == null || value.isEmpty) return;
    if (_process != proc || proc.exited) {
      _log('Core changed while input was open. Input was not sent.');
      return;
    }
    try {
      proc.p.stdin.writeln(value);
      await proc.p.stdin.flush();
      _log('Input sent to core.');
    } catch (_) {
      _log('Could not send input to core.');
    }
  }

  // The startup check only logs a new release; the button also offers to open its page.
  Future<void> _checkUpdates({required bool interactive}) async {
    if (interactive) _log('Checking for updates...');
    try {
      final have = await Aether.appVersion();
      final latest = await Aether.latestGuiRelease();
      final mine = Aether.parseVersion(have),
          theirs = Aether.parseVersion(latest?.$1);
      if (!mounted) return;
      if (latest == null ||
          mine == null ||
          theirs == null ||
          !Aether.isNewer(theirs, mine)) {
        if (interactive) _log('Up to date: Aether $have.');
        return;
      }
      _log('Aether ${latest.$1} is available (you have $have).', Colors.blue);
      if (!interactive || !latest.$2.startsWith('https://github.com/')) return;
      final open = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Update available'),
          content: Text(
            'Aether ${latest.$1} is available. Open the download page?',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Later'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Open'),
            ),
          ],
        ),
      );
      if (open == true) await Aether.openUrl(latest.$2);
    } catch (e) {
      if (interactive) _log('Update check failed: $e');
    }
  }

  void _showProxyInfo() {
    const head = Colors.blue, info = null, tip = Colors.orange;
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
      'aether: ${_process != null && !_process!.exited ? 'running' : 'stopped'} | $_status | delay $_delay',
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

  @override
  void setState(VoidCallback fn) {
    super.setState(fn);
    _changes.value++;
  }

  void _log(String msg, [Color? color]) {
    if (!mounted) return;
    final t = DateTime.now().toIso8601String().substring(11, 19);
    setState(() => _lines.add(('[$t] $msg', color)));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  void _openSettings() => Navigator.push(
    context,
    MaterialPageRoute(
      builder: (_) => ListenableBuilder(
        listenable: _changes,
        builder: (context, _) => _settingsPage(context),
      ),
    ),
  );

  // ---------- Home ----------

  @override
  Widget build(BuildContext context) {
    final busy = _ct != null;
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 72,
        title: const Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Aether', style: TextStyle(fontWeight: FontWeight.w700)),
            Text(
              'Private Network Client',
              style: TextStyle(fontSize: 12, color: Palette.secondaryText),
            ),
          ],
        ),
        actions: [
          IconButton(
            tooltip: 'Settings',
            icon: const Icon(Icons.settings_outlined),
            onPressed: _openSettings,
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const SizedBox(height: 16),
                  Center(child: _powerButton(busy)),
                  const SizedBox(height: 24),
                  _routeLine(busy),
                  const SizedBox(height: 32),
                  _quickSettings(),
                  const SizedBox(height: 16),
                  _statusBanner(busy),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  // The big round button: red when off, amber with a spinning ring while connecting, green when on.
  // Color is never the only signal: each state also has its own icon and text.
  Widget _powerButton(bool busy) {
    final connected = _statusColor == seaGreen;
    final connecting = busy && !connected;
    final color = _statusColor;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);
    final (icon, label, detail) = connected
        ? (Icons.shield_rounded, 'Connected', _delay)
        : connecting
        ? (Icons.sync_rounded, 'Connecting...', 'Tap to cancel')
        : (Icons.power_settings_new_rounded, 'Tap to Connect', '');
    return Semantics(
      button: true,
      label: busy ? 'Disconnect' : 'Connect',
      child: SizedBox.square(
        dimension: 228,
        child: Stack(
          alignment: Alignment.center,
          children: [
            if (connecting)
              SizedBox.expand(
                child: CircularProgressIndicator(
                  strokeWidth: 3,
                  color: color,
                  value: reduceMotion ? 1 : null,
                ),
              ),
            AnimatedContainer(
              duration: reduceMotion
                  ? Duration.zero
                  : const Duration(milliseconds: 300),
              width: 200,
              height: 200,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: Palette.surface,
                border: Border.all(color: color, width: 3),
                boxShadow: [
                  BoxShadow(
                    color: color.withValues(alpha: 0.35),
                    blurRadius: 40,
                    spreadRadius: 4,
                  ),
                ],
              ),
              child: Material(
                type: MaterialType.transparency,
                shape: const CircleBorder(),
                clipBehavior: Clip.antiAlias,
                child: InkWell(
                  onTap: _connect,
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(icon, size: 64, color: color),
                      const SizedBox(height: 12),
                      Text(
                        label,
                        style: const TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      if (detail.isNotEmpty)
                        Text(
                          detail,
                          style: const TextStyle(color: Palette.secondaryText),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  // Which route is used: the one running now, else the saved choice. Change opens Settings.
  Widget _routeLine(bool busy) {
    final route =
        _route ??
        (_preset < 0 ? 'Auto - fastest route' : Aether.presets[_preset].name);
    return Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        const Icon(Icons.bolt_rounded, size: 20, color: Palette.accent),
        const SizedBox(width: 8),
        Flexible(child: Text(route, overflow: TextOverflow.ellipsis)),
        TextButton(
          onPressed: busy ? null : _openSettings,
          child: const Text('Change'),
        ),
      ],
    );
  }

  Widget _quickSettings() => Card(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Quick Settings',
            style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 8),
          SwitchListTile(
            secondary: const Icon(Icons.bolt_outlined),
            title: const Text('Auto-Connect on Startup'),
            value: _autoConnect,
            onChanged: (v) {
              setState(() => _autoConnect = v);
              _saveSettings();
            },
          ),
          // Android, not the app, can block traffic while the VPN is down, so this opens its setting.
          const ListTile(
            leading: Icon(Icons.block_rounded),
            title: Text('Kill Switch'),
            subtitle: Text(
              'Turn on "Always-on VPN" and "Block connections without VPN"',
            ),
            trailing: Icon(Icons.open_in_new_rounded, size: 20),
            onTap: Aether.vpnSettings,
          ),
        ],
      ),
    ),
  );

  Widget _statusBanner(bool busy) {
    final connected = _statusColor == seaGreen;
    final (icon, text, color) = connected
        ? (Icons.check_circle_rounded, 'Your connection is secure', seaGreen)
        : busy
        ? (Icons.sync_rounded, 'Connecting...', orange)
        : (Icons.error_outline_rounded, 'You are not protected', red);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(24),
      ),
      child: Row(
        children: [
          Icon(icon, color: color),
          const SizedBox(width: 12),
          Expanded(child: Text(text)),
        ],
      ),
    );
  }

  // ---------- Settings: everything technical lives here ----------

  Widget _settingsPage(BuildContext context) {
    final busy = _ct != null;
    Widget section(String title) => Padding(
      padding: const EdgeInsets.fromLTRB(4, 24, 4, 8),
      child: Text(
        title,
        style: const TextStyle(
          color: Palette.accent,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
        children: [
          section('Connection'),
          DropdownMenu<int>(
            label: const Text('Protocol'),
            leadingIcon: const Icon(Icons.vpn_lock_outlined),
            expandedInsets: EdgeInsets.zero,
            enabled: !busy, // pick before connecting; Disconnect to change
            initialSelection: _preset,
            dropdownMenuEntries: [
              const DropdownMenuEntry(
                value: -1,
                label: 'Auto (fast protocols)',
              ),
              // Keep persisted values stable while displaying fast presets first.
              for (final manualOnly in [false, true])
                for (final (i, p) in Aether.presets.indexed)
                  if (p.manualOnly == manualOnly)
                    DropdownMenuEntry(value: i, label: p.label),
            ],
            onSelected: (v) {
              setState(() => _preset = v ?? -1);
              _saveSettings();
            },
          ),
          const SizedBox(height: 16),
          const Text('Server search'),
          const SizedBox(height: 8),
          SegmentedButton<String>(
            segments: const [
              ButtonSegment(
                value: 'turbo',
                label: Text('Fast'),
                tooltip: 'First server that answers',
              ),
              ButtonSegment(
                value: 'balanced',
                label: Text('Balanced'),
                tooltip: 'Tries a few servers, keeps the fastest',
              ),
            ],
            selected: {_scan},
            onSelectionChanged: busy
                ? null
                : (v) {
                    setState(() => _scan = v.first);
                    _saveSettings();
                  },
          ),
          section('Advanced'),
          ListTile(
            leading: const Icon(Icons.tune_rounded),
            title: const Text('Core settings'),
            enabled: !busy,
            onTap: _showCoreSettings,
          ),
          ListTile(
            leading: const Icon(Icons.keyboard_outlined),
            title: const Text('Core input'),
            subtitle: const Text('Send a sign-in code the core asks for'),
            enabled: busy,
            onTap: _sendCoreInput,
          ),
          ListTile(
            leading: const Icon(Icons.info_outline_rounded),
            title: const Text('Proxy info'),
            onTap: _showProxyInfo,
          ),
          ListTile(
            leading: const Icon(Icons.system_update_outlined),
            title: const Text('Check for updates'),
            onTap: () => _checkUpdates(interactive: true),
          ),
          section('Activity'),
          Card(
            clipBehavior: Clip.antiAlias,
            child: SizedBox(
              height: 320,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Align(
                    alignment: Alignment.centerRight,
                    child: IconButton(
                      tooltip: 'Clear activity',
                      onPressed: _lines.isEmpty
                          ? null
                          : () => setState(_lines.clear),
                      icon: const Icon(Icons.delete_outline_rounded),
                    ),
                  ),
                  Expanded(
                    child: SelectionArea(
                      child: ListView.builder(
                        controller: _scroll,
                        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                        itemCount: _lines.isEmpty ? 1 : _lines.length,
                        itemBuilder: (_, i) => Padding(
                          padding: const EdgeInsets.only(bottom: 4),
                          child: Text(
                            _lines.isEmpty
                                ? 'Waiting for logs...'
                                : _lines[i].$1,
                            style: TextStyle(
                              color: _lines.isEmpty
                                  ? Palette.secondaryText
                                  : _lines[i].$2 ?? Palette.secondaryText,
                              fontFamily: 'monospace',
                              fontSize: 12,
                              height: 1.45,
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
        ],
      ),
    );
  }
}
